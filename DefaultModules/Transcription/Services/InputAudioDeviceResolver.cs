using Microsoft.EntityFrameworkCore;

namespace SharpClaw.Modules.Transcription.Services;

/// <summary>
/// Resolves input audio device details from <see cref="TranscriptionDbContext"/>.
/// </summary>
internal sealed class InputAudioDeviceResolver(TranscriptionDbContext db) : IInputAudioDeviceResolver
{
    public async Task<(string DeviceIdentifier, string Name)?> GetDeviceAsync(
        Guid id, CancellationToken ct = default)
    {
        var device = await db.InputAudios
            .Where(d => d.Id == id)
            .Select(d => new { d.DeviceIdentifier, d.Name })
            .FirstOrDefaultAsync(ct);

        if (device is null || device.DeviceIdentifier is null)
            return null;

        return (device.DeviceIdentifier, device.Name);
    }
}
