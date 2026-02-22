using Mk8.Shell.Models;

namespace Mk8.Shell.Startup;

/// <summary>
/// Registers a new mk8.shell sandbox on the local machine. This is the
/// ONLY way to create a sandbox — mk8.shell itself never writes to the
/// sandbox env files.
/// <para>
/// Creates the following in the specified directory:
/// <list type="bullet">
///   <item><c>mk8.shell.env</c> — user-editable env file (never read by mk8.shell at runtime)</item>
///   <item><c>mk8.shell.signed.env</c> — cryptographically signed copy, verified on every command</item>
/// </list>
/// Registers the sandbox ID → path mapping in
/// <c>%APPDATA%/mk8.shell/sandboxes.json</c> and archives the
/// initial signed env into <c>%APPDATA%/mk8.shell/history/</c>.
/// </para>
/// </summary>
public static class Mk8SandboxRegistrar
{
    /// <summary>
    /// Registers a new sandbox at the given directory path.
    /// </summary>
    /// <param name="sandboxId">
    /// Unique identifier for this sandbox (e.g. "Banana"). Must be
    /// alphanumeric + underscores only.
    /// </param>
    /// <param name="directoryPath">
    /// Absolute path to the sandbox root. Must be either a new
    /// (non-existent) directory or a completely empty directory.
    /// </param>
    /// <param name="initialEnvVars">
    /// Initial environment variables to bake into the sandbox env.
    /// If <c>null</c>, an empty env is created.
    /// </param>
    /// <param name="registry">
    /// Local registry. If <c>null</c>, uses the default
    /// <c>%APPDATA%/mk8.shell</c> location.
    /// </param>
    /// <returns>The created <see cref="Mk8Sandbox"/>.</returns>
    public static Mk8Sandbox Register(
        string sandboxId,
        string directoryPath,
        IReadOnlyDictionary<string, string>? initialEnvVars = null,
        Mk8SandboxRegistry? registry = null)
    {
        // ── Validate sandbox ID ───────────────────────────────────
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ValidateSandboxId(sandboxId);

        // ── Validate directory ────────────────────────────────────
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var rootPath = Path.GetFullPath(directoryPath);

        if (Directory.Exists(rootPath))
        {
            var entries = Directory.EnumerateFileSystemEntries(rootPath);
            if (entries.Any())
                throw new InvalidOperationException(
                    $"Directory '{rootPath}' is not empty. Sandbox root " +
                    "must be a new or empty directory.");
        }
        else
        {
            Directory.CreateDirectory(rootPath);
        }

        // ── Initialize registry ───────────────────────────────────
        registry ??= new Mk8SandboxRegistry();
        registry.EnsureInitialized();

        // ── Check for duplicate ───────────────────────────────────
        var sandboxes = registry.LoadSandboxes();
        if (sandboxes.ContainsKey(sandboxId))
            throw new InvalidOperationException(
                $"Sandbox '{sandboxId}' is already registered. " +
                "Unregister it first or choose a different ID.");

        // ── Build env content ─────────────────────────────────────
        var envVars = initialEnvVars is not null
            ? new Dictionary<string, string>(initialEnvVars)
            : new Dictionary<string, string>();

        // Always include the sandbox ID as a variable.
        envVars.TryAdd("MK8_SANDBOX_ID", sandboxId);

        var envContent = Mk8SandboxEnvParser.Serialize(envVars);

        // ── Write the user-editable .env ──────────────────────────
        var envFilePath = Path.Combine(
            rootPath, Mk8SandboxRegistry.SandboxEnvFileName);
        File.WriteAllText(envFilePath, envContent);

        // ── Sign and write the signed .env ────────────────────────
        var key = registry.LoadKey();
        var signedContent = Mk8SandboxEnvSigner.Sign(envContent, key);

        var signedEnvFilePath = Path.Combine(
            rootPath, Mk8SandboxRegistry.SandboxSignedEnvFileName);
        File.WriteAllText(signedEnvFilePath, signedContent);

        // ── Archive in history ────────────────────────────────────
        registry.ArchiveSignedEnv(sandboxId, signedContent);

        // ── Register in sandboxes.json ────────────────────────────
        var now = DateTimeOffset.UtcNow;
        sandboxes[sandboxId] = new Mk8SandboxEntry
        {
            RootPath = rootPath,
            RegisteredAtUtc = now,
        };
        registry.SaveSandboxes(sandboxes);

        return new Mk8Sandbox
        {
            Id = sandboxId,
            RootPath = rootPath,
            RegisteredAtUtc = now,
        };
    }

    /// <summary>
    /// Re-signs a sandbox env after the user has edited <c>mk8.shell.env</c>.
    /// Reads the current <c>.env</c>, signs it, overwrites
    /// <c>.signed.env</c>, and archives the new signed copy.
    /// </summary>
    public static void Resign(
        string sandboxId,
        Mk8SandboxRegistry? registry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);

        registry ??= new Mk8SandboxRegistry();
        registry.EnsureInitialized();

        var entry = registry.Resolve(sandboxId);
        var rootPath = Path.GetFullPath(entry.RootPath);

        var envFilePath = Path.Combine(
            rootPath, Mk8SandboxRegistry.SandboxEnvFileName);

        if (!File.Exists(envFilePath))
            throw new FileNotFoundException(
                $"Cannot re-sign: '{Mk8SandboxRegistry.SandboxEnvFileName}' " +
                $"not found in '{rootPath}'.", envFilePath);

        var envContent = File.ReadAllText(envFilePath);
        var key = registry.LoadKey();
        var signedContent = Mk8SandboxEnvSigner.Sign(envContent, key);

        var signedEnvFilePath = Path.Combine(
            rootPath, Mk8SandboxRegistry.SandboxSignedEnvFileName);
        File.WriteAllText(signedEnvFilePath, signedContent);

        registry.ArchiveSignedEnv(sandboxId, signedContent);
    }

    // ── Validation ────────────────────────────────────────────────

    private static void ValidateSandboxId(string id)
    {
        if (id.Length > 64)
            throw new ArgumentException(
                "Sandbox ID must be 64 characters or fewer.", nameof(id));

        foreach (var c in id)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                throw new ArgumentException(
                    $"Sandbox ID contains invalid character '{c}'. " +
                    "Only English letters (A-Z, a-z) and digits (0-9) are allowed.",
                    nameof(id));
        }
    }
}
