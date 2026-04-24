using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.ComputerUse.Models;

namespace SharpClaw.Modules.ComputerUse;

/// <summary>
/// EF DbContext for ComputerUse-owned entities.
/// Configured at startup by the host using the same storage backend as
/// <c>SharpClawDbContext</c>.
/// </summary>
public sealed class ComputerUseDbContext(DbContextOptions<ComputerUseDbContext> options)
    : DbContext(options)
{
    public DbSet<NativeApplicationDB> NativeApplications => Set<NativeApplicationDB>();
    public DbSet<DisplayDeviceDB> DisplayDevices => Set<DisplayDeviceDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NativeApplicationDB>(e =>
        {
            e.HasIndex(a => a.Alias).IsUnique();
        });
    }

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
