namespace Mk8.Shell.Safety;

/// <summary>
/// Read-only env var access. Blocks secret-containing names.
/// </summary>
public static class Mk8EnvAllowlist
{
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "HOME", "USERPROFILE", "USER", "USERNAME",
        "PATH", "LANG", "LC_ALL", "TZ", "TERM",
        "PWD", "HOSTNAME", "SHELL", "EDITOR",
        "DOTNET_ROOT", "NODE_ENV",
    };

    private static readonly string[] BlockedSubstrings =
    [
        "KEY", "SECRET", "TOKEN", "PASSWORD", "PASSWD",
        "CREDENTIAL", "CONN", "CONNECTION_STRING",
        "PRIVATE", "ENCRYPT", "JWT", "BEARER", "AUTH",
        "CERTIFICATE", "APIKEY", "API_KEY",
    ];

    public static bool IsAllowed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var blocked in BlockedSubstrings)
        {
            if (name.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return Allowed.Contains(name);
    }

    public static IReadOnlySet<string> GetAllowed() => Allowed;
}
