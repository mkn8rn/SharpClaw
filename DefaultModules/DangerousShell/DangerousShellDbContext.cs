using Microsoft.EntityFrameworkCore;

using SharpClaw.Modules.DangerousShell.Models;

namespace SharpClaw.Modules.DangerousShell;

public class DangerousShellDbContext(DbContextOptions<DangerousShellDbContext> options) : DbContext(options)
{
    public DbSet<SystemUserDB> SystemUsers => Set<SystemUserDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemUserDB>(e =>
        {
            e.HasIndex(s => s.Username).IsUnique();
        });
    }
}
