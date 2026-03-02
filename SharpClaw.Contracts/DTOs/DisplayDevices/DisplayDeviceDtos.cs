namespace SharpClaw.Contracts.DTOs.DisplayDevices;

public sealed record CreateDisplayDeviceRequest(
    string Name,
    string? DeviceIdentifier = null,
    int DisplayIndex = 0,
    string? Description = null);

public sealed record UpdateDisplayDeviceRequest(
    string? Name = null,
    string? DeviceIdentifier = null,
    int? DisplayIndex = null,
    string? Description = null);

public sealed record DisplayDeviceResponse(
    Guid Id,
    string Name,
    string? DeviceIdentifier,
    int DisplayIndex,
    string? Description,
    Guid? SkillId,
    DateTimeOffset CreatedAt);

public sealed record DisplayDeviceSyncResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> ImportedNames,
    IReadOnlyList<string> SkippedNames);
