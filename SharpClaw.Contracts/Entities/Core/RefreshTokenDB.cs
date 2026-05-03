using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

public class RefreshTokenDB : BaseEntity
{
    public required string Token { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }

    public Guid UserId { get; set; }
    public UserDB User { get; set; } = null!;
}
