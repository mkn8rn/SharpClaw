using Mk8.Shell;
using SharpClaw.Application.Infrastructure.Models.Resources;

namespace SharpClaw.Application.Infrastructure.Shell;

/// <summary>
/// Builds <see cref="Mk8WorkspaceContext"/> from <see cref="SystemUserDB"/>
/// at job startup. This lives in Infrastructure because it depends on
/// the EF entity, while the rest of mk8.shell is in its own project.
/// </summary>
public static class Mk8WorkspaceFactory
{
    private static string DefaultSandboxRoot(string username) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(@"C:\Users", username, "sandbox")
            : Path.Combine("/home", username, "sandbox");

    public static Mk8WorkspaceContext Create(
        SystemUserDB systemUser,
        IReadOnlyDictionary<string, string>? additionalVariables = null)
    {
        ArgumentNullException.ThrowIfNull(systemUser);

        var sandboxRoot = Path.GetFullPath(
            systemUser.SandboxRoot ?? DefaultSandboxRoot(systemUser.Username));

        var workingDirectory = systemUser.WorkingDirectory is not null
            ? Path.GetFullPath(systemUser.WorkingDirectory)
            : sandboxRoot;

        if (!Mk8PathSanitizer.IsInsideSandbox(workingDirectory, sandboxRoot))
            throw new InvalidOperationException(
                $"SystemUser '{systemUser.Username}' working directory " +
                $"'{workingDirectory}' is outside sandbox '{sandboxRoot}'.");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (additionalVariables is not null)
        {
            foreach (var (key, value) in additionalVariables)
                variables.TryAdd(key, value);
        }

        return new Mk8WorkspaceContext(
            SandboxRoot: sandboxRoot,
            WorkingDirectory: workingDirectory,
            RunAsUser: systemUser.Username,
            Variables: variables);
    }

    public static Mk8ExecutionOptions ResolveOptions(
        SystemUserDB systemUser,
        Mk8ExecutionOptions? scriptOverrides)
    {
        var defaults = Mk8ExecutionOptions.Default;
        var overrides = scriptOverrides ?? defaults;

        var maxRetries = systemUser.DefaultMaxRetries > 0
            ? Math.Min(overrides.MaxRetries, systemUser.DefaultMaxRetries)
            : overrides.MaxRetries;

        var stepTimeout = systemUser.DefaultStepTimeoutSeconds > 0
            ? Min(overrides.StepTimeout, TimeSpan.FromSeconds(systemUser.DefaultStepTimeoutSeconds))
            : overrides.StepTimeout;

        return overrides with
        {
            MaxRetries = maxRetries,
            RetryDelay = overrides.RetryDelay == default
                ? defaults.RetryDelay : overrides.RetryDelay,
            StepTimeout = stepTimeout == default
                ? defaults.StepTimeout : stepTimeout,
            ScriptTimeout = overrides.ScriptTimeout == default
                ? defaults.ScriptTimeout : overrides.ScriptTimeout,
        };
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) =>
        a == default ? b : b == default ? a : a < b ? a : b;
}
