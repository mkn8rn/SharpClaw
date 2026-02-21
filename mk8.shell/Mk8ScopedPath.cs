namespace Mk8.Shell;

/// <summary>
/// Builds workspace-relative paths that can ONLY descend into
/// subdirectories. No <c>..</c> segments, no absolute paths, no
/// escape. The output is always <c>$WORKSPACE/a/b/c</c> form —
/// the compiler then resolves <c>$WORKSPACE</c> and validates
/// via <see cref="Mk8PathSanitizer"/>.
/// <para>
/// Agents use this to construct paths without knowing the actual
/// sandbox root. All path components are validated at build time.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Agent builds:  Mk8ScopedPath.Join("src", "components", "App.tsx")
/// // Produces:      "$WORKSPACE/src/components/App.tsx"
/// //
/// // Agent tries:   Mk8ScopedPath.Join("..", "..", "etc", "passwd")
/// // Throws:        Mk8CompileException
/// </code>
/// </example>
public static class Mk8ScopedPath
{
    /// <summary>
    /// Characters that are never allowed in a path segment.
    /// </summary>
    private static readonly char[] ForbiddenChars =
        ['\0', '\n', '\r', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Joins segments into a <c>$WORKSPACE</c>-relative path.
    /// Every segment is validated — no <c>..</c>, no absolute
    /// paths, no special characters.
    /// </summary>
    /// <param name="segments">
    /// Path components like <c>["src", "components", "App.tsx"]</c>.
    /// Forward slashes within a segment are treated as sub-segments.
    /// </param>
    /// <returns>
    /// A path string like <c>$WORKSPACE/src/components/App.tsx</c>.
    /// </returns>
    public static string Join(params string[] segments)
    {
        if (segments.Length == 0)
            return "$WORKSPACE";

        var parts = new List<string>();
        foreach (var segment in segments)
        {
            // Split on both separators to handle "src/components" as
            // two segments.
            var subParts = segment.Split(['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in subParts)
                parts.Add(ValidateSegment(part));
        }

        return "$WORKSPACE/" + string.Join('/', parts);
    }

    /// <summary>
    /// Joins segments relative to <c>$CWD</c> instead of
    /// <c>$WORKSPACE</c>. Same validation rules.
    /// </summary>
    public static string JoinCwd(params string[] segments)
    {
        if (segments.Length == 0)
            return "$CWD";

        var parts = new List<string>();
        foreach (var segment in segments)
        {
            var subParts = segment.Split(['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in subParts)
                parts.Add(ValidateSegment(part));
        }

        return "$CWD/" + string.Join('/', parts);
    }

    /// <summary>
    /// Validates a resolved (post-variable-expansion) path is
    /// actually within the sandbox. Called by the compiler after
    /// <c>$WORKSPACE</c> has been resolved to an absolute path.
    /// </summary>
    /// <remarks>
    /// This is the defence-in-depth check. Even if an agent somehow
    /// crafted a segment that resolved to <c>..</c> after variable
    /// expansion, this catches it.
    /// </remarks>
    public static void AssertWithinSandbox(
        string resolvedPath, string sandboxRoot)
    {
        // Delegate to the existing sanitizer — this is the single
        // source of truth for sandbox containment.
        Mk8PathSanitizer.Resolve(resolvedPath, sandboxRoot);
    }

    // ═══════════════════════════════════════════════════════════════

    private static string ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                "Path segment is empty or whitespace.");

        // Block traversal
        if (segment is ".." or ".")
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Path segment '{segment}' is not allowed. " +
                "Paths must only descend into subdirectories.");

        // Block hidden traversal via encoded or embedded forms
        if (segment.Contains("..", StringComparison.Ordinal))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Path segment '{segment}' contains '..' traversal.");

        // Block absolute path indicators
        if (Path.IsPathRooted(segment))
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Path segment '{segment}' is an absolute path. " +
                "Use relative segments only.");

        // Block forbidden characters
        if (segment.IndexOfAny(ForbiddenChars) >= 0)
            throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                $"Path segment '{segment}' contains forbidden characters.");

        // Block control characters
        foreach (var c in segment)
        {
            if (char.IsControl(c))
                throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                    $"Path segment contains control character 0x{(int)c:X2}.");
        }

        // Block leading/trailing dots and spaces (Windows)
        if (segment.StartsWith('.') || segment.EndsWith('.') ||
            segment.StartsWith(' ') || segment.EndsWith(' '))
        {
            // Allow dotfiles like .gitignore — only block lone dots
            // and trailing dots which are Windows device tricks
            if (segment.TrimStart('.').Length == 0 || segment.EndsWith('.'))
                throw new Mk8CompileException(Mk8ShellVerb.FileWrite,
                    $"Path segment '{segment}' has suspicious dot pattern.");
        }

        return segment;
    }
}
