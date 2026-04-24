using Microsoft.EntityFrameworkCore;

using SharpClaw.Modules.AgentOrchestration.Models;

namespace SharpClaw.Modules.AgentOrchestration;

public class AgentOrchestrationDbContext(DbContextOptions<AgentOrchestrationDbContext> options)
    : DbContext(options)
{
    public DbSet<SkillDB> Skills => Set<SkillDB>();
    public DbSet<ScheduledJobDB> ScheduledJobs => Set<ScheduledJobDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduledJobDB>(e =>
        {
            e.Property(j => j.Status).HasConversion<string>();
            e.Property(j => j.MissedFirePolicy).HasConversion<string>();
        });
    }
}
