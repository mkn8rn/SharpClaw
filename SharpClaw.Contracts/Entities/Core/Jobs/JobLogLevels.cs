namespace SharpClaw.Contracts.Entities.Core.Jobs;

/// <summary>
/// Canonical severity strings persisted in <see cref="AgentJobLogEntryDB.Level"/>
/// and <c>TaskExecutionLogDB.Level</c>.
/// <para>
/// These exist so callers don't repeat raw string literals across
/// <see cref="SharpClaw.Application.Services"/> and module-side adapters.
/// A typo in a level today silently produces an unparseable severity at
/// the persistence/UI layer; routing every write through the constants
/// here removes that class of bug and gives us one place to evolve the
/// surface (e.g. promoting it to an enum) when we choose to.
/// </para>
/// </summary>
public static class JobLogLevels
{
    /// <summary>Default informational log entry.</summary>
    public const string Info = "Info";

    /// <summary>Recoverable problem or rejected request.</summary>
    public const string Warning = "Warning";

    /// <summary>Failure that ended the job or step.</summary>
    public const string Error = "Error";
}
