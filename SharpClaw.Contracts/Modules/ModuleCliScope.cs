namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Scope at which a <see cref="ModuleCliCommand"/> is registered in the CLI REPL.
/// </summary>
public enum ModuleCliScope
{
    /// <summary>
    /// Registered as a top-level verb
    /// (e.g. <c>myverb sub arg1 arg2</c>).
    /// The handler receives the full argument array including the verb.
    /// </summary>
    TopLevel,

    /// <summary>
    /// Registered under the <c>resource</c> namespace
    /// (e.g. <c>resource mytype add ...</c>).
    /// The handler receives the full argument array starting from <c>resource</c>.
    /// </summary>
    ResourceType
}
