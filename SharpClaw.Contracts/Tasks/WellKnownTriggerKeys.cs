namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Well-known string keys for the trigger sources built into the core.
/// Stored as-is in <c>TaskTriggerBindingDB.Kind</c> and in
/// <see cref="TaskTriggerDefinition.TriggerKey"/>.
/// </summary>
public static class WellKnownTriggerKeys
{
    public const string Cron            = "Cron";
    public const string Event           = "Event";
    public const string FileChanged     = "FileChanged";
    public const string Webhook         = "Webhook";
    public const string HostReachable   = "HostReachable";
    public const string HostUnreachable = "HostUnreachable";
    public const string TaskCompleted   = "TaskCompleted";
    public const string TaskFailed      = "TaskFailed";
    public const string NetworkChanged  = "NetworkChanged";
    public const string MetricThreshold = "MetricThreshold";
    public const string Startup         = "Startup";
    public const string Shutdown        = "Shutdown";
}
