namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Module-owned global-flag keys for the context-tools surface, rolled
/// into agent-orchestration from the former <c>sharpclaw_context_tools</c>
/// module. The string values are the canonical wire identifiers persisted
/// in <c>GlobalFlagDB.FlagKey</c>.
/// </summary>
public static class ContextToolsPermissionKeys
{
    /// <summary>
    /// Grants permission to read conversation history from threads on
    /// channels other than the active one.
    /// </summary>
    public const string CanReadCrossThreadHistory = "CanReadCrossThreadHistory";
}
