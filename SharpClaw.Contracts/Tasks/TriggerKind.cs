namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Self-registration trigger type for a task class.
/// Maps to a corresponding <c>[Schedule]</c>, <c>[OnEvent]</c>,
/// <c>[OnFileChanged]</c>, etc. attribute.
/// </summary>
public enum TriggerKind
{
    Cron,
    Event,
    FileChanged,
    ProcessStarted,
    ProcessStopped,
    Webhook,
    HostReachable,
    HostUnreachable,
    TaskCompleted,
    TaskFailed,
    WindowFocused,
    WindowBlurred,
    Hotkey,
    SystemIdle,
    SystemActive,
    ScreenLocked,
    ScreenUnlocked,
    NetworkChanged,
    DeviceConnected,
    DeviceDisconnected,
    QueryReturnsRows,
    MetricThreshold,
    Startup,
    Shutdown,
    OsShortcut,
    /// <summary>Custom source declared via <c>[OnTrigger("SourceName")]</c>.</summary>
    Custom,
}
