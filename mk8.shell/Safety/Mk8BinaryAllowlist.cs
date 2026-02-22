namespace Mk8.Shell.Safety;

/// <summary>
/// Permanently blocked binaries for ProcRun.  These can NEVER be
/// executed regardless of what templates are registered in
/// <see cref="Mk8CommandWhitelist"/>.
/// <para>
/// Command validation has moved to <see cref="Mk8CommandWhitelist"/>
/// which uses a strict template model — only explicitly registered
/// command patterns with typed parameter slots are allowed.  This
/// class retains only the hard-block list as a defence-in-depth
/// layer that cannot be overridden.
/// </para>
/// </summary>
public static class Mk8BinaryAllowlist
{
    private static readonly HashSet<string> PermanentlyBlocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Shells
        "bash", "sh", "zsh", "fish", "dash", "csh", "tcsh", "ksh",
        "cmd", "cmd.exe",
        "powershell", "powershell.exe", "pwsh", "pwsh.exe",

        // Interpreters — can execute arbitrary code via -c / -e
        "python", "python2", "python3", "pip",
        "perl", "ruby", "lua", "php", "tclsh", "wish",

        // Package managers that execute arbitrary code during install
        "pip3",  // internally invokes Python for setup.py
        "npx",   // downloads + executes arbitrary npm packages

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

        // Build tools with implicit Makefile/CMakeLists execution
        "make", "cmake",

        // Free-text argument tools — use in-memory verbs instead:
        //   echo → FileWrite,  grep → TextRegex,  jq → JsonQuery
        "echo", "grep", "jq",

        // Find can exec
        "find",
    };

    public static bool IsPermanentlyBlocked(string binary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binary);
        return PermanentlyBlocked.Contains(Path.GetFileName(binary));
    }

    public static IReadOnlySet<string> GetPermanentlyBlocked() => PermanentlyBlocked;
}
