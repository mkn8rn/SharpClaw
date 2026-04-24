using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.BotIntegration.Models;

namespace SharpClaw.Modules.BotIntegration;

/// <summary>
/// EF DbContext for BotIntegration-owned entities.
/// </summary>
public sealed class BotIntegrationDbContext(DbContextOptions<BotIntegrationDbContext> options)
    : DbContext(options)
{
    public DbSet<BotIntegrationDB> BotIntegrations => Set<BotIntegrationDB>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            var now = DateTimeOffset.UtcNow;
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                    entry.Entity.Id = Guid.NewGuid();
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                if (entry.Entity.UpdatedAt == default)
                    entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
