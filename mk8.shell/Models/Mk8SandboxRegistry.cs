using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Shell.Models;

/// <summary>
/// Manages the local <c>%APPDATA%/mk8.shell</c> directory which contains:
/// <list type="bullet">
///   <item><c>sandboxes.json</c> — sandbox ID → local root path mapping</item>
///   <item><c>mk8.shell.key</c> — encrypted signing key for env verification</item>
///   <item><c>history/</c> — timestamped backups of every sandbox env signing</item>
/// </list>
/// <para>
/// This is a machine-local registry. It is never synced between machines.
/// Each machine has its own signing key, so signed env files are bound
/// to the machine that created them.
/// </para>
/// </summary>
public sealed class Mk8SandboxRegistry
{
    private const string AppDataFolderName = "mk8.shell";
    private const string SandboxesFileName = "sandboxes.json";
    private const string KeyFileName = "mk8.shell.key";
    private const string HistoryFolderName = "history";

    /// <summary>
    /// Well-known filenames that live inside each sandbox root.
    /// These are GIGABLACKLISTED — mk8.shell commands must NEVER
    /// read, write, modify, or delete them.
    /// </summary>
    public const string SandboxEnvFileName = "mk8.shell.env";
    public const string SandboxSignedEnvFileName = "mk8.shell.signed.env";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _appDataRoot;

    /// <summary>
    /// Creates a registry rooted at the standard
    /// <c>%APPDATA%/mk8.shell</c> directory.
    /// </summary>
    public Mk8SandboxRegistry()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolderName))
    {
    }

    /// <summary>
    /// Creates a registry at a custom root (for testing).
    /// </summary>
    public Mk8SandboxRegistry(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        _appDataRoot = Path.GetFullPath(appDataRoot);
    }

    // ── Paths ─────────────────────────────────────────────────────

    public string AppDataRoot => _appDataRoot;
    public string SandboxesFilePath => Path.Combine(_appDataRoot, SandboxesFileName);
    public string KeyFilePath => Path.Combine(_appDataRoot, KeyFileName);
    public string HistoryFolderPath => Path.Combine(_appDataRoot, HistoryFolderName);

    // ── Initialization ────────────────────────────────────────────

    /// <summary>
    /// Ensures the <c>%APPDATA%/mk8.shell</c> directory structure exists
    /// and that a signing key is present. Generates a new key if none
    /// exists.
    /// </summary>
    public void EnsureInitialized()
    {
        Directory.CreateDirectory(_appDataRoot);
        Directory.CreateDirectory(HistoryFolderPath);

        if (!File.Exists(KeyFilePath))
        {
            var key = Mk8SandboxEnvSigner.GenerateKey();
            File.WriteAllBytes(KeyFilePath, key);
        }
    }

    // ── Key ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the local signing key. Throws if not initialized.
    /// </summary>
    public byte[] LoadKey()
    {
        if (!File.Exists(KeyFilePath))
            throw new InvalidOperationException(
                $"mk8.shell signing key not found at '{KeyFilePath}'. " +
                "Run EnsureInitialized() or mk8.shell.startup first.");

        return File.ReadAllBytes(KeyFilePath);
    }

    // ── Sandbox listing ───────────────────────────────────────────

    /// <summary>
    /// Loads all registered sandbox entries from the local registry.
    /// Returns an empty dictionary if the file does not exist.
    /// </summary>
    public Dictionary<string, Mk8SandboxEntry> LoadSandboxes()
    {
        if (!File.Exists(SandboxesFilePath))
            return new Dictionary<string, Mk8SandboxEntry>(
                StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(SandboxesFilePath);
        var entries = JsonSerializer.Deserialize<
            Dictionary<string, Mk8SandboxEntry>>(json, JsonOptions);

        return entries is not null
            ? new Dictionary<string, Mk8SandboxEntry>(
                entries, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Mk8SandboxEntry>(
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists the sandbox registry back to disk.
    /// </summary>
    public void SaveSandboxes(Dictionary<string, Mk8SandboxEntry> sandboxes)
    {
        ArgumentNullException.ThrowIfNull(sandboxes);
        Directory.CreateDirectory(_appDataRoot);
        var json = JsonSerializer.Serialize(sandboxes, JsonOptions);
        File.WriteAllText(SandboxesFilePath, json);
    }

    /// <summary>
    /// Resolves a sandbox ID to its local entry. Case-insensitive.
    /// </summary>
    /// <exception cref="Mk8SandboxNotFoundException">
    /// Thrown when the sandbox ID is not registered locally.
    /// </exception>
    public Mk8SandboxEntry Resolve(string sandboxId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);

        var sandboxes = LoadSandboxes();

        if (!sandboxes.TryGetValue(sandboxId, out var entry))
            throw new Mk8SandboxNotFoundException(sandboxId);

        return entry;
    }

    // ── History ───────────────────────────────────────────────────

    /// <summary>
    /// Archives a timestamped copy of the signed env content into the
    /// history folder. Format: <c>{sandboxId}_{yyyyMMdd_HHmmss}.signed.env</c>
    /// </summary>
    public void ArchiveSignedEnv(string sandboxId, string signedEnvContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedEnvContent);

        Directory.CreateDirectory(HistoryFolderPath);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{sandboxId}_{timestamp}.signed.env";
        var filePath = Path.Combine(HistoryFolderPath, fileName);

        File.WriteAllText(filePath, signedEnvContent);
    }
}

/// <summary>
/// A single entry in the local sandbox registry (<c>sandboxes.json</c>).
/// </summary>
public sealed class Mk8SandboxEntry
{
    /// <summary>Absolute path to the sandbox root directory.</summary>
    [JsonPropertyName("rootPath")]
    public required string RootPath { get; set; }

    /// <summary>UTC timestamp of when this sandbox was registered.</summary>
    [JsonPropertyName("registeredAtUtc")]
    public required DateTimeOffset RegisteredAtUtc { get; set; }
}

/// <summary>
/// Thrown when a sandbox ID is not found in the local registry.
/// </summary>
public sealed class Mk8SandboxNotFoundException(string sandboxId)
    : InvalidOperationException(
        $"mk8.shell sandbox '{sandboxId}' is not registered on this machine. " +
        "Use mk8.shell.startup to register a new sandbox.");
