using System.Reflection;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using SharpClaw.Contracts.Modules;
using SharpClaw.Utils.Security;

namespace SharpClaw.Application.Core.Modules;

/// <summary>
/// Singleton that holds all bundled (default) module instances and provides
/// manifest loading. Modules are always instantiated, but only registered
/// with <see cref="ModuleRegistry"/> when enabled.
/// </summary>
public sealed class ModuleLoader
{
    private readonly Dictionary<string, ISharpClawModule> _bundled;
    private readonly HashSet<string> _manifestOnlyBundledIds;
    private Dictionary<string, ModuleManifest>? _manifestsCache;
    private IServiceProvider? _rootServices;

    public ModuleLoader(params ISharpClawModule[] modules)
        : this(modules, manifests: null, manifestOnlyBundledIds: [])
    {
    }

    private ModuleLoader(
        IEnumerable<ISharpClawModule> modules,
        Dictionary<string, ModuleManifest>? manifests,
        IEnumerable<string> manifestOnlyBundledIds)
    {
        _bundled = modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _manifestsCache = manifests;
        _manifestOnlyBundledIds = [.. manifestOnlyBundledIds];
    }

    /// <summary>
    /// Discover bundled modules from manifests and in-process module assemblies.
    /// Sidecar-manifest modules are represented by manifest metadata unless the
    /// host explicitly forces .NET sidecars back in-process.
    /// </summary>
    public static ModuleLoader DiscoverBundled(IConfiguration? configuration = null)
    {
        var manifests = LoadBundledManifestsFromDisk();
        var runtimeInfos = LoadBundledRuntimeInfosFromDisk();
        var hostingMode = DotNetModuleHostingModeOptions.Resolve(configuration);
        var manifestOnlyManifests = hostingMode == DotNetModuleHostingMode.InProcess
            ? Array.Empty<ModuleManifest>()
            : manifests.Values
                .Where(manifest =>
                    runtimeInfos.TryGetValue(manifest.Id, out var runtimeInfo)
                    && IsDotNetSidecarManifest(runtimeInfo))
                .ToArray();
        var manifestOnlyIds = manifestOnlyManifests
            .Select(manifest => manifest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var manifestOnlyAssemblyNames = manifestOnlyManifests
            .Select(manifest => Path.GetFileNameWithoutExtension(manifest.EntryAssembly))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Module assemblies are only present in the output directory via ProjectReference
        // but are NOT loaded into the AppDomain unless something forces type resolution.
        // Explicitly load matching DLLs for modules that still require in-process composition.
        var baseDir = ResolveApplicationBaseDirectory();
        foreach (var dll in Directory.GetFiles(baseDir, "SharpClaw.Modules.*.dll"))
        {
            try
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dll);
                if (manifestOnlyAssemblyNames.Contains(assemblyName))
                    continue;

                // Validate the enumerated path stays inside the base directory.
                var safeDll = PathGuard.EnsureContainedIn(dll, baseDir);
                Assembly.LoadFrom(safeDll);
            }
            catch
            {
                // Skip assemblies that fail to load (e.g. native-only, already loaded
                // under a different identity, or missing transitive dependencies).
            }
        }

        var moduleType = typeof(ISharpClawModule);
        var modules = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Where(a => !manifestOnlyAssemblyNames.Contains(a.GetName().Name ?? ""))
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && moduleType.IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (ISharpClawModule)Activator.CreateInstance(t!)!)
            .ToList();

        foreach (var manifest in manifestOnlyManifests)
        {
            if (modules.Any(module => string.Equals(module.Id, manifest.Id, StringComparison.Ordinal)))
                continue;

            modules.Add(new ManifestOnlyBundledModule(manifest));
        }

        return new ModuleLoader(modules, manifests, manifestOnlyIds);
    }

    /// <summary>
    /// The root <see cref="IServiceProvider"/> set after the host is built.
    /// Used by <see cref="ModuleService.EnableAsync"/> to run module initialization.
    /// </summary>
    public IServiceProvider RootServices =>
        _rootServices ?? throw new InvalidOperationException("RootServices not yet set — call SetRootServices after Build.");

    /// <summary>Assign the built <see cref="IServiceProvider"/>. Called once from Program.cs after Build.</summary>
    public void SetRootServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _rootServices = services;
    }

    /// <summary>Get a bundled module by its ID, or <c>null</c> if unknown.</summary>
    public ISharpClawModule? GetBundledModule(string moduleId)
        => _bundled.GetValueOrDefault(moduleId);

    /// <summary>Get all bundled module instances (regardless of enabled state).</summary>
    public IReadOnlyList<ISharpClawModule> GetAllBundled() => [.. _bundled.Values];

    /// <summary>Whether a module ID refers to a known default module.</summary>
    public bool IsDefaultModule(string moduleId) => _bundled.ContainsKey(moduleId);

    /// <summary>
    /// Whether the bundled module is represented only by manifest metadata in
    /// the parent host and must be launched through its sidecar runtime.
    /// </summary>
    public bool IsManifestOnlyBundledModule(string moduleId) =>
        _manifestOnlyBundledIds.Contains(moduleId);

    /// <summary>
    /// Check whether a module is enabled in the current <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// Returns <c>true</c> only when the key <c>Modules:{moduleId}</c> is explicitly <c>"true"</c>.
    /// Missing or any other value means disabled.
    /// </summary>
    public static bool IsEnabledInConfig(string moduleId, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var value = config[$"Modules:{moduleId}"];
        return bool.TryParse(value, out var enabled) && enabled;
    }

    // ═══════════════════════════════════════════════════════════════
    // Manifest loading
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Scan the <c>modules/</c> subdirectories next to the host assembly for
    /// <c>module.json</c> manifests. Results are cached after the first call.
    /// </summary>
    public IReadOnlyDictionary<string, ModuleManifest> LoadAllManifests()
    {
        if (_manifestsCache is not null) return _manifestsCache;

        _manifestsCache = LoadBundledManifestsFromDisk();
        return _manifestsCache;
    }

    private static Dictionary<string, ModuleManifest> LoadBundledManifestsFromDisk()
    {
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        var modulesDir = Path.Combine(
            ResolveApplicationBaseDirectory(),
            ModuleFileNames.BundledModulesDir);

        if (!Directory.Exists(modulesDir)) return manifests;

        foreach (var dir in Directory.EnumerateDirectories(modulesDir))
        {
            var safeDir = PathGuard.EnsureContainedIn(dir, modulesDir);
            var manifestPath = Path.Combine(safeDir, ModuleFileNames.ManifestFile);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, SecureJsonOptions.Manifest);
                if (manifest is not null)
                    manifests[manifest.Id] = manifest;
            }
            catch
            {
                // Skip malformed manifests — logged at a higher level if needed.
            }
        }

        return manifests;
    }

    private static Dictionary<string, ModuleManifestRuntimeInfo> LoadBundledRuntimeInfosFromDisk()
    {
        var runtimeInfos = new Dictionary<string, ModuleManifestRuntimeInfo>(StringComparer.Ordinal);
        var modulesDir = Path.Combine(
            ResolveApplicationBaseDirectory(),
            ModuleFileNames.BundledModulesDir);

        if (!Directory.Exists(modulesDir)) return runtimeInfos;

        foreach (var dir in Directory.EnumerateDirectories(modulesDir))
        {
            var manifestPath = Path.Combine(dir, ModuleFileNames.ManifestFile);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, SecureJsonOptions.Manifest);
                if (manifest is not null)
                    runtimeInfos[manifest.Id] = ModuleManifestRuntimeInfo.FromJson(json);
            }
            catch
            {
                // Ignore malformed manifests during discovery; external load reports errors explicitly.
            }
        }

        return runtimeInfos;
    }

    /// <summary>Get a cached manifest for a specific module, or <c>null</c>.</summary>
    public ModuleManifest? GetManifest(string moduleId)
    {
        var all = LoadAllManifests();
        return all.GetValueOrDefault(moduleId);
    }

    private static bool IsDotNetSidecarManifest(ModuleManifestRuntimeInfo runtimeInfo) =>
        runtimeInfo.IsDotNet && runtimeInfo.IsSidecarHostMode;

    private static string ResolveApplicationBaseDirectory() =>
        Path.GetDirectoryName(typeof(ModuleLoader).Assembly.Location)!;

    private sealed class ManifestOnlyBundledModule(ModuleManifest manifest) : ISharpClawModule
    {
        public string Id => manifest.Id;
        public string DisplayName => manifest.DisplayName;
        public string ToolPrefix => manifest.ToolPrefix;

        public IReadOnlyList<ModuleContractRequirement> RequiredContracts =>
            [.. (manifest.Requires ?? []).Select(requirement =>
                new ModuleContractRequirement(
                    requirement.ContractName,
                    ResolveKnownType(requirement.ServiceType),
                    requirement.Optional))];

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
        }

        public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

        public Task<string> ExecuteToolAsync(
            string toolName,
            JsonElement parameters,
            AgentJobContext job,
            IServiceProvider sp,
            CancellationToken ct) =>
            throw new InvalidOperationException(
                $"Bundled module '{Id}' is manifest-only in the parent host and must be executed through its sidecar.");

        private static Type? ResolveKnownType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            return Type.GetType(typeName, throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic)
                    .Select(assembly => assembly.GetType(typeName, throwOnError: false))
                    .FirstOrDefault(type => type is not null);
        }
    }
}
