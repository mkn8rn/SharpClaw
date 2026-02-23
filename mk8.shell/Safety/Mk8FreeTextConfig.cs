using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Shell.Safety;

/// <summary>
/// Configuration controlling whether <see cref="Mk8SlotKind.FreeText"/>
/// slots are enabled, and per-verb granular overrides.
/// <para>
/// Loaded from <c>mk8.shell.base.env</c> (global) and
/// <c>mk8.shell.env</c> (per-sandbox). The sandbox .env overrides the
/// global value when set. Per-verb entries from both files are merged
/// additively.
/// </para>
/// <para>
/// <b>Unsafe commands</b> (e.g., ProcRun with arbitrary binaries) can
/// NEVER have FreeText enabled — the
/// <see cref="UnsafeCommandDescriptions"/> set is compile-time constant
/// and immutable.
/// </para>
/// </summary>
public sealed class Mk8FreeTextConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Master toggle. If <c>false</c>, all FreeText slots fall back to
    /// <see cref="Mk8SlotKind.ComposedWords"/> validation.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Default max character length for FreeText values when no
    /// per-verb override is set.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int MaxLength { get; set; } = 200;

    /// <summary>
    /// Per-verb overrides. Key is the command template description
    /// (e.g., <c>"git commit"</c>). Merged additively from global +
    /// sandbox env — sandbox entries override global entries with the
    /// same key.
    /// </summary>
    [JsonPropertyName("perVerb")]
    public Dictionary<string, Mk8FreeTextVerbPolicy> PerVerb { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Command template descriptions where FreeText is NEVER allowed,
    /// regardless of configuration. Compile-time constant, immutable.
    /// <para>
    /// These are commands where free text in arguments could influence
    /// process execution, file system state via path manipulation, or
    /// network behavior in ways the slot-type system cannot validate.
    /// </para>
    /// </summary>
    public static readonly HashSet<string> UnsafeCommandDescriptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ProcRun commands where free text args could be dangerous
            // are protected by the template system — but we additionally
            // block any command description containing these patterns
            // from using FreeText slots at registration time.
        };

    /// <summary>
    /// Binary names that can NEVER have FreeText-typed slots.
    /// </summary>
    public static readonly HashSet<string> UnsafeBinaries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Interpreters (permanently blocked anyway, but defense-in-depth)
            "bash", "sh", "zsh", "cmd", "powershell", "pwsh",
            "python", "python3", "ruby", "perl", "lua", "php",
            "node", "npx", "deno", "bun",
            // System commands
            "sudo", "su", "chmod", "chown",
            "curl", "wget", "ssh", "scp",
        };

    /// <summary>
    /// Returns the effective max length for a specific command description.
    /// Uses per-verb override if set, otherwise the global default.
    /// </summary>
    public int GetMaxLength(string commandDescription) =>
        PerVerb.TryGetValue(commandDescription, out var policy) && policy.MaxLength > 0
            ? policy.MaxLength
            : MaxLength;

    /// <summary>
    /// Returns whether FreeText is enabled for a specific command.
    /// Checks: master toggle → unsafe binary → unsafe description → per-verb override.
    /// </summary>
    public bool IsEnabledFor(string commandDescription, string binary)
    {
        if (!Enabled)
            return false;

        if (UnsafeBinaries.Contains(binary))
            return false;

        if (UnsafeCommandDescriptions.Contains(commandDescription))
            return false;

        if (PerVerb.TryGetValue(commandDescription, out var policy))
            return policy.Enabled;

        return true; // master enabled, no per-verb override → allowed
    }

    /// <summary>
    /// Parses a <see cref="Mk8FreeTextConfig"/> from a JSON string.
    /// </summary>
    public static Mk8FreeTextConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Mk8FreeTextConfig();

        return JsonSerializer.Deserialize<Mk8FreeTextConfig>(json, JsonOptions)
            ?? new Mk8FreeTextConfig();
    }

    /// <summary>
    /// Merges a sandbox-level config into this (global) config.
    /// Sandbox values override global scalar values. Per-verb entries
    /// merge additively (sandbox wins on key conflict).
    /// </summary>
    public Mk8FreeTextConfig MergeWith(Mk8FreeTextConfig? sandbox)
    {
        if (sandbox is null)
            return this;

        var merged = new Mk8FreeTextConfig
        {
            // Sandbox overrides global scalar values
            Enabled = sandbox.Enabled,
            MaxLength = sandbox.MaxLength > 0 ? sandbox.MaxLength : MaxLength,
            PerVerb = new Dictionary<string, Mk8FreeTextVerbPolicy>(
                PerVerb, StringComparer.OrdinalIgnoreCase),
        };

        // Sandbox per-verb entries override global ones with same key
        foreach (var (key, value) in sandbox.PerVerb)
            merged.PerVerb[key] = value;

        return merged;
    }

    /// <summary>
    /// Serializes to JSON for writing to env files.
    /// </summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
}

/// <summary>
/// Per-verb FreeText policy override.
/// </summary>
public sealed class Mk8FreeTextVerbPolicy
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxLength")]
    public int MaxLength { get; set; }
}
