using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Core.Clients;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.DTOs.Transcription;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

/// <summary>
/// Manages audio device CRUD. Transcription job lifecycle is handled
/// by <see cref="AgentJobService"/> via the job/permission system.
/// </summary>
public sealed class TranscriptionService(SharpClawDbContext db, IAudioCaptureProvider capture)
{
    // ═══════════════════════════════════════════════════════════════
    // Audio device CRUD
    // ═══════════════════════════════════════════════════════════════

    public async Task<AudioDeviceResponse> CreateDeviceAsync(CreateAudioDeviceRequest request, CancellationToken ct = default)
    {
        var device = new AudioDeviceDB
        {
            Name = request.Name,
            DeviceIdentifier = request.DeviceIdentifier,
            Description = request.Description
        };

        db.AudioDevices.Add(device);
        await db.SaveChangesAsync(ct);

        return ToResponse(device);
    }

    public async Task<IReadOnlyList<AudioDeviceResponse>> ListDevicesAsync(CancellationToken ct = default)
    {
        var devices = await db.AudioDevices
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        return devices.Select(ToResponse).ToList();
    }

    public async Task<AudioDeviceResponse?> GetDeviceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.AudioDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is not null ? ToResponse(device) : null;
    }

    public async Task<AudioDeviceResponse?> UpdateDeviceAsync(Guid id, UpdateAudioDeviceRequest request, CancellationToken ct = default)
    {
        var device = await db.AudioDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return null;

        if (request.Name is not null) device.Name = request.Name;
        if (request.DeviceIdentifier is not null) device.DeviceIdentifier = request.DeviceIdentifier;
        if (request.Description is not null) device.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    public async Task<bool> DeleteDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.AudioDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return false;

        db.AudioDevices.Remove(device);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // Sync — discover system audio devices and import new ones
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Discovers all audio input devices on the current system and
    /// imports any that are not already in the database. Duplicates
    /// (matched by <see cref="AudioDeviceDB.DeviceIdentifier"/>) are
    /// skipped.
    /// </summary>
    public async Task<AudioDeviceSyncResult> SyncDevicesAsync(CancellationToken ct = default)
    {
        var systemDevices = capture.ListDevices();

        var existingIdentifiers = await db.AudioDevices
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

            var device = new AudioDeviceDB
            {
                Name = name,
                DeviceIdentifier = id,
                Description = "Synced from system audio devices",
            };

            db.AudioDevices.Add(device);
            imported.Add(name);
        }

        if (imported.Count > 0)
            await db.SaveChangesAsync(ct);

        return new AudioDeviceSyncResult(
            imported.Count,
            skipped.Count,
            imported,
            skipped);
    }

    private static AudioDeviceResponse ToResponse(AudioDeviceDB d) =>
        new(d.Id, d.Name, d.DeviceIdentifier, d.Description, d.SkillId, d.CreatedAt);
}
