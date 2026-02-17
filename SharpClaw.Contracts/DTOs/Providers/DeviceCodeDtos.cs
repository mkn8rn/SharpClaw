namespace SharpClaw.Contracts.DTOs.Providers;

/// <summary>
/// Returned when a device code flow is initiated for a provider.
/// The user must visit <see cref="VerificationUri"/> and enter <see cref="UserCode"/>.
/// </summary>
public sealed record DeviceCodeResponse(string UserCode, string VerificationUri, int ExpiresInSeconds);

/// <summary>
/// Holds internal state for an in-progress device code flow.
/// </summary>
public sealed record DeviceCodeSession(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);
