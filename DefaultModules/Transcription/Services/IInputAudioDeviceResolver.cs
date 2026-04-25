namespace SharpClaw.Modules.Transcription.Services;

/// <summary>
/// Resolves input audio device details from the Transcription module's data store.
/// </summary>
internal interface IInputAudioDeviceResolver
{
    /// <summary>
    /// Returns the device identifier and name for the given input audio resource ID,
    /// or <c>null</c> if no matching device is found.
    /// </summary>
    Task<(string DeviceIdentifier, string Name)?> GetDeviceAsync(Guid id, CancellationToken ct = default);
}
