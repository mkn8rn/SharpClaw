namespace SharpClaw.Contracts.DTOs.Auth;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);
