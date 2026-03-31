using Mk8.Shell.Engine;
using Mk8.Shell.Models;

namespace Mk8.Shell.Safety;

/// <summary>
/// Resolves and validates filesystem paths against a sandbox root.
/// <para>
/// Core rule: <b>resolve first, check second.</b>
/// </para>
/// </summary>
public static class Mk8PathSanitizer
{
    /// <summary>
    /// GIGABLACKLISTED filenames — mk8.shell commands must NEVER read,
    /// write, modify, or delete these files under any circumstances.
    /// They are only managed by the user directly or by mk8.shell.startup.
    /// Checked on ALL path resolutions (read and write).
    /// </summary>
    private static readonly HashSet<string> GigaBlacklistedFilenames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        Mk8SandboxRegistry.SandboxEnvFileName,
        Mk8SandboxRegistry.SandboxSignedEnvFileName,
    };

    /// <summary>
    /// File extensions blocked on write targets. Only two categories:
    /// <list type="bullet">
    ///   <item><b>Native executables</b> — OS-loadable binaries and shared
    ///     libraries that other processes in the sandbox could implicitly
    ///     execute or link against (DLL hijacking, binary planting).</item>
    ///   <item><b>MSBuild project files</b> — <c>dotnet build</c> is on the
    ///     ProcRun allowlist and executes &lt;Exec&gt; targets, source generators,
    ///     and pre/post build events from these files.</item>
    /// </list>
    /// <para>
    /// Everything else is the developer's responsibility. Use
    /// <c>CustomBlacklist</c> in base.env or <c>MK8_BLACKLIST</c> in sandbox
    /// env to add project-specific restrictions.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> BlockedWriteExtensions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Native executables (OS runs directly / binary planting) ─
        ".exe", ".com", ".scr", ".msi", ".msp", ".dll",
        ".bin", ".run", ".appimage", ".elf", ".so", ".dylib",

        // ── MSBuild project files (dotnet build executes <Exec> targets) ──
        ".csproj", ".fsproj", ".vbproj", ".proj",
        ".targets", ".props",
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

        ValidateNotGigaBlacklisted(canonical, userPath);

        return canonical;
    }

    /// <summary>
    /// Validates a path that will be the TARGET of a write, append,
    /// move, or copy operation. Blocks:
    /// <list type="bullet">
    ///   <item>Native executables and shared libraries (binary planting)</item>
    ///   <item>MSBuild project files (dotnet build executes &lt;Exec&gt; targets)</item>
    ///   <item>Config filenames with live attack paths (nuget.config)</item>
    ///   <item>Paths inside <c>.git/</c> (hook injection, config tampering)</item>
    /// </list>
    /// <para>
    /// Everything else is allowed. Developers can add project-specific
    /// restrictions via <c>CustomBlacklist</c> / <c>MK8_BLACKLIST</c>.
    /// </para>
    /// </summary>
    public static string ResolveForWrite(string userPath, string sandboxRoot)
    {
        var resolved = Resolve(userPath, sandboxRoot);
        ValidateNotGitInternals(resolved, userPath);
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
                    "Path contains a null byte (0x00). Paths must contain only " +
                    "printable characters.\n" +
                    "  ✓ Correct: \"$WORKSPACE/src/app.cs\"\n" +
                    "  ✗ Wrong:   a path with embedded null bytes", nameof(path));
            if (char.IsControl(c) && c != '\t')
                throw new ArgumentException(
                    $"Path contains control character (U+{(int)c:X4}). Only printable " +
                    "characters and tabs are allowed in paths.\n" +
                    "  ✓ Correct: \"$WORKSPACE/src/app.cs\"\n" +
                    "  ✗ Wrong:   paths with newlines, carriage returns, or escape sequences",
                    nameof(path));
        }
    }

    /// <summary>
    /// Blocks writes to <c>.git/</c> internal paths.  Prevents:
    /// <list type="bullet">
    ///   <item>Hook injection (<c>.git/hooks/pre-commit</c>)</item>
    ///   <item>Config tampering (<c>.git/config</c>)</item>
    ///   <item>Object injection (<c>.git/objects/...</c>)</item>
    ///   <item>HEAD manipulation (<c>.git/HEAD</c>)</item>
    /// </list>
    /// </summary>
    private static void ValidateNotGitInternals(
        string resolvedPath, string originalPath)
    {
        var sep = Path.DirectorySeparatorChar;
        var gitSegment = $"{sep}.git{sep}";

        if (resolvedPath.Contains(gitSegment, PathComparison)
            || resolvedPath.EndsWith($"{sep}.git", PathComparison))
        {
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Cannot write to git internals (.git/): '{originalPath}'.\n" +
                "The .git/ directory contains hooks, config, objects, and refs " +
                "that must only be modified through git commands — never through " +
                "direct file writes (which could inject malicious hooks or tamper " +
                "with repository state).\n" +
                "  ✓ Correct: { \"verb\": \"FileWrite\", \"args\": [\"$WORKSPACE/src/readme.md\", \"...\"] }\n" +
                "  ✗ Wrong:   { \"verb\": \"FileWrite\", \"args\": [\"$WORKSPACE/.git/hooks/pre-commit\", \"...\"] }\n" +
                "Use git commands (via ProcRun) for git operations. " +
                "Run { \"verb\": \"Mk8Templates\", \"args\": [] } to see available git templates.");
        }
    }

    private static void ValidateNotExecutableExtension(
        string resolvedPath, string originalPath)
    {
        var ext = Path.GetExtension(resolvedPath);
        if (!string.IsNullOrEmpty(ext) && BlockedWriteExtensions.Contains(ext))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Cannot write to '{ext}' file: '{originalPath}'.\n" +
                "This extension is blocked because the file is either a native executable " +
                "(OS-loadable binary/shared library — binary planting risk) or an MSBuild " +
                "project file (dotnet build executes <Exec> targets from these).\n" +
                "  ✓ Correct: .txt, .json, .yaml, .xml, .csv, .md, .sh, .py, .js, .rs, .toml\n" +
                $"  ✗ Wrong:   {ext} (native executable or MSBuild project file)\n" +
                "If you need to create a configuration or data file, use a safe extension.");
    }

    /// <summary>
    /// Filenames blocked on write targets. Only files with a genuine
    /// live attack path through the current ProcRun template set:
    /// <list type="bullet">
    ///   <item><c>nuget.config</c> — <c>dotnet restore</c> reads package
    ///     sources → malicious feed → MSBuild &lt;Exec&gt; in .targets from
    ///     NuGet cache → arbitrary code execution.</item>
    ///   <item>Sandbox env files — also GIGABLACKLISTED, listed here for
    ///     defence-in-depth.</item>
    /// </list>
    /// <para>
    /// Everything else is the developer's responsibility. Use
    /// <c>CustomBlacklist</c> in base.env or <c>MK8_BLACKLIST</c> in sandbox
    /// env to add project-specific restrictions (e.g. <c>package.json</c>,
    /// <c>.gitattributes</c>, <c>build.rs</c>).
    /// </para>
    /// </summary>
    private static readonly HashSet<string> BlockedWriteFilenames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // dotnet restore reads this → malicious package source →
        // MSBuild <Exec> escape via .targets in NuGet cache.
        "nuget.config", "NuGet.Config", "NuGet.config",

        // mk8.shell sandbox env files — also in GigaBlacklistedFilenames
        // but listed here too for defence-in-depth on the write path.
        Mk8SandboxRegistry.SandboxEnvFileName,
        Mk8SandboxRegistry.SandboxSignedEnvFileName,
    };

    private static void ValidateNotDangerousFilename(
        string resolvedPath, string originalPath)
    {
        var fileName = Path.GetFileName(resolvedPath);
        if (BlockedWriteFilenames.Contains(fileName))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Cannot write to '{fileName}': '{originalPath}'.\n" +
                "This filename has a live attack path through the current ProcRun " +
                "template set (e.g. nuget.config → dotnet restore → malicious NuGet " +
                "feed → MSBuild <Exec> escape).\n" +
                "  ✓ Correct: { \"verb\": \"FileWrite\", \"args\": [\"$WORKSPACE/config.yaml\", \"...\"] }\n" +
                $"  ✗ Wrong:   {{ \"verb\": \"FileWrite\", \"args\": [\"$WORKSPACE/{fileName}\", \"...\"] }}\n" +
                "Use FileTemplate/FilePatch to modify existing safe files instead.");
    }

    /// <summary>
    /// GIGABLACKLIST enforcement. These files are completely off-limits
    /// to mk8.shell commands — no read, no write, no copy, no move,
    /// no delete, no hash, no list, nothing. Only the user or
    /// mk8.shell.startup may touch them.
    /// </summary>
    private static void ValidateNotGigaBlacklisted(
        string resolvedPath, string originalPath)
    {
        var fileName = Path.GetFileName(resolvedPath);
        if (GigaBlacklistedFilenames.Contains(fileName))
            throw new Mk8CompileException(Mk8ShellVerb.FileRead,
                $"Access to '{fileName}' is permanently forbidden.\n" +
                "mk8.shell sandbox environment files (mk8.shell.env, " +
                "mk8.shell.signed.env) can only be managed by the user " +
                "directly or by mk8.shell.startup. No mk8.shell command " +
                "may read, write, copy, move, delete, hash, or list them.\n" +
                $"  ✗ Wrong: any operation targeting '{originalPath}'\n" +
                "These files contain sandbox configuration and cryptographic " +
                "signatures. Use { \"verb\": \"Mk8Env\", \"args\": [] } to " +
                "see your merged environment variables instead.");
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
