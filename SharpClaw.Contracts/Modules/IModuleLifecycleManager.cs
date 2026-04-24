namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side interface for external module lifecycle operations (load, unload,
/// reload) and tool lookup. Implemented by <c>ModuleService</c>; injected into
/// modules that need to manage or introspect the module roster at runtime.
/// </summary>
public interface IModuleLifecycleManager
{
    /// <summary>
    /// Absolute path to the external-modules root directory.
    /// Modules use this as the sandbox boundary for path validation.
    /// </summary>
    string ExternalModulesDir { get; }

    /// <summary>Returns <c>true</c> if a module with the given ID is registered.</summary>
    bool IsModuleRegistered(string moduleId);

    /// <summary>Returns <c>true</c> if a module with the given tool prefix is registered.</summary>
    bool IsToolPrefixRegistered(string toolPrefix);

    /// <summary>
    /// Finds a tool by its fully-qualified name across all loaded modules.
    /// Returns the owning module's <see cref="ISharpClawModule"/> and the resolved
    /// tool name, or <c>null</c> if no match is found.
    /// </summary>
    (ISharpClawModule Module, string ToolName)? FindToolByName(string toolName);

    /// <summary>Loads an external module from the given directory.</summary>
    Task<ModuleStateResponse> LoadExternalAsync(
        string moduleDir, IServiceProvider hostServices, CancellationToken ct = default);

    /// <summary>Unloads the external module with the given ID.</summary>
    Task UnloadExternalAsync(string moduleId, CancellationToken ct = default);

    /// <summary>Unloads then reloads the external module with the given ID.</summary>
    Task<ModuleStateResponse> ReloadExternalAsync(
        string moduleId, IServiceProvider hostServices, CancellationToken ct = default);
}
