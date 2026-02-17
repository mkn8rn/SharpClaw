namespace SharpClaw.Contracts.DTOs.Auth;

public sealed record InvalidateRequest(IReadOnlyList<Guid> UserIds);
