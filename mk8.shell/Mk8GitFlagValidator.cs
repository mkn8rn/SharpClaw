namespace Mk8.Shell;

/// <summary>
/// Validates git arguments to prevent command injection through
/// git's own flag interpreter.
/// </summary>
public static class Mk8GitFlagValidator
{
    private static readonly HashSet<string> BlockedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--exec", "--upload-pack", "--receive-pack",
        "-u",  // alias for --upload-pack
        "--exec-path",
        "-c", "--config",
        "--run", "--filter",
        "core.editor", "core.pager", "core.sshCommand",
        "sequence.editor", "diff.external", "merge.tool",
        "credential.helper",
        "--git-dir", "--work-tree",
        "--post-checkout", "--post-merge",
    };

    private static readonly string[] BlockedPrefixes =
    [
        "-c=", "-c ", "--config=", "--exec=",
        "--upload-pack=", "--receive-pack=",
        "--exec-path=", "--filter=",
        "--git-dir=", "--work-tree=",
    ];

    public static void Validate(string subCommand, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (BlockedFlags.Contains(arg))
                throw new Mk8CompileException(
                    Mk8ShellVerb.GitStatus,
                    $"Blocked git flag '{arg}' in 'git {subCommand}'.");

            foreach (var prefix in BlockedPrefixes)
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new Mk8CompileException(
                        Mk8ShellVerb.GitStatus,
                        $"Blocked git flag prefix '{prefix.TrimEnd('=', ' ')}' in 'git {subCommand}'.");
            }
        }
    }
}
