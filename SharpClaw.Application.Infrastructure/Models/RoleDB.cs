using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

public class RoleDB : BaseEntity
{
    public required string Name { get; set; }

    // TODO: Add permissions collection when a permission model is defined.

    public RolePermissionsDB? Permissions { get; set; }

    public ICollection<UserDB> Users { get; set; } = [];
}
