namespace Mk8.Shell;

/// <summary>
/// Resolves <c>$VARIABLE</c> shortcuts at compile time.
/// Variables are NOT shell env vars — they're resolved before
/// any process spawns.
/// </summary>
public static class Mk8VariableResolver
{
    /// <summary>
    /// Variables that are NEVER resolved in ProcRun arguments.
    /// An agent could use FileRead → $PREV → ProcRun to inject
    /// file contents as process arguments. Blocking $PREV in
    /// ProcRun makes this structurally impossible.
    /// <para>
    /// Named captures from process steps are added dynamically via
    /// <see cref="BlockCapturedVariable"/>.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ProcRunBlockedVars =
        new(StringComparer.OrdinalIgnoreCase) { "PREV" };

    public static Dictionary<string, string> BuildVariables(Mk8WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WORKSPACE"] = workspace.SandboxRoot,
            ["CWD"] = workspace.WorkingDirectory,
            ["USER"] = workspace.RunAsUser,
            ["PREV"] = string.Empty,
        };

        foreach (var (key, value) in workspace.Variables)
            vars.TryAdd(key, value);

        return vars;
    }

    /// <summary>
    /// Validates that capture names in the script are legal and within limits.
    /// Returns the set of capture names that originate from process-spawning
    /// verbs (ProcRun, Git*) — these must be blocked in ProcRun args.
    /// </summary>
    public static HashSet<string> ValidateCaptures(
        IReadOnlyList<Mk8ShellOperation> operations)
    {
        var captureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processCaptures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.CaptureAs))
                continue;

            var name = op.CaptureAs;

            // Cannot override built-ins.
            if (Mk8CaptureRules.ReservedNames.Contains(name))
                throw new Mk8CompileException(op.Verb,
                    $"Capture name '{name}' is a reserved variable name.");

            // Uniqueness.
            if (!captureNames.Add(name))
                throw new Mk8CompileException(op.Verb,
                    $"Duplicate capture name '{name}'.");

            // Cap total captures.
            if (captureNames.Count > Mk8CaptureRules.MaxCaptures)
                throw new Mk8CompileException(op.Verb,
                    $"Script exceeds the maximum of {Mk8CaptureRules.MaxCaptures} " +
                    "captured variables.");

            // Validate name characters (same as label rules).
            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    throw new Mk8CompileException(op.Verb,
                        $"Capture name '{name}' contains invalid character '{c}'. " +
                        "Names may only contain letters, digits, and underscores.");
            }

            // Track if this capture comes from a process-spawning verb.
            if (IsProcessVerb(op.Verb))
                processCaptures.Add(name);
        }

        return processCaptures;
    }

    /// <summary>
    /// Builds a ProcRun-blocked variable set that includes both the
    /// built-in blocked vars ($PREV) and any captured variables that
    /// originated from process-spawning steps.
    /// </summary>
    public static HashSet<string> BuildProcRunBlockedVars(
        HashSet<string> processCaptures)
    {
        var blocked = new HashSet<string>(ProcRunBlockedVars, StringComparer.OrdinalIgnoreCase);
        foreach (var name in processCaptures)
            blocked.Add(name);
        return blocked;
    }

    /// <summary>
    /// Sets a captured variable in the variable dictionary.
    /// Called by the executor after a step completes.
    /// </summary>
    public static void SetCapturedVariable(
        Dictionary<string, string> variables,
        string name,
        string? output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Mk8CaptureRules.ReservedNames.Contains(name))
            throw new InvalidOperationException(
                $"Cannot set reserved variable '{name}' as a capture.");

        variables[name] = output?.Trim() ?? string.Empty;
    }

    public static string ResolveArg(
        string arg,
        IReadOnlyDictionary<string, string> variables,
        HashSet<string>? blockedVars = null)
    {
        if (string.IsNullOrEmpty(arg) || !arg.Contains('$'))
            return arg;

        var result = new System.Text.StringBuilder(arg.Length);
        var i = 0;

        while (i < arg.Length)
        {
            if (arg[i] == '$' && i + 1 < arg.Length && IsVarStart(arg[i + 1]))
            {
                var nameStart = i + 1;
                var nameEnd = nameStart;
                while (nameEnd < arg.Length && IsVarChar(arg[nameEnd]))
                    nameEnd++;

                var name = arg[nameStart..nameEnd];

                // Block listed variables — throw, don't silently skip.
                if (blockedVars is not null && blockedVars.Contains(name))
                    throw new Mk8CompileException(Mk8ShellVerb.ProcRun,
                        $"Variable '${name}' cannot be used in this context.");

                if (variables.TryGetValue(name, out var value))
                    result.Append(value);
                else
                    result.Append(arg, i, nameEnd - i);

                i = nameEnd;
            }
            else
            {
                result.Append(arg[i]);
                i++;
            }
        }

        return result.ToString();
    }

    public static string[] ResolveArgs(
        string[] args, IReadOnlyDictionary<string, string> variables)
    {
        var resolved = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
            resolved[i] = ResolveArg(args[i], variables);
        return resolved;
    }

    /// <summary>
    /// Resolves args for ProcRun — blocks <c>$PREV</c> and other
    /// dangerous variables from appearing in process arguments.
    /// </summary>
    public static string[] ResolveArgsForProc(
        string[] args, IReadOnlyDictionary<string, string> variables)
    {
        return ResolveArgsForProc(args, variables, ProcRunBlockedVars);
    }

    /// <summary>
    /// Resolves args for ProcRun with a custom blocked variable set
    /// that includes both built-in blocks and process-captured variables.
    /// </summary>
    public static string[] ResolveArgsForProc(
        string[] args,
        IReadOnlyDictionary<string, string> variables,
        HashSet<string> blockedVars)
    {
        var resolved = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
            resolved[i] = ResolveArg(args[i], variables, blockedVars);
        return resolved;
    }

    public static void SetPreviousOutput(
        Dictionary<string, string> variables, string? output)
    {
        variables["PREV"] = output?.Trim() ?? string.Empty;
    }

    private static bool IsProcessVerb(Mk8ShellVerb verb) =>
        verb is Mk8ShellVerb.ProcRun
            or Mk8ShellVerb.GitStatus or Mk8ShellVerb.GitLog
            or Mk8ShellVerb.GitDiff or Mk8ShellVerb.GitAdd
            or Mk8ShellVerb.GitCommit or Mk8ShellVerb.GitPush
            or Mk8ShellVerb.GitPull or Mk8ShellVerb.GitClone
            or Mk8ShellVerb.GitCheckout or Mk8ShellVerb.GitBranch;

    private static bool IsVarStart(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsVarChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
