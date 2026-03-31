namespace Mk8.Shell.Safety;

/// <summary>
/// Centralized gigablacklist enforcement. If ANY pattern in this list
/// appears ANYWHERE in ANY argument of ANY command, the operation fails
/// before compilation with <see cref="Mk8GigaBlacklistException"/>.
/// <para>
/// This is a separate, unconditional safety layer that runs on the
/// full materialized command — after variable expansion, after slot
/// validation, on every string the agent produces.
/// </para>
/// <para>
/// The compile-time patterns are intentionally minimal — only patterns
/// that protect mk8.shell's own infrastructure (sandbox env/key files)
/// and raw block-device paths that could cause catastrophic data loss
/// if referenced anywhere:
/// <list type="bullet">
///   <item><b>mk8.shell env patterns</b> — the 4 sandbox env/key filenames.
///     Disabled only via <c>DisableMk8shellEnvsGigablacklist</c>
///     in base.env.</item>
///   <item><b>Hardcoded patterns</b> — raw block-device paths only.
///     Disabled via <c>DisableHardcodedGigablacklist</c> in base.env.</item>
/// </list>
/// </para>
/// <para>
/// Everything else is the developer's responsibility. Use
/// <c>CustomBlacklist</c> in base.env (global) or <c>MK8_BLACKLIST</c>
/// in sandbox signed env (per-sandbox) to add project-specific patterns.
/// Both are additive — patterns from all sources are merged.
/// </para>
/// </summary>
public sealed class Mk8GigaBlacklist
{
    /// <summary>Minimum length for a custom blacklist pattern to be accepted.</summary>
    public const int MinCustomPatternLength = 2;

    /// <summary>
    /// mk8.shell infrastructure patterns — sandbox env filenames and the
    /// signing key. These protect the sandbox's own configuration files
    /// from agent access. Disabled independently from the rest of the
    /// hardcoded list via <c>DisableMk8shellEnvsGigablacklist</c>.
    /// </summary>
    private static readonly string[] Mk8ShellEnvPatterns =
    [
        "mk8.shell.env",
        "mk8.shell.signed.env",
        "mk8.shell.base.env",
        "mk8.shell.key",
    ];

    /// <summary>
    /// Compile-time hardcoded patterns — limited to raw block-device
    /// paths that could cause catastrophic data loss. Everything else
    /// (shell injection markers, destructive commands, SQL destruction,
    /// privilege escalation, etc.) is not enforceable here — mk8.shell
    /// never executes a shell, and blocking these strings in file content
    /// or commit messages is counterproductive. Developers can add
    /// project-specific patterns via <c>CustomBlacklist</c> / <c>MK8_BLACKLIST</c>.
    /// Disabled via <c>DisableHardcodedGigablacklist</c>.
    /// </summary>
    private static readonly string[] HardcodedPatterns =
    [
        // ── Raw block-device access ───────────────────────────────
        // These paths reference physical storage devices. While mk8.shell
        // itself can't open them (outside sandbox), blocking references
        // prevents agents from crafting content that other tools might
        // consume to target raw devices.
        "/dev/sda",
        "/dev/sdb",
        "/dev/sdc",
        "/dev/nvme",
        "/dev/vda",
        "/dev/hda",
        "/dev/xvda",
        "\\\\.\\PhysicalDrive",
        "\\\\.\\GLOBALROOT",
    ];

    /// <summary>
    /// All effective patterns for this instance.
    /// </summary>
    private readonly string[] _allPatterns;

    /// <summary>
    /// Returns all effective patterns (compile-time + env-sourced).
    /// Read-only view for diagnostic introspection.
    /// </summary>
    public IReadOnlyList<string> EffectivePatterns => _allPatterns;

    /// <summary>
    /// Creates a gigablacklist with all compile-time patterns (no env extras).
    /// </summary>
    public Mk8GigaBlacklist() =>
        _allPatterns = [.. Mk8ShellEnvPatterns, .. HardcodedPatterns];

    /// <summary>
    /// Creates a gigablacklist with configurable compile-time groups plus
    /// custom patterns from env files.
    /// </summary>
    /// <param name="customPatterns">
    /// Additional patterns from <c>MK8_BLACKLIST</c> in base.env and/or
    /// sandbox env. Merged additively — both sources contribute.
    /// </param>
    /// <param name="disableHardcoded">
    /// When <c>true</c>, the hardcoded block-device patterns are excluded.
    /// The mk8.shell env patterns remain active unless
    /// <paramref name="disableMk8shellEnvs"/> is also <c>true</c>.
    /// </param>
    /// <param name="disableMk8shellEnvs">
    /// When <c>true</c>, the mk8.shell env/key filename patterns are
    /// also excluded. Only takes effect when
    /// <paramref name="disableHardcoded"/> is also <c>true</c>.
    /// <para>
    /// <b>WARNING:</b> Disabling both flags removes ALL compile-time
    /// protection — only custom patterns remain. This is strongly
    /// discouraged.
    /// </para>
    /// </param>
    public Mk8GigaBlacklist(
        IEnumerable<string>? customPatterns,
        bool disableHardcoded = false,
        bool disableMk8shellEnvs = false)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Compile-time patterns (conditional) ───────────────────
        // mk8.shell env patterns are included unless BOTH disable
        // flags are set. When only disableHardcoded is true, the env
        // patterns still protect sandbox infrastructure.
        if (!disableHardcoded)
        {
            foreach (var p in Mk8ShellEnvPatterns)
                patterns.Add(p);
            foreach (var p in HardcodedPatterns)
                patterns.Add(p);
        }
        else if (!disableMk8shellEnvs)
        {
            // Hardcoded disabled, but env patterns still active
            foreach (var p in Mk8ShellEnvPatterns)
                patterns.Add(p);
        }

        // ── Custom patterns (always additive) ─────────────────────
        if (customPatterns is not null)
        {
            foreach (var p in customPatterns)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;

                var trimmed = p.Trim();
                if (trimmed.Length < MinCustomPatternLength)
                    continue;

                patterns.Add(trimmed);
            }
        }

        _allPatterns = [.. patterns];
    }

    /// <summary>
    /// Checks a single value against the gigablacklist. Returns the
    /// matching pattern if found, or <c>null</c> if clean.
    /// </summary>
    public string? Check(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        foreach (var pattern in _allPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pattern;
        }

        return null;
    }

    /// <summary>
    /// Checks all arguments in a command invocation. Throws
    /// <see cref="Mk8GigaBlacklistException"/> on first match.
    /// </summary>
    public void EnforceAll(string binary, string[] args)
    {
        var match = Check(binary);
        if (match is not null)
            throw new Mk8GigaBlacklistException(match,
                $"Binary name '{binary}' contains gigablacklisted term '{match}'.\n" +
                "The gigablacklist runs on ALL arguments (including the binary name) " +
                "before any other validation. This is non-bypassable.\n" +
                $"  ✗ Binary '{binary}' matched pattern: \"{match}\"\n" +
                "Run { \"verb\": \"Mk8Templates\", \"args\": [] } to see allowed commands, " +
                "or { \"verb\": \"Mk8Blacklist\", \"args\": [] } to see all blocked patterns.");

        foreach (var arg in args)
        {
            match = Check(arg);
            if (match is not null)
                throw new Mk8GigaBlacklistException(match,
                    $"ProcRun argument '{Truncate(arg)}' contains gigablacklisted term '{match}'.\n" +
                    "The gigablacklist blocks dangerous patterns in ALL arguments of ALL commands.\n" +
                    $"  ✗ Argument matched pattern: \"{match}\"\n" +
                    "Rephrase the argument to avoid this pattern. " +
                    "Run { \"verb\": \"Mk8Blacklist\", \"args\": [] } to see all blocked patterns.");
        }
    }

    /// <summary>
    /// Checks all arguments in an in-memory verb invocation. Throws
    /// <see cref="Mk8GigaBlacklistException"/> on first match.
    /// </summary>
    public void EnforceAllInMemory(string verb, string[] args)
    {
        foreach (var arg in args)
        {
            var match = Check(arg);
            if (match is not null)
                throw new Mk8GigaBlacklistException(match,
                    $"Argument to in-memory verb '{verb}' contains gigablacklisted term '{match}'.\n" +
                    "The gigablacklist runs on ALL arguments of ALL commands (including " +
                    "in-memory verbs like FileRead, FileWrite, DirList, etc.), not just ProcRun.\n" +
                    $"  ✗ Argument matched pattern: \"{match}\"\n" +
                    "Rephrase the argument to avoid this pattern. " +
                    "Run { \"verb\": \"Mk8Blacklist\", \"args\": [] } to see all blocked patterns.");
        }
    }

    /// <summary>
    /// Validates candidate custom patterns. Returns only those that are
    /// non-empty and meet the minimum length requirement.
    /// </summary>
    public static string[] ValidateCustomPatterns(string[]? raw)
    {
        if (raw is null || raw.Length == 0)
            return [];

        return raw
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Trim().Length >= MinCustomPatternLength)
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Truncate(string s) =>
        s.Length > 40 ? s[..40] + "..." : s;
}
