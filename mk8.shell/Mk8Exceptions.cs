namespace Mk8.Shell;

/// <summary>
/// Thrown when the compiler encounters an invalid verb or argument set.
/// </summary>
public sealed class Mk8CompileException(
    Mk8ShellVerb verb, string reason)
    : InvalidOperationException($"mk8.shell compile error [{verb}]: {reason}")
{
    public Mk8ShellVerb Verb { get; } = verb;
    public string Reason { get; } = reason;
}

/// <summary>
/// Thrown when a path resolves outside the allowed sandbox directory.
/// </summary>
public sealed class Mk8PathViolationException(
    string attemptedPath, string sandboxRoot)
    : InvalidOperationException(
        $"mk8.shell path violation: '{attemptedPath}' resolves outside sandbox '{sandboxRoot}'.")
{
    public string AttemptedPath { get; } = attemptedPath;
    public string SandboxRoot { get; } = sandboxRoot;
}

/// <summary>
/// Thrown when a URL fails mk8.shell security validation.
/// </summary>
public sealed class Mk8UrlViolationException(
    string attemptedUrl, string reason)
    : InvalidOperationException(
        $"mk8.shell URL violation: '{attemptedUrl}' — {reason}")
{
    public string AttemptedUrl { get; } = attemptedUrl;
    public string Reason { get; } = reason;
}
