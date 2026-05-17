using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.TestHarness;

namespace SharpClaw.Tests.TestHarness;

[TestFixture]
public sealed class TestHarnessCostTrackingExpandedTests
{
    [Test]
    public async Task JobCostIsNonNullForCompletedJobAndStableAcrossGetCalls()
    {
        await using var host = ChatHarnessHost.Create();
        host.Harness.ConfigurePermissionedJobTool(new TestHarnessToolBehavior { Result = "job-ok" });
        var seeded = await host.SeedChatAsync(
            TestHarnessConstants.ToolProviderKey,
            grantHarnessPermission: true);
        ConfigureJobToolThenFinal(
            host,
            TestHarnessConstants.JobPermissionedTool,
            """{}""",
            new TokenUsage(11, 7),
            "done");

        var response = await host.Chat.SendMessageAsync(
            seeded.Channel.Id,
            new ChatRequest("job"));

        var job = response.JobResults!.Single();
        job.JobCost.Should().NotBeNull();
        job.JobCost!.TotalTokens.Should().Be(18);

        var service = host.Services.GetRequiredService<AgentJobService>();
        var firstGet = await service.GetAsync(job.Id);
        var secondGet = await service.GetAsync(job.Id);
        firstGet!.JobCost.Should().BeEquivalentTo(job.JobCost);
        secondGet!.JobCost.Should().BeEquivalentTo(job.JobCost);
    }

    [Test]
    public async Task FailedJobCostBehaviorIsExplicit()
    {
        await using var host = ChatHarnessHost.Create();
        host.Harness.ConfigurePermissionedJobTool(new TestHarnessToolBehavior { ThrowFailure = true });
        var seeded = await host.SeedChatAsync(
            TestHarnessConstants.ToolProviderKey,
            grantHarnessPermission: true);
        ConfigureJobToolThenFinal(
            host,
            TestHarnessConstants.JobPermissionedTool,
            """{}""",
            new TokenUsage(5, 6),
            "after failed job");

        var response = await host.Chat.SendMessageAsync(
            seeded.Channel.Id,
            new ChatRequest("failed job"));

        var job = response.JobResults!.Single();
        job.Status.Should().Be(AgentJobStatus.Failed);
        job.JobCost.Should().NotBeNull();
        job.JobCost!.TotalTokens.Should().Be(11);
    }

    [Test]
    public async Task StreamingCostReportedOnlyAtEndStillAttachesToJob()
    {
        await using var host = ChatHarnessHost.Create();
        host.Harness.ConfigurePermissionedJobTool(new TestHarnessToolBehavior { Result = "job-ok" });
        var seeded = await host.SeedChatAsync(
            TestHarnessConstants.ToolProviderKey,
            grantHarnessPermission: true);
        ConfigureJobToolThenFinal(
            host,
            TestHarnessConstants.JobPermissionedTool,
            """{}""",
            new TokenUsage(8, 9),
            "stream final");

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in host.Chat.SendMessageStreamAsync(
            seeded.Channel.Id,
            new ChatRequest("streaming job"),
            (_, _) => Task.FromResult(true)))
        {
            events.Add(evt);
        }

        events.Single(e => e.Type == ChatStreamEventType.ToolCallStart)
            .Job!.JobCost.Should().BeNull();
        events.Single(e => e.Type == ChatStreamEventType.Done)
            .FinalResponse!.JobResults!.Single().JobCost!.TotalTokens.Should().Be(17);
    }

    [Test]
    public async Task MultipleAgentsInOneChannelDoNotCrossContaminateCosts()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var other = new AgentDB
        {
            Id = Guid.NewGuid(),
            Name = "Other Cost Agent",
            ModelId = seeded.Model.Id,
            Model = seeded.Model,
            RoleId = seeded.Role.Id,
            Role = seeded.Role,
            SystemPrompt = "other prompt",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        host.Db.Agents.Add(other);
        seeded.Channel.AllowedAgents.Add(other);
        await host.Db.SaveChangesAsync();

        host.Harness.ConfigureProvider(
            TestHarnessConstants.PlainProviderKey,
            new TestHarnessProviderScenario
            {
                Turns =
                [
                    new TestHarnessProviderTurn { Content = "first", Usage = new TokenUsage(2, 3) },
                    new TestHarnessProviderTurn { Content = "second", Usage = new TokenUsage(7, 11) }
                ]
            });

        var first = await host.Chat.SendMessageAsync(
            seeded.Channel.Id,
            new ChatRequest("first"));
        var second = await host.Chat.SendMessageAsync(
            seeded.Channel.Id,
            new ChatRequest("second", AgentId: other.Id));

        first.AgentCost!.AgentId.Should().Be(seeded.Agent.Id);
        first.AgentCost.TotalTokens.Should().Be(5);
        second.AgentCost!.AgentId.Should().Be(other.Id);
        second.AgentCost.TotalTokens.Should().Be(18);
        second.ChannelCost!.AgentBreakdown.Should().Contain(b =>
            b.AgentId == seeded.Agent.Id && b.TotalTokens == 5);
        second.ChannelCost.AgentBreakdown.Should().Contain(b =>
            b.AgentId == other.Id && b.TotalTokens == 18);
    }

    [Test]
    public async Task CustomModuleCostHookAddsUsageWithoutCoreKnowingModuleInternals()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.CostProviderKey);
        host.Harness.ConfigureCost(new TestHarnessCostBehavior
        {
            Result = new ProviderCostResult(
                12.34m,
                "usd",
                [new ProviderCostDailyBucket(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 12.34m)])
        });

        var result = await host.Services.GetRequiredService<ProviderCostService>()
            .GetCostAsync(seeded.Provider.Id, startDate: DateTimeOffset.UnixEpoch, endDate: DateTimeOffset.UnixEpoch.AddDays(1));

        result!.TotalCost.Should().Be(12.34m);
        result.CostApiSupported.Should().BeTrue();
        host.Harness.CostCalls.Should().ContainSingle();
    }

    private static void ConfigureJobToolThenFinal(
        ChatHarnessHost host,
        string toolName,
        string argumentsJson,
        TokenUsage toolRoundUsage,
        string finalContent)
    {
        host.Harness.ConfigureProvider(
            TestHarnessConstants.ToolProviderKey,
            new TestHarnessProviderScenario
            {
                Turns =
                [
                    new TestHarnessProviderTurn
                    {
                        ToolCalls = [new ChatToolCall("call-job", toolName, argumentsJson)],
                        Usage = toolRoundUsage
                    },
                    new TestHarnessProviderTurn
                    {
                        Content = finalContent,
                        Usage = new TokenUsage(1, 1)
                    }
                ]
            });
    }
}
