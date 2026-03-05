using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.LocalModels;

public sealed record DownloadModelRequest(
    string Url,
    string? Name = null,
    string? Quantization = null,
    int? GpuLayers = null);

public sealed record LoadModelRequest(
    int? GpuLayers = null,
    uint? ContextSize = null);

public sealed record ResolvedModelFileResponse(
    string DownloadUrl,
    string Filename,
    string? Quantization);

public sealed record LocalModelFileResponse(
    Guid Id,
    Guid ModelId,
    string ModelName,
    string SourceUrl,
    string FilePath,
    long FileSizeBytes,
    string? Quantization,
    LocalModelStatus Status,
    double DownloadProgress,
    bool IsLoaded);
