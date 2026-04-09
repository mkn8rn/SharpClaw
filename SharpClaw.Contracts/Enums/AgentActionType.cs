namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Category tag for agent job records. All tool calls route through modules
/// and use <see cref="ModuleAction"/>. The specific tool is identified by
/// <c>AgentJobDB.ActionKey</c> (the prefixed module tool name).
/// <para>
/// Historical job records may contain legacy integer values (0–54) from
/// before the all-module migration. These values are no longer defined in
/// the enum but are harmless — the database column stores them as integers
/// and they deserialise to the underlying <see langword="int"/> representation.
/// </para>
/// </summary>
public enum AgentActionType
{
    /// <summary>
    /// The only valid value for new jobs. Every tool is module-provided.
    /// The specific tool is identified by <c>AgentJobDB.ActionKey</c>,
    /// which holds the prefixed tool name (e.g. "cu_enumerate_windows").
    /// <c>ScriptJson</c> carries the module envelope for parameter extraction:
    /// <c>{ "module": "computer_use", "tool": "enumerate_windows", "params": { ... } }</c>
    /// </summary>
    ModuleAction = 100,
}
