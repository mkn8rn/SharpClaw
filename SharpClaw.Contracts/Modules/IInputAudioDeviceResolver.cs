namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Resolves input audio device details from the Transcription module's data store.
/// Registered by the Transcription module; consumed by host services that need
/// device info without taking a direct module reference.
/// </summary>
public interface IInputAudioDeviceResolver
{
    /// <summary>
    /// Returns the device identifier and name for the given input audio resource ID,
    /// or <c>null</c> if no matching device is found.
    /// </summary>
    Task<(string DeviceIdentifier, string Name)?> GetDeviceAsync(Guid id, CancellationToken ct = default);
}
