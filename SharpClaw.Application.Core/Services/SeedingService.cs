using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Utils.Security;

namespace SharpClaw.Application.Services;

/// <summary>
/// Seeds the admin role and initial admin user on startup when they do not already exist.
/// Reads credentials from the <c>Admin:Username</c> and <c>Admin:Password</c> configuration keys.
/// </summary>
public sealed class SeedingService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SeedingService> logger) : IHostedLifecycleService
{
    private const string AdminRoleName = "Admin";

    public Task StartingAsync(CancellationToken cancellationToken) => SeedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();

        var adminRole = await SeedAdminRoleAsync(db, ct);
        await SeedAdminUserAsync(db, adminRole, ct);
    }

    private async Task<RoleDB> SeedAdminRoleAsync(SharpClawDbContext db, CancellationToken ct)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == AdminRoleName, ct);
        if (existing is not null)
            return existing;

        logger.LogInformation("Seeding '{Role}' role.", AdminRoleName);

        var role = new RoleDB { Name = AdminRoleName };

        // TODO: Grant all permissions to the admin role once a permission model is defined.

        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role;
    }

    private async Task SeedAdminUserAsync(SharpClawDbContext db, RoleDB adminRole, CancellationToken ct)
    {
        var hasAdmin = await db.Users.AnyAsync(u => u.RoleId == adminRole.Id, ct);
        if (hasAdmin)
            return;

        var username = configuration["Admin:Username"];
        var password = configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No admin user exists and Admin:Username / Admin:Password are not configured. Skipping admin seed.");
            return;
        }

        logger.LogInformation("Seeding admin user '{Username}'.", username);

        var salt = PasswordHelper.GenerateSalt();
        var hash = PasswordHelper.Hash(password, salt);

        var user = new UserDB
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            RoleId = adminRole.Id
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }
}
