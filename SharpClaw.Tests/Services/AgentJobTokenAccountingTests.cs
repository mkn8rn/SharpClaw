using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Runtime.BLL.Modules;
using SharpClaw.Runtime.BLL.Services;
using SharpClaw.Contracts.Entities.Core.Jobs;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;
using SharpClaw.Runtime.INF.Persistence;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Shared.DurableStorage;
using SharpClaw.Core.Jobs;
using SharpClaw.Core.Modules;
using SharpClaw.Core.Permissions;
using SharpClaw.Core.Resources;

namespace SharpClaw.Tests.Services;

public sealed class AgentJobTokenAccountingTests
{
    [Test]
    public async Task WhenCurrentExecutionRecordsTokensThenJobResponseIncludesJobCost()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var provider = CreateHostProvider(databaseName, databaseRoot);
        var job = MakeJob();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
        db.AgentJobs.Add(job);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<AgentJobService>();

        using (AgentJobService.BeginExecutionScope(job.Id))
        {
            await service.RecordTokensForCurrentExecutionAsync(17, 5);
        }

        var response = await service.GetAsync(job.Id);

        response.Should().NotBeNull();
        response!.JobCost.Should().NotBeNull();
        response.JobCost!.TotalPromptTokens.Should().Be(17);
        response.JobCost.TotalCompletionTokens.Should().Be(5);
        response.JobCost.TotalTokens.Should().Be(22);
    }

    [Test]
    public async Task WhenRecordingTokensForMultipleJobsThenInputOrderGetsTheRemainder()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var provider = CreateHostProvider(databaseName, databaseRoot);
        var first = MakeJob();
        var second = MakeJob();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
        db.AgentJobs.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<AgentJobService>();

        await service.RecordTokensAsync([second.Id, first.Id], promptTokens: 5, completionTokens: 3);

        var firstResponse = await service.GetAsync(first.Id);
        var secondResponse = await service.GetAsync(second.Id);

        secondResponse!.JobCost!.TotalPromptTokens.Should().Be(3);
        secondResponse.JobCost.TotalCompletionTokens.Should().Be(2);
        firstResponse!.JobCost!.TotalPromptTokens.Should().Be(2);
        firstResponse.JobCost.TotalCompletionTokens.Should().Be(1);
    }

    [Test]
    public async Task WhenModuleCostTrackerRecordsTokensThenJobResponseIncludesJobCost()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using var provider = CreateHostProvider(databaseName, root);

        var job = MakeJob();
        using (var setupScope = provider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
            db.AgentJobs.Add(job);
            await db.SaveChangesAsync();
        }

        var tracker = provider.GetRequiredService<IAgentJobCostTracker>();
        await tracker.RecordTokensAsync(job.Id, 23, 9);

        using var verifyScope = provider.CreateScope();
        var service = verifyScope.ServiceProvider.GetRequiredService<AgentJobService>();
        var response = await service.GetAsync(job.Id);

        response!.JobCost.Should().NotBeNull();
        response.JobCost!.TotalPromptTokens.Should().Be(23);
        response.JobCost.TotalCompletionTokens.Should().Be(9);
        response.JobCost.TotalTokens.Should().Be(32);
    }

    [Test]
    public void WhenExecutionScopesAreNestedThenPreviousCurrentJobIsRestored()
    {
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (AgentJobService.BeginExecutionScope(outer))
        {
            AgentJobService.CurrentExecutionJobId.Should().Be(outer);

            using (AgentJobService.BeginExecutionScope(inner))
            {
                AgentJobService.CurrentExecutionJobId.Should().Be(inner);
            }

            AgentJobService.CurrentExecutionJobId.Should().Be(outer);
        }

        AgentJobService.CurrentExecutionJobId.Should().BeNull();
    }

    private static ServiceProvider CreateHostProvider(
        string databaseName,
        InMemoryDatabaseRoot root)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<SharpClawDbContext>(
            options => options.UseInMemoryDatabase(databaseName, root));
        services.AddSingleton<TemporaryDurableRoot>();
        services.AddSingleton(sp => new DurableStorageOptions
        {
            RootDirectory = sp.GetRequiredService<TemporaryDurableRoot>().Path,
            EncryptionKey = DurableStorageKeyDerivation.Derive(
                TemporaryDurableRoot.RootKey,
                "records"),
            SegmentMaxBytes = 64 * 1024,
        });
        services.AddSingleton<DurableSegmentStore>();
        services.AddSingleton(sp => new DurableStreamPathEncoder(
            sp.GetRequiredService<TemporaryDurableRoot>().Path));
        services.AddSingleton(sp => new DurableCursorCodec(
            DurableStorageKeyDerivation.Derive(
                TemporaryDurableRoot.RootKey,
                "cursors"),
            sp.GetRequiredService<DurableStreamPathEncoder>()));
        services.AddSingleton(sp => new DatabaseCursorCodec(
            DurableStorageKeyDerivation.Derive(
                TemporaryDurableRoot.RootKey,
                "database-cursors")));
        services.AddSingleton(sp => new ExecutionArtifactStore(
            sp.GetRequiredService<TemporaryDurableRoot>().Path,
            DurableStorageKeyDerivation.Derive(
                TemporaryDurableRoot.RootKey,
                "artifacts")));
        services.AddSingleton<IExecutionArtifactStore>(sp =>
            sp.GetRequiredService<ExecutionArtifactStore>());
        services.AddSingleton<ExecutionDiagnosticStore>();
        services.AddScoped<IPersistenceEntityResolver, EfPersistenceEntityResolver>();
        services.AddSingleton<ModuleRegistry>();
        services.AddSingleton<ModuleToolExecutionPlanner>();
        services.AddSingleton<ModuleToolPermissionPlanner>();
        services.AddSingleton<ModuleToolPermissionExecutor>();
        services.AddSingleton<ModuleMetricsCollector>();
        services.AddSingleton<ModuleJobToolExecutor>();
        services.AddSingleton<ChatCache>();
        services.AddSingleton<PermissionEvaluationEngine>();
        services.AddSingleton<PermissionDelegateEvaluationEngine>();
        services.AddSingleton<AgentActionWorkflowEngine>();
        services.AddSingleton<AgentJobLifecycleEngine>();
        services.AddSingleton<AgentJobAdministrationEngine>();
        services.AddSingleton<AgentJobAdministrationWorkflowEngine>();
        services.AddSingleton<AgentJobRuntimeEngine>();
        services.AddSingleton<DefaultResourceEngine>();
        services.AddSingleton<AgentJobDefaultResourceResolver>();
        services.AddSingleton<ModuleEventDispatcher>(sp =>
            new ModuleEventDispatcher(
                sp,
                sp.GetRequiredService<IConfiguration>(),
                NullLogger<ModuleEventDispatcher>.Instance));
        services.AddScoped<AgentActionService>();
        services.AddScoped<SessionService>();
        services.AddScoped<DurableExecutionPersistence>();
        services.AddScoped<ExecutionQueryService>();
        services.AddScoped<EfAgentJobAdministrationHost>();
        services.AddScoped<EfAgentActionHost>();
        services.AddScoped<AgentJobService>();
        services.AddSingleton<IAgentJobCostTracker, HostAgentJobCostTracker>();

        return services.BuildServiceProvider();
    }

    private static AgentJobDB MakeJob() => new()
    {
        Id = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        AgentId = Guid.NewGuid(),
        Status = AgentJobStatus.Completed,
        ActionKey = "test_action",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private sealed class TemporaryDurableRoot : IDisposable
    {
        public static byte[] RootKey { get; } =
            Enumerable.Repeat((byte)0x27, 32).ToArray();

        public TemporaryDurableRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sharpclaw-token-accounting",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
