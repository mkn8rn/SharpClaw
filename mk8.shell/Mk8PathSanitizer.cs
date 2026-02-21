namespace Mk8.Shell;

/// <summary>
/// Resolves and validates filesystem paths against a sandbox root.
/// <para>
/// Core rule: <b>resolve first, check second.</b>
/// </para>
/// </summary>
public static class Mk8PathSanitizer
{
    /// <summary>
    /// File extensions that remain blocked on write targets because
    /// an allowed binary on the ProcRun allowlist could directly execute
    /// them, or because they are native executables the OS runs directly.
    /// <para>
    /// <b>NOT blocked here</b>: script extensions whose interpreters are
    /// ALL permanently blocked (e.g. .sh, .bat, .ps1, .py). Agents can
    /// author these for humans or unrestricted external tooling — the
    /// agent itself has no way to run them through mk8.shell.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> BlockedWriteExtensions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Native executables (OS runs directly) ─────────────────
        ".exe", ".com", ".scr", ".msi", ".msp", ".dll",
        ".bin", ".run", ".appimage", ".elf", ".so", ".dylib",

        // ── Extensions executable by ALLOWED ProcRun binaries ─────
        // node (on allowlist) can run these directly
        ".js", ".mjs", ".cjs",
        // Windows script host (could be invoked by OS association)
        ".jse", ".wsf", ".wsh", ".msh", ".vbs", ".vbe",
    };

    public static string Resolve(string userPath, string sandboxRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxRoot);

        ValidateCharacters(userPath);

        var canonicalRoot = NormalizeSeparators(Path.GetFullPath(sandboxRoot));
        if (!canonicalRoot.EndsWith(Path.DirectorySeparatorChar))
            canonicalRoot += Path.DirectorySeparatorChar;

        var combined = Path.IsPathRooted(userPath)
            ? Path.GetFullPath(userPath)
            : Path.GetFullPath(Path.Combine(canonicalRoot, userPath));
        var canonical = NormalizeSeparators(combined);

        if (!canonical.StartsWith(canonicalRoot, PathComparison)
            && !string.Equals(canonical + Path.DirectorySeparatorChar, canonicalRoot, PathComparison))
        {
            throw new Mk8PathViolationException(userPath, sandboxRoot);
        }

        if (OperatingSystem.IsWindows())
            ValidateNoDeviceName(canonical);

        return canonical;
    }

    /// <summary>
    /// Validates a path that will be the TARGET of a write, append,
    /// move, or copy operation. Blocks:
    /// <list type="bullet">
    ///   <item>Native executables and code files runnable by allowed binaries</item>
    ///   <item>Config filenames that trigger implicit code execution (e.g. Makefile)</item>
    /// </list>
    /// <para>
    /// Script extensions whose interpreters are ALL permanently blocked
    /// (.sh, .bat, .ps1, .py, etc.) are intentionally ALLOWED — agents
    /// can author these for humans or external tooling.
    /// </para>
    /// </summary>
    public static string ResolveForWrite(string userPath, string sandboxRoot)
    {
        var resolved = Resolve(userPath, sandboxRoot);
        ValidateNotExecutableExtension(resolved, userPath);
        ValidateNotDangerousFilename(resolved, userPath);
        return resolved;
    }

    /// <summary>
    /// Returns <c>true</c> if the path resolves inside the sandbox.
    /// </summary>
    public static bool IsInsideSandbox(string userPath, string sandboxRoot)
    {
        try
        {
            Resolve(userPath, sandboxRoot);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════

    private static void ValidateCharacters(string path)
    {
        foreach (var c in path)
        {
            if (c == '\0')
                throw new ArgumentException(
                    "Path contains a null byte.", nameof(path));
            if (char.IsControl(c) && c != '\t')
                throw new ArgumentException(
                    $"Path contains control character 0x{(int)c:X2}.", nameof(path));
        }
    }

    private static void ValidateNotExecutableExtension(
        string resolvedPath, string originalPath)
    {
        var ext = Path.GetExtension(resolvedPath);
        if (!string.IsNullOrEmpty(ext) && BlockedWriteExtensions.Contains(ext))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Cannot write to executable file type '{ext}': '{originalPath}'.");
    }

    /// <summary>
    /// Filenames that trigger code execution by proximity — just
    /// existing in a directory causes allowed build tools to run
    /// whatever is inside them.
    /// </summary>
    private static readonly HashSet<string> BlockedWriteFilenames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "Makefile", "makefile", "GNUmakefile",
        "CMakeLists.txt",
        "Dockerfile",
        ".npmrc",
    };

    private static void ValidateNotDangerousFilename(
        string resolvedPath, string originalPath)
    {
        var fileName = Path.GetFileName(resolvedPath);
        if (BlockedWriteFilenames.Contains(fileName))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Cannot write to config file '{fileName}': " +
                $"it contains executable directives for build tools. " +
                $"Path: '{originalPath}'.");
    }

    private static readonly HashSet<string> WindowsDeviceNames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4",
        "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4",
        "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static void ValidateNoDeviceName(string canonical)
    {
        var fileName = Path.GetFileNameWithoutExtension(canonical);
        if (fileName is not null && WindowsDeviceNames.Contains(fileName))
            throw new ArgumentException(
                $"Path contains reserved Windows device name '{fileName}'.");
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
