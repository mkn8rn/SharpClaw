using Microsoft.EntityFrameworkCore;

using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.DTOs.Transcription;
using SharpClaw.Contracts.Modules;
using SharpClaw.Modules.Transcription.Clients;
using SharpClaw.Modules.Transcription.Models;

namespace SharpClaw.Modules.Transcription.Services;

/// <summary>
/// Manages input audio CRUD and transcription job queries.
/// Job lifecycle (submit, approve, cancel, pause, resume) is handled
/// by <see cref="SharpClaw.Application.Services.AgentJobService"/> via the
/// job/permission system.  This service owns the transcription-specific
/// DTO mapping so the core stays free of transcription knowledge.
/// </summary>
public sealed class TranscriptionService(
    TranscriptionDbContext db,
    IAudioCaptureProvider capture,
    IAgentJobReader jobReader)
{
    // ═══════════════════════════════════════════════════════════════
    // Input audio CRUD
    // ═══════════════════════════════════════════════════════════════

    public async Task<InputAudioResponse> CreateDeviceAsync(CreateInputAudioRequest request, CancellationToken ct = default)
    {
        var device = new InputAudioDB
        {
            Name = request.Name,
            DeviceIdentifier = request.DeviceIdentifier,
            Description = request.Description
        };

        db.InputAudios.Add(device);
        await db.SaveChangesAsync(ct);

        return ToResponse(device);
    }

    public async Task<IReadOnlyList<InputAudioResponse>> ListDevicesAsync(CancellationToken ct = default)
    {
        var devices = await db.InputAudios
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        return devices.Select(ToResponse).ToList();
    }

    public async Task<InputAudioResponse?> GetDeviceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.InputAudios.FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is not null ? ToResponse(device) : null;
    }

    public async Task<InputAudioResponse?> UpdateDeviceAsync(Guid id, UpdateInputAudioRequest request, CancellationToken ct = default)
    {
        var device = await db.InputAudios.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return null;

        if (request.Name is not null) device.Name = request.Name;
        if (request.DeviceIdentifier is not null) device.DeviceIdentifier = request.DeviceIdentifier;
        if (request.Description is not null) device.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    public async Task<bool> DeleteDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.InputAudios.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return false;

        db.InputAudios.Remove(device);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // Sync — discover system audio devices and import new ones
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Discovers all audio input devices on the current system and
    /// imports any that are not already in the database. Duplicates
    /// (matched by <see cref="InputAudioDB.DeviceIdentifier"/>) are
    /// skipped.
    /// </summary>
    public async Task<InputAudioSyncResult> SyncDevicesAsync(CancellationToken ct = default)
    {
        var systemDevices = capture.ListDevices();

        var existingIdentifiers = await db.InputAudios
            .Where(d => d.DeviceIdentifier != null)
            .Select(d => d.DeviceIdentifier!)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(
            existingIdentifiers, StringComparer.OrdinalIgnoreCase);

        var imported = new List<string>();
        var skipped = new List<string>();

        foreach (var (id, name) in systemDevices)
        {
            if (existingSet.Contains(id))
            {
                skipped.Add(name);
                continue;
            }

            var device = new InputAudioDB
            {
                Name = name,
                DeviceIdentifier = id,
                Description = "Synced from system audio devices",
            };

            db.InputAudios.Add(device);
            imported.Add(name);
        }

        if (imported.Count > 0)
            await db.SaveChangesAsync(ct);

        return new InputAudioSyncResult(
            imported.Count,
            skipped.Count,
            imported,
            skipped);
    }

    private static InputAudioResponse ToResponse(InputAudioDB d) =>
        new(d.Id, d.Name, d.DeviceIdentifier, d.Description, d.SkillId, d.CreatedAt);

    // ═══════════════════════════════════════════════════════════════
    // Transcription job queries
    // ═══════════════════════════════════════════════════════════════

    private const string TranscriptionActionPrefix = "transcribe_from_audio";

    /// <summary>
    /// Retrieves a single transcription job by ID.
    /// Returns <see langword="null"/> if the job does not exist or is not a
    /// transcription job.
    /// </summary>
    public async Task<TranscriptionJobResponse?> GetTranscriptionJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var job = await jobReader.GetJobAsync(jobId, ct);
        if (job is null || !IsTranscriptionAction(job.ActionKey))
            return null;

        return ToTranscriptionResponse(job);
    }

    /// <summary>
    /// Lists all transcription jobs, optionally filtered by input audio device.
    /// </summary>
    public async Task<IReadOnlyList<TranscriptionJobResponse>> ListTranscriptionJobsAsync(
        Guid? inputAudioId = null, CancellationToken ct = default)
    {
        var jobs = await jobReader.ListJobsByActionPrefixAsync(
            TranscriptionActionPrefix, inputAudioId, ct);
        return jobs.Select(ToTranscriptionResponse).ToList();
    }

    /// <summary>
    /// Lists lightweight transcription job summaries — no segments or heavy payloads.
    /// </summary>
    public async Task<IReadOnlyList<TranscriptionJobSummaryResponse>> ListTranscriptionJobSummariesAsync(
        Guid? inputAudioId = null, CancellationToken ct = default)
    {
        var summaries = await jobReader.ListJobSummariesByActionPrefixAsync(
            TranscriptionActionPrefix, inputAudioId, ct);
        return summaries.Select(ToTranscriptionSummary).ToList();
    }

    /// <summary>
    /// Retrieves transcription segments for a job, optionally filtered by timestamp.
    /// Standalone polling alternative to WebSocket/SSE streaming.
    /// </summary>
    public async Task<IReadOnlyList<TranscriptionSegmentResponse>?> GetSegmentsAsync(
        Guid jobId, DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var exists = await jobReader.JobExistsWithActionPrefixAsync(
            jobId, TranscriptionActionPrefix, ct);
        if (!exists)
            return null;

        var threshold = since ?? DateTimeOffset.MinValue;
        var segments = await db.TranscriptionSegments
            .Where(s => s.AgentJobId == jobId && s.Timestamp > threshold)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);

        return segments.Select(ToSegmentResponse).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // Mapping — AgentJobResponse → transcription-specific DTOs
    // ═══════════════════════════════════════════════════════════════

    private static TranscriptionJobResponse ToTranscriptionResponse(AgentJobResponse job)
    {
        var segments = job.Segments ?? [];
        var finalized = segments.Count(s => !s.IsProvisional);
        var provisional = segments.Count(s => s.IsProvisional);
        var duration = segments.Count > 0
            ? segments.Max(s => s.EndTime) - segments.Min(s => s.StartTime)
            : (double?)null;

        return new TranscriptionJobResponse(
            Id: job.Id,
            ChannelId: job.ChannelId,
            AgentId: job.AgentId,
            ActionKey: job.ActionKey,
            ResourceId: job.ResourceId,
            Status: job.Status,
            EffectiveClearance: job.EffectiveClearance,
            ResultData: job.ResultData,
            ErrorLog: job.ErrorLog,
            Logs: job.Logs,
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt,
            TranscriptionModelId: job.TranscriptionModelId,
            Language: job.Language,
            TranscriptionMode: job.TranscriptionMode,
            WindowSeconds: job.WindowSeconds,
            StepSeconds: job.StepSeconds,
            Segments: [.. segments],
            TotalSegments: segments.Count,
            FinalizedSegments: finalized,
            ProvisionalSegments: provisional,
            TranscribedDurationSeconds: duration,
            JobCost: job.JobCost);
    }

    private static TranscriptionJobSummaryResponse ToTranscriptionSummary(AgentJobSummaryResponse job) =>
        new(
            Id: job.Id,
            ChannelId: job.ChannelId,
            AgentId: job.AgentId,
            ActionKey: job.ActionKey,
            ResourceId: job.ResourceId,
            Status: job.Status,
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt,
            TranscriptionModelId: null,
            Language: null,
            TranscriptionMode: null,
            TotalSegments: 0,
            FinalizedSegments: 0,
            ProvisionalSegments: 0,
            TranscribedDurationSeconds: null);

    private static TranscriptionSegmentResponse ToSegmentResponse(TranscriptionSegmentDB s) =>
        new(s.Id, s.Text, s.StartTime, s.EndTime, s.Confidence, s.Timestamp, s.IsProvisional);

    private static bool IsTranscriptionAction(string? actionKey) =>
        actionKey is not null
        && actionKey.StartsWith(TranscriptionActionPrefix, StringComparison.OrdinalIgnoreCase);
}
