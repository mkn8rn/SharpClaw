using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.Enums;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Tests.Tasks;

/// <summary>
/// Tests for Phase 6: <see cref="ScheduledJobService"/> CRUD, validation,
/// pause/resume, and cron preview.
/// </summary>
[TestFixture]
public class ScheduledJobCrudTests
{
    private SharpClawDbContext _db = null!;
    private ScheduledJobService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<SharpClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db  = new SharpClawDbContext(opts);
        _svc = new ScheduledJobService(_db, NullLogger<ScheduledJobService>.Instance);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────
    // Validation helper (static) — SCHED001-004
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void ValidateCronFields_BothCronAndInterval_ThrowsSCHED001()
    {
        var act = () => ScheduledJobService.ValidateCronFields(
            "0 * * * *", null, TimeSpan.FromHours(1), null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrBothSchedules}*");
    }

    [Test]
    public void ValidateCronFields_InvalidCronExpression_ThrowsSCHED002()
    {
        var act = () => ScheduledJobService.ValidateCronFields(
            "not-a-cron", null, null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrInvalidCron}*");
    }

    [Test]
    public void ValidateCronFields_UnknownTimezone_ThrowsSCHED003()
    {
        var act = () => ScheduledJobService.ValidateCronFields(
            "0 * * * *", "Invalid/Timezone", null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrInvalidTz}*");
    }

    [Test]
    public void ValidateCronFields_NeverFiringCron_ReturnsWarningAndAllowsCreation()
    {
        // Feb 30 never exists — expression will have no future occurrences
        // Use a past-only expression: "0 0 30 2 *" (Feb 30 - never fires)
        var (nextRunAt, warnings) = ScheduledJobService.ValidateCronFields(
            "0 0 30 2 *", null, null, null);

        warnings.Should().ContainSingle(w => w.Contains(ScheduledJobService.WarnNeverFires));
        nextRunAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ValidateCronFields_ValidCron_AutoDerivesNextRunAt()
    {
        var (nextRunAt, warnings) = ScheduledJobService.ValidateCronFields(
            "0 * * * *", null, null, null);

        warnings.Should().BeEmpty();
        nextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Test]
    public void ValidateCronFields_SuppliedNextRunAtIsPreserved_WhenCronValid()
    {
        var supplied = DateTimeOffset.UtcNow.AddDays(7);

        var (nextRunAt, _) = ScheduledJobService.ValidateCronFields(
            "0 * * * *", null, null, supplied);

        nextRunAt.Should().Be(supplied);
    }

    // ─────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_WithCronExpression_PersistsAndAutoDerivesNextRunAt()
    {
        var req = new CreateScheduledJobRequest(
            Name: "hourly",
            CronExpression: "0 * * * *");

        var result = await _svc.CreateAsync(req);

        result.Id.Should().NotBeEmpty();
        result.CronExpression.Should().Be("0 * * * *");
        result.NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
        result.Status.Should().Be(ScheduledTaskStatus.Pending);
    }

    [Test]
    public async Task CreateAsync_WithRepeatInterval_PersistsCorrectly()
    {
        var next = DateTimeOffset.UtcNow.AddHours(1);
        var req  = new CreateScheduledJobRequest(
            Name: "interval-job",
            NextRunAt: next,
            RepeatInterval: TimeSpan.FromHours(2));

        var result = await _svc.CreateAsync(req);

        result.RepeatInterval.Should().Be(TimeSpan.FromHours(2));
        result.NextRunAt.Should().BeCloseTo(next, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task CreateAsync_BothCronAndInterval_Throws()
    {
        var req = new CreateScheduledJobRequest(
            Name: "bad",
            CronExpression: "0 * * * *",
            RepeatInterval: TimeSpan.FromHours(1));

        var act = async () => await _svc.CreateAsync(req);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrBothSchedules}*");
    }

    [Test]
    public async Task GetByIdAsync_ExistingJob_ReturnsResponse()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("job1", CronExpression: "0 * * * *"));

        var fetched = await _svc.GetByIdAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("job1");
    }

    [Test]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        var result = await _svc.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Test]
    public async Task ListAsync_ReturnsAllJobs_OrderedByNextRunAt()
    {
        var later  = DateTimeOffset.UtcNow.AddHours(2);
        var sooner = DateTimeOffset.UtcNow.AddHours(1);

        await _svc.CreateAsync(new CreateScheduledJobRequest("b", NextRunAt: later,
            RepeatInterval: TimeSpan.FromHours(1)));
        await _svc.CreateAsync(new CreateScheduledJobRequest("a", NextRunAt: sooner,
            RepeatInterval: TimeSpan.FromHours(1)));

        var list = await _svc.ListAsync();

        list.Should().HaveCount(2);
        list[0].NextRunAt.Should().BeBefore(list[1].NextRunAt);
    }

    [Test]
    public async Task UpdateAsync_ChangesName_Persists()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("original", CronExpression: "0 * * * *"));

        var updated = await _svc.UpdateAsync(created.Id,
            new UpdateScheduledJobRequest(Name: "renamed"));

        updated!.Name.Should().Be("renamed");
    }

    [Test]
    public async Task UpdateAsync_MissingId_ReturnsNull()
    {
        var result = await _svc.UpdateAsync(Guid.NewGuid(),
            new UpdateScheduledJobRequest(Name: "x"));

        result.Should().BeNull();
    }

    [Test]
    public async Task UpdateAsync_BothCronAndInterval_Throws()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("job", CronExpression: "0 * * * *"));

        var act = async () => await _svc.UpdateAsync(created.Id,
            new UpdateScheduledJobRequest(
                CronExpression: "0 0 * * *",
                RepeatInterval: TimeSpan.FromHours(1)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrBothSchedules}*");
    }

    [Test]
    public async Task DeleteAsync_ExistingJob_RemovesFromDb()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("del-me", CronExpression: "0 * * * *"));

        var deleted = await _svc.DeleteAsync(created.Id);
        var fetched = await _svc.GetByIdAsync(created.Id);

        deleted.Should().BeTrue();
        fetched.Should().BeNull();
    }

    [Test]
    public async Task DeleteAsync_MissingId_ReturnsFalse()
    {
        var result = await _svc.DeleteAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────
    // Pause / Resume
    // ─────────────────────────────────────────────────────────────

    [Test]
    public async Task PauseAsync_PendingJob_SetsStatusToPaused()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("pauseable", CronExpression: "0 * * * *"));

        var paused = await _svc.PauseAsync(created.Id);

        paused!.Status.Should().Be(ScheduledTaskStatus.Paused);
    }

    [Test]
    public async Task ResumeAsync_PausedJob_SetsStatusToPending()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("resumeable", CronExpression: "0 * * * *"));
        await _svc.PauseAsync(created.Id);

        var resumed = await _svc.ResumeAsync(created.Id);

        resumed!.Status.Should().Be(ScheduledTaskStatus.Pending);
    }

    [Test]
    public async Task ResumeAsync_PausedCronJob_RecomputesNextRunAt()
    {
        // Create with a NextRunAt far in the past to simulate it expiring while paused
        var pastNext = DateTimeOffset.UtcNow.AddHours(-1);
        var entity = new ScheduledJobDB
        {
            Name           = "stale-cron",
            CronExpression = "0 * * * *",
            NextRunAt      = pastNext,
            Status         = ScheduledTaskStatus.Paused,
        };
        _db.ScheduledTasks.Add(entity);
        await _db.SaveChangesAsync();

        var resumed = await _svc.ResumeAsync(entity.Id);

        resumed!.Status.Should().Be(ScheduledTaskStatus.Pending);
        resumed.NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task PauseAsync_MissingId_ReturnsNull()
    {
        var result = await _svc.PauseAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Test]
    public async Task ResumeAsync_MissingId_ReturnsNull()
    {
        var result = await _svc.ResumeAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────
    // Preview — stateless
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void PreviewExpression_ValidCron_ReturnsOccurrences()
    {
        var result = ScheduledJobService.PreviewExpression("0 * * * *", count: 5);

        result.Expression.Should().Be("0 * * * *");
        result.NextOccurrences.Should().HaveCount(5);
        result.NextOccurrences.Should().BeInAscendingOrder();
    }

    [Test]
    public void PreviewExpression_InvalidCron_Throws()
    {
        var act = () => ScheduledJobService.PreviewExpression("not-valid");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrInvalidCron}*");
    }

    [Test]
    public void PreviewExpression_InvalidTimezone_Throws()
    {
        var act = () => ScheduledJobService.PreviewExpression(
            "0 * * * *", "Bad/Zone");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ScheduledJobService.ErrInvalidTz}*");
    }

    [Test]
    public async Task PreviewJobAsync_CronJob_ReturnsOccurrences()
    {
        var created = await _svc.CreateAsync(
            new CreateScheduledJobRequest("preview-job", CronExpression: "0 * * * *"));

        var preview = await _svc.PreviewJobAsync(created.Id, count: 3);

        preview.Should().NotBeNull();
        preview!.Expression.Should().Be("0 * * * *");
        preview.NextOccurrences.Should().HaveCount(3);
    }

    [Test]
    public async Task PreviewJobAsync_IntervalJobWithNoCron_ReturnsNull()
    {
        var created = await _svc.CreateAsync(new CreateScheduledJobRequest(
            "interval", NextRunAt: DateTimeOffset.UtcNow.AddHours(1),
            RepeatInterval: TimeSpan.FromHours(1)));

        var preview = await _svc.PreviewJobAsync(created.Id);

        preview.Should().BeNull();
    }

    [Test]
    public async Task PreviewJobAsync_MissingId_ReturnsNull()
    {
        var result = await _svc.PreviewJobAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
