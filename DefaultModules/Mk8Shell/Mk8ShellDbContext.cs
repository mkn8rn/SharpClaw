using Microsoft.EntityFrameworkCore;

using SharpClaw.Modules.Mk8Shell.Models;

namespace SharpClaw.Modules.Mk8Shell;

public class Mk8ShellDbContext(DbContextOptions<Mk8ShellDbContext> options) : DbContext(options)
{
    public DbSet<ContainerDB> Containers => Set<ContainerDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerDB>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Type).HasConversion<string>();
        });
    }
}
