using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SharpClaw.Contracts.Entities.Core.Jobs;
using SharpClaw.Contracts.Enums;
using SharpClaw.Core.Jobs;
using SharpClaw.Runtime.BLL.Modules;
using SharpClaw.Runtime.BLL.Services;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Runtime.INF.Persistence;
using SharpClaw.Shared.DurableStorage;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class HostAgentJobControllerTests
{
    [Test]
    public async Task AddJobLogAsync_WhenJobExists_AppendsDurableRecordAndUpdatesCompactSummary()
    {
        await using var host = TestHost.Create();
        var job = await host.AddJobAsync(AgentJobStatus.Executing);

        await host.Controller.AddJobLogAsync(
            job.Id,
            "arbitrary",
            JobLogLevels.Warning);

        host.SaveProbe.SaveCount.Should().Be(1);
        var logs = await host.ReadLogsAsync(job.Id);
        logs.Should().ContainSingle(record =>
            record.Message == "arbitrary"
            && record.Level == JobLogLevels.Warning);
        var detail = host.Persistence.ToJobDetail(job);
        detail.LogRecordCount.Should().Be(1);
        detail.FinalLogSequence.Should().Be(1);
    }

    [Test]
    public async Task AddJobLogAsync_WhenJobIsMissing_DoesNotCreateDiagnosticStream()
    {
        await using var host = TestHost.Create();
        var missingId = Guid.NewGuid();

        await host.Controller.AddJobLogAsync(missingId, "missing");

        host.SaveProbe.SaveCount.Should().Be(0);
        (await host.ReadLogsAsync(missingId)).Should().BeEmpty();
    }

    [Test]
    public async Task MarkJobCompletedAsync_PersistsCompactStateAndExternalizesResult()
    {
        await using var host = TestHost.Create();
        var job = await host.AddJobAsync(AgentJobStatus.Executing);

        await host.Controller.MarkJobCompletedAsync(
            job.Id,
            resultData: "durable result",
            message: "module complete");

        job.Status.Should().Be(AgentJobStatus.Completed);
        job.CompletedAt.Should().NotBeNull();
        var detail = host.Persistence.ToJobDetail(job);
        detail.ResultArtifact.Should().NotBeNull();
        detail.ResultArtifact!.Preview.Should().Be("durable result");
        (await host.ReadLogsAsync(job.Id)).Select(record => record.Message)
            .Should().Equal("module complete");
        host.OpenSegmentCount.Should().Be(0);
        host.ResidentStreamCount.Should().Be(0);
    }

    [Test]
    public async Task MarkJobFailedAsync_PersistsBoundedFailureMetadataAndDurableDetails()
    {
        await using var host = TestHost.Create();
        var job = await host.AddJobAsync(AgentJobStatus.Executing);
        var exception = new InvalidOperationException("late failure");

        await host.Controller.MarkJobFailedAsync(job.Id, exception);

        job.Status.Should().Be(AgentJobStatus.Failed);
        job.CompletedAt.Should().NotBeNull();
        var detail = host.Persistence.ToJobDetail(job);
        detail.ErrorCode.Should().Be("job_execution_failed");
        detail.ErrorMessage.Should().Be("late failure");
        var messages = (await host.ReadLogsAsync(job.Id))
            .Select(record => record.Message)
            .ToArray();
        messages.Should().Contain("Job failed: late failure");
        messages.Should().Contain(message =>
            message.Contains(nameof(InvalidOperationException)));
    }

    [Test]
    public async Task MarkJobFailedAsync_WhenJobIsTerminal_DoesNotWriteAgain()
    {
        await using var host = TestHost.Create();
        var job = await host.AddJobAsync(AgentJobStatus.Completed);

        await host.Controller.MarkJobFailedAsync(
            job.Id,
            new InvalidOperationException("late failure"));

        host.SaveProbe.SaveCount.Should().Be(0);
        job.Status.Should().Be(AgentJobStatus.Completed);
        (await host.ReadLogsAsync(job.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task MarkJobCancelledAsync_UsesCustomDurableMessage()
    {
        await using var host = TestHost.Create();
        var job = await host.AddJobAsync(AgentJobStatus.Queued);

        await host.Controller.MarkJobCancelledAsync(job.Id, "custom cancel");

        job.Status.Should().Be(AgentJobStatus.Cancelled);
        (await host.ReadLogsAsync(job.Id)).Should().ContainSingle(record =>
            record.Message == "custom cancel"
            && record.Level == JobLogLevels.Warning);
    }

    [Test]
    public async Task CancelStaleJobsByActionPrefixAsync_OnlyCancelsQueuedAndExecutingMatches()
    {
        await using var host = TestHost.Create();
        var queued = await host.AddJobAsync(
            AgentJobStatus.Queued,
            "Curativa.Audio.Start");
        var executing = await host.AddJobAsync(
            AgentJobStatus.Executing,
            "curativa.audio.stop");
        var paused = await host.AddJobAsync(
            AgentJobStatus.Paused,
            "curativa.audio.pause");
        var other = await host.AddJobAsync(
            AgentJobStatus.Queued,
            "other.audio.start");
        host.SaveProbe.Reset();

        await host.Controller.CancelStaleJobsByActionPrefixAsync(
            "curativa.audio.");

        queued.Status.Should().Be(AgentJobStatus.Cancelled);
        executing.Status.Should().Be(AgentJobStatus.Cancelled);
        paused.Status.Should().Be(AgentJobStatus.Paused);
        other.Status.Should().Be(AgentJobStatus.Queued);
        host.SaveProbe.SaveCount.Should().Be(2);
        (await host.ReadLogsAsync(queued.Id)).Should().ContainSingle(record =>
            record.Message == "Cancelled: stale from previous session.");
        (await host.ReadLogsAsync(paused.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task CancelStaleJobsByActionPrefixAsync_WhenPrefixIsBlank_PreservesArgumentException()
    {
        await using var host = TestHost.Create();

        var act = async () =>
            await host.Controller.CancelStaleJobsByActionPrefixAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage(
                "Action key prefix is required. (Parameter 'actionKeyPrefix')");
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly string _root;
        private readonly DurableSegmentStore _records;

        private TestHost(
            string root,
            DurableSegmentStore records,
            SharpClawDbContext db,
            SaveProbe saveProbe,
            ExecutionDiagnosticStore diagnostics,
            DurableExecutionPersistence persistence)
        {
            _root = root;
            _records = records;
            Db = db;
            SaveProbe = saveProbe;
            Diagnostics = diagnostics;
            Persistence = persistence;
            Controller = new HostAgentJobController(
                jobs: null!,
                db,
                new AgentJobAdministrationEngine(),
                new AgentJobLifecycleEngine(),
                persistence);
        }

        public SharpClawDbContext Db { get; }
        public SaveProbe SaveProbe { get; }
        public ExecutionDiagnosticStore Diagnostics { get; }
        public DurableExecutionPersistence Persistence { get; }
        public HostAgentJobController Controller { get; }
        public int OpenSegmentCount => Directory.Exists(_root)
            ? Directory.GetFiles(_root, "*.open", SearchOption.AllDirectories).Length
            : 0;
        public int ResidentStreamCount => _records.GetSnapshot().ResidentStreams;

        public static TestHost Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "sharpclaw-host-job-controller",
                Guid.NewGuid().ToString("N"));
            var rootKey = Enumerable.Repeat((byte)0x3c, 32).ToArray();
            var options = new DurableStorageOptions
            {
                RootDirectory = root,
                EncryptionKey = DurableStorageKeyDerivation.Derive(
                    rootKey,
                    "records"),
                SegmentMaxBytes = 64 * 1024,
            };
            var records = new DurableSegmentStore(options);
            var paths = new DurableStreamPathEncoder(root);
            var artifactStore = new ExecutionArtifactStore(
                root,
                DurableStorageKeyDerivation.Derive(rootKey, "artifacts"));
            var diagnostics = new ExecutionDiagnosticStore(
                records,
                new DurableCursorCodec(
                    DurableStorageKeyDerivation.Derive(rootKey, "cursors"),
                    paths),
                artifactStore);
            var saveProbe = new SaveProbe();
            var dbOptions = new DbContextOptionsBuilder<SharpClawDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString(),
                    new InMemoryDatabaseRoot())
                .AddInterceptors(saveProbe)
                .Options;
            var db = new SharpClawDbContext(dbOptions);
            var persistence = new DurableExecutionPersistence(
                db,
                diagnostics,
                artifactStore);
            return new TestHost(
                root,
                records,
                db,
                saveProbe,
                diagnostics,
                persistence);
        }

        public async Task<AgentJobDB> AddJobAsync(
            AgentJobStatus status,
            string? actionKey = "module.action")
        {
            var job = new AgentJobDB
            {
                Id = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                AgentId = Guid.NewGuid(),
                Status = status,
                ActionKey = actionKey,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            Db.AgentJobs.Add(job);
            await Db.SaveChangesAsync();
            SaveProbe.Reset();
            return job;
        }

        public async Task<IReadOnlyList<SharpClaw.Contracts.DTOs.Diagnostics.DurableLogRecordResponse>>
            ReadLogsAsync(Guid jobId)
        {
            var page = await Diagnostics.ReadJobLogsAsync(
                jobId,
                cursor: null,
                query: new DurableLogQuery(Take: 100, MaxBytes: 262_144));
            return page.Records;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _records.DisposeAsync();
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public void Reset() => SaveCount = 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }
}
