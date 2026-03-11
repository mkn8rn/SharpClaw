namespace SharpClaw.Contracts.DTOs.Users;

public sealed record UserEntry(
    Guid Id,
    string Username,
    string? Bio,
    Guid? RoleId,
    string? RoleName,
    bool IsUserAdmin);

public sealed record SetUserRoleRequest(Guid RoleId);
