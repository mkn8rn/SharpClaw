namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Trigger and parameter keys for the filesystem-trigger surface owned by
/// the agent-orchestration module (rolled in from the former
/// <c>sharpclaw_filesystem_triggers</c> module). String values are
/// persisted verbatim in binding rows and serialized scripts and remain
/// wire-compatible with the legacy module.
/// </summary>
public static class FilesystemTriggerKeys
{
    /// <summary>Trigger-key value persisted in <c>TaskTriggerBindingDB.Kind</c>.</summary>
    public const string FileChanged = "FileChanged";

    // Parameter names — must match TaskTriggerDefinition property names.
    public const string WatchPath   = "WatchPath";
    public const string FilePattern = "FilePattern";
    public const string FileEvents  = "FileEvents";
}
