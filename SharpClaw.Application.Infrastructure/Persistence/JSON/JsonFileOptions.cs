using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Application.Infrastructure.Models.Messages;
using SharpClaw.Application.Infrastructure.Models.Tasks;

namespace SharpClaw.Infrastructure.Persistence.JSON;

public sealed class JsonFileOptions
{
    /// <summary>
    /// Directory where JSON data files are stored.
    /// Defaults to a "data" folder next to the application.
    /// </summary>
    public string DataDirectory { get; set; } = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
        "Data");

    /// <summary>
    /// When true (default), all entity and join-table files are AES-256-GCM
    /// encrypted on disk. The read path auto-detects plaintext regardless of
    /// this flag, so legacy data is always loadable and toggling to false is
    /// non-destructive (new writes become plaintext, existing encrypted files
    /// still load).
    /// </summary>
    public bool EncryptAtRest { get; set; } = true;

    /// <summary>
    /// Entity types that are skipped during <see cref="JsonFilePersistenceService.LoadAsync"/>
    /// to reduce memory pressure at startup. These are high-volume, append-heavy
    /// tables whose data will be loaded on demand in a future phase.
    /// </summary>
    public HashSet<Type> ColdEntityTypes { get; } =
    [
        typeof(ChatMessageDB),
        typeof(AgentJobDB),
        typeof(AgentJobLogEntryDB),
        typeof(TaskInstanceDB),
        typeof(TaskExecutionLogDB),
        typeof(TaskOutputEntryDB),
        typeof(TranscriptionSegmentDB),
    ];
}
