using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.OfficeApps.Models;

namespace SharpClaw.Modules.OfficeApps;

/// <summary>
/// EF DbContext for OfficeApps-owned entities.
/// </summary>
public sealed class OfficeAppsDbContext(DbContextOptions<OfficeAppsDbContext> options)
    : DbContext(options)
{
    public DbSet<DocumentSessionDB> DocumentSessions => Set<DocumentSessionDB>();

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
