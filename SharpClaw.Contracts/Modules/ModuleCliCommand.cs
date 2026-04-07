namespace SharpClaw.Contracts.Modules;

/// <summary>
/// A CLI command provided by a module.
/// Discovered during module registration and dispatched by the CLI REPL.
/// </summary>
/// <param name="Name">Primary command name (lowercase).</param>
/// <param name="Aliases">Alternative names (e.g. short forms). Empty array if none.</param>
/// <param name="Scope">Where in the CLI hierarchy this command is registered.</param>
/// <param name="Description">One-line description for help output.</param>
/// <param name="UsageLines">Detailed usage lines shown when the command is invoked without valid sub-arguments.</param>
/// <param name="Handler">
/// Async handler that receives the full argument array, a scoped
/// <see cref="IServiceProvider"/>, and a <see cref="CancellationToken"/>.
/// Writes output directly to <see cref="Console"/>.
/// </param>
public sealed record ModuleCliCommand(
    string Name,
    string[] Aliases,
    ModuleCliScope Scope,
    string Description,
    string[] UsageLines,
    Func<string[], IServiceProvider, CancellationToken, Task> Handler
);
