using SharpClaw.Contracts.Entities;

namespace SharpClaw.Contracts.Entities.Core;

/// <summary>
/// Host-owned module document record. Modules access these records only
/// through the module storage capability contract, including when running as
/// sidecars.
/// </summary>
public class ModuleStorageRecordDB : BaseEntity
{
    public required string ModuleId { get; set; }
    public required string StorageName { get; set; }
    public required string RecordKey { get; set; }
    public required string ValueJson { get; set; }
}
