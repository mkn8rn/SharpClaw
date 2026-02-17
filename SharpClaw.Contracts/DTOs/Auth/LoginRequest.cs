namespace SharpClaw.Contracts.DTOs.Auth;

public sealed record LoginRequest(
    string Username,
    string Password,
    bool RememberMe = false);
