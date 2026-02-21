namespace Mk8.Shell;

/// <summary>
/// Closed allowlist of binaries for <c>ProcRun</c>. Shells and
/// interpreters are permanently excluded.
/// </summary>
public static class Mk8BinaryAllowlist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // Build tools
        "dotnet", "node", "npm", "npx", "pip3", "cargo", "make", "cmake",

        // Data / archive
        "jq", "tar", "gzip", "gunzip", "unzip", "zip",

        // Version control
        "git",

        // Safe read-only tools
        "echo", "cat", "head", "tail", "wc", "sort", "uniq",
        "grep", "diff", "sha256sum", "md5sum", "base64",
    };

    private static readonly HashSet<string> PermanentlyBlocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Shells
        "bash", "sh", "zsh", "fish", "dash", "csh", "tcsh", "ksh",
        "cmd", "cmd.exe",
        "powershell", "powershell.exe", "pwsh", "pwsh.exe",

        // Interpreters — can execute arbitrary code via -c / -e
        "python", "python2", "python3", "pip",
        "perl", "ruby", "lua", "php", "tclsh", "wish",

        // Wrappers that re-invoke shell
        "env", "xargs", "nohup", "setsid", "script", "expect",
        "screen", "tmux", "strace", "ltrace",

        // Privilege escalation
        "sudo", "su", "doas", "pkexec", "runuser", "chroot", "nsenter",
        "runas", "gsudo",

        // Dangerous system tools
        "chmod", "chown", "chgrp", "mount", "umount",
        "iptables", "ip6tables", "nft",
        "systemctl", "service", "init", "journalctl",
        "dd", "mkfs", "fdisk", "parted",
        "visudo", "passwd", "useradd", "usermod", "groupadd",
        "setenforce", "getenforce",
        "crontab", "at", "atq", "atrm",
        "nc", "ncat", "socat", "netcat",
        "ssh", "scp", "sftp", "rsync",

        // Network download tools that can output to pipe/exec
        "curl", "wget",

        // Find can exec
        "find",
    };

    /// <summary>
    /// Per-binary flags that are blocked even for allowed binaries.
    /// Prevents arg injection like <c>dotnet run -- malicious</c> or
    /// <c>node -e "require('child_process')..."</c>.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> PerBinaryBlockedFlags =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = new(StringComparer.OrdinalIgnoreCase) { "run", "script", "exec" },
        ["node"] = new(StringComparer.OrdinalIgnoreCase) { "-e", "--eval", "-p", "--print", "--input-type" },
        ["npm"] = new(StringComparer.OrdinalIgnoreCase) { "exec", "x", "start", "test", "run" },
        ["npx"] = new(StringComparer.OrdinalIgnoreCase) { "-c" },
        ["cargo"] = new(StringComparer.OrdinalIgnoreCase) { "run", "script" },
        ["make"] = new(StringComparer.OrdinalIgnoreCase) { "SHELL=", "--eval" },
        ["git"] = new(StringComparer.OrdinalIgnoreCase) { "-c", "--exec", "--upload-pack", "--receive-pack" },
        ["tar"] = new(StringComparer.OrdinalIgnoreCase) { "--checkpoint-action", "--to-command", "--use-compress-program" },
    };

    /// <summary>
    /// Binaries that interpret file arguments as executable code.
    /// Maps binary → set of file extensions it can execute.
    /// If any ProcRun argument ends with a listed extension, it's blocked.
    /// <para>
    /// This closes the write-then-execute chain:
    /// <c>FileWrite ["evil.js", "..."] → ProcRun ["node", "evil.js"]</c>
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> CodeFileExtensions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = new(StringComparer.OrdinalIgnoreCase) { ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts" },
        ["npx"] = new(StringComparer.OrdinalIgnoreCase) { ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts" },
    };

    /// <summary>
    /// Filenames (exact match) that contain executable config — if
    /// these exist in the working directory, certain allowed binaries
    /// will execute whatever is inside them. These cannot be written
    /// via FileWrite and cannot be passed as arguments.
    /// </summary>
    private static readonly HashSet<string> DangerousConfigFiles =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "Makefile", "makefile", "GNUmakefile",
    };

    public static bool IsAllowed(string binary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binary);
        var name = Path.GetFileName(binary);

        if (PermanentlyBlocked.Contains(name))
            return false;

        return Allowed.Contains(name);
    }

    /// <summary>
    /// Validates per-binary arg restrictions. Must be called AFTER
    /// <see cref="IsAllowed"/> returns true. Checks three layers:
    /// <list type="number">
    ///   <item>Blocked flags (e.g. <c>dotnet run</c>)</item>
    ///   <item>Code-file arguments (e.g. <c>node evil.js</c>)</item>
    ///   <item>Dangerous config filenames (e.g. <c>Makefile</c>)</item>
    /// </list>
    /// </summary>
    public static void ValidateArgs(string binary, string[] args)
    {
        var name = Path.GetFileName(binary);

        // Layer 1: blocked flags.
        if (PerBinaryBlockedFlags.TryGetValue(name, out var blocked))
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (blocked.Contains(arg))
                    throw new Mk8CompileException(Mk8ShellVerb.ProcRun,
                        $"Blocked flag '{arg}' for binary '{name}'.");

                foreach (var flag in blocked)
                {
                    if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                        throw new Mk8CompileException(Mk8ShellVerb.ProcRun,
                            $"Blocked flag '{flag}' for binary '{name}'.");
                }
            }
        }

        // Layer 2: code-file arguments — blocks ProcRun ["node", "evil.js"].
        if (CodeFileExtensions.TryGetValue(name, out var codeExts))
        {
            for (var i = 0; i < args.Length; i++)
            {
                var ext = Path.GetExtension(args[i]);
                if (!string.IsNullOrEmpty(ext) && codeExts.Contains(ext))
                    throw new Mk8CompileException(Mk8ShellVerb.ProcRun,
                        $"Cannot pass code file '*{ext}' as argument to '{name}'. " +
                        "This binary would execute the file contents.");
            }
        }

        // Layer 3: dangerous config filenames in any arg position.
        for (var i = 0; i < args.Length; i++)
        {
            var leaf = Path.GetFileName(args[i]);
            if (DangerousConfigFiles.Contains(leaf))
                throw new Mk8CompileException(Mk8ShellVerb.ProcRun,
                    $"Cannot reference config file '{leaf}' — it contains " +
                    "executable directives for build tools.");
        }
    }

    public static bool IsPermanentlyBlocked(string binary) =>
        PermanentlyBlocked.Contains(Path.GetFileName(binary));

    public static IReadOnlySet<string> GetAllowed() => Allowed;
    public static IReadOnlySet<string> GetPermanentlyBlocked() => PermanentlyBlocked;
}
