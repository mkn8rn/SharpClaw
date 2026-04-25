namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// A single trigger binding declared on a task class via a self-registration
/// attribute. Produced by the parser; persisted as JSON on TaskDefinitionDB.
/// </summary>
public sealed record TaskTriggerDefinition
{
    /// <summary>
    /// String key identifying the trigger source that owns this definition.
    /// Matches <see cref="ITaskTriggerSource.TriggerKey"/> (or one of
    /// <see cref="ITaskTriggerSource.TriggerKeys"/>). Set for both core and
    /// module-owned triggers; replaces the former <c>Kind</c> enum.
    /// </summary>
    public string? TriggerKey { get; init; }

    // ── Cron ──────────────────────────────────────────────────────
    public string? CronExpression { get; init; }
    public string? CronTimezone   { get; init; }

    // ── Event ─────────────────────────────────────────────────────
    /// <summary>SharpClawEventType flag name(s) as a comma-separated string.</summary>
    public string? EventType   { get; init; }
    public string? EventFilter { get; init; }

    // ── FileChanged ───────────────────────────────────────────────
    public string?        WatchPath   { get; init; }
    public string?        FilePattern { get; init; }
    public FileWatchEvent FileEvents  { get; init; }

    // ── Process ───────────────────────────────────────────────────
    public string? ProcessName { get; init; }

    // ── Webhook ───────────────────────────────────────────────────
    public string? WebhookRoute            { get; init; }
    public string? WebhookSecretEnvVar     { get; init; }
    public string? WebhookSignatureHeader  { get; init; }

    // ── Host probe ────────────────────────────────────────────────
    public string? HostName { get; init; }
    public int?    HostPort { get; init; }

    // ── Task chaining ─────────────────────────────────────────────
    public string? SourceTaskName { get; init; }

    // ── Hotkey ────────────────────────────────────────────────────
    public string? HotkeyCombo { get; init; }

    // ── Idle ─────────────────────────────────────────────────────
    public int? IdleMinutes { get; init; }

    // ── Network ───────────────────────────────────────────────────
    public string?       NetworkSsid  { get; init; }
    public NetworkState  NetworkState { get; init; }

    // ── Device ───────────────────────────────────────────────────
    public string? DeviceClass       { get; init; }
    public string? DeviceNamePattern { get; init; }

    // ── Query rows ───────────────────────────────────────────────
    public string? SqlQuery              { get; init; }
    public int?    QueryPollIntervalSecs { get; init; }

    // ── Metric threshold ─────────────────────────────────────────
    public string?            MetricSource           { get; init; }
    public double?            MetricThreshold        { get; init; }
    public ThresholdDirection MetricDirection        { get; init; }
    public int?               MetricPollIntervalSecs { get; init; }

    // ── OS shortcut ───────────────────────────────────────────────
    public string? ShortcutLabel    { get; init; }
    public string? ShortcutIcon     { get; init; }
    public string? ShortcutCategory { get; init; }

    // ── Custom source ─────────────────────────────────────────────
    /// <summary>
    /// Kept for serialisation compatibility. Superseded by <see cref="TriggerKey"/>.
    /// </summary>
    public string? CustomSourceName   { get; init; }
    public string? CustomSourceFilter { get; init; }

    // ── Cross-cutting ─────────────────────────────────────────────
    public TriggerConcurrency Concurrency { get; init; } = TriggerConcurrency.SkipIfRunning;

    /// <summary>Source line number in the task script for diagnostic purposes.</summary>
    public int Line { get; init; }
}
