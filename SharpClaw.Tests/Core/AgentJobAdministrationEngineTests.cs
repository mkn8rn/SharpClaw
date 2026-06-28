using SharpClaw.Contracts;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.Entities.Core.Jobs;
using SharpClaw.Contracts.Enums;
using SharpClaw.Core.Jobs;

namespace SharpClaw.Tests.Core;

[TestFixture]
public sealed class AgentJobAdministrationEngineTests
{
    private readonly AgentJobAdministrationEngine _engine = new();

    [Test]
    public void ResolveSubmissionAgent_WhenChannelHasAllowedOverride_UsesRequestedAgent()
    {
        var defaultAgentId = Guid.NewGuid();
        var requestedAgentId = Guid.NewGuid();
        var channel = new ChannelDB
        {
            Id = Guid.NewGuid(),
            Title = "channel",
            AgentId = defaultAgentId
        };
        channel.AllowedAgents.Add(new AgentDB
        {
            Id = requestedAgentId,
            Name = "allowed"
        });

        var resolved = _engine.ResolveSubmissionAgent(
            channel,
            channel.Id,
            requestedAgentId);

        resolved.Should().Be(requestedAgentId);
    }

    [Test]
    public void ResolveSubmissionAgent_WhenChannelHasNoAgent_UsesContextAgent()
    {
        var contextAgentId = Guid.NewGuid();
        var channel = new ChannelDB
        {
            Id = Guid.NewGuid(),
            Title = "channel",
            AgentContext = new ChannelContextDB
            {
                Name = "context",
                AgentId = contextAgentId
            }
        };

        var resolved = _engine.ResolveSubmissionAgent(
            channel,
            channel.Id,
            requestedAgentId: null);

        resolved.Should().Be(contextAgentId);
    }

    [Test]
    public void ResolveSubmissionAgent_WhenRequestedAgentIsNotAllowed_Throws()
    {
        var defaultAgentId = Guid.NewGuid();
        var requestedAgentId = Guid.NewGuid();
        var channel = new ChannelDB
        {
            Id = Guid.NewGuid(),
            Title = "channel",
            AgentId = defaultAgentId
        };

        var act = () => _engine.ResolveSubmissionAgent(
            channel,
            channel.Id,
            requestedAgentId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Agent {requestedAgentId} is not allowed on channel {channel.Id}*");
    }

    [Test]
    public void CreateSubmissionJob_MapsRequestAndCaller()
    {
        var channelId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var callerAgentId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var request = new SubmitAgentJobRequest(
            ActionKey: "module.tool",
            ResourceId: null,
            AgentId: Guid.NewGuid(),
            CallerAgentId: callerAgentId,
            ScriptJson: "{}",
            WorkingDirectory: "E:\\work");

        var job = _engine.CreateSubmissionJob(
            channelId,
            agentId,
            request,
            callerUserId,
            resourceId);

        job.ChannelId.Should().Be(channelId);
        job.AgentId.Should().Be(agentId);
        job.CallerUserId.Should().Be(callerUserId);
        job.CallerAgentId.Should().Be(callerAgentId);
        job.ActionKey.Should().Be("module.tool");
        job.ResourceId.Should().Be(resourceId);
        job.ScriptJson.Should().Be("{}");
        job.WorkingDirectory.Should().Be("E:\\work");
    }

    [Test]
    public void ApplyLifecycleDecision_UpdatesJobAndAttachesLogs()
    {
        var started = DateTimeOffset.Parse("2026-06-28T18:00:00Z");
        var job = new AgentJobDB();
        var decision = new AgentJobLifecycleDecision
        {
            Status = AgentJobStatus.Executing,
            UpdateStartedAt = true,
            StartedAt = started,
            Logs =
            [
                new AgentJobLifecycleLog("started", JobLogLevels.Info),
                new AgentJobLifecycleLog("careful", JobLogLevels.Warning)
            ]
        };

        var logs = _engine.ApplyLifecycleDecision(job, decision);

        job.Status.Should().Be(AgentJobStatus.Executing);
        job.StartedAt.Should().Be(started);
        logs.Should().HaveCount(2);
        job.LogEntries.Should().BeEquivalentTo(logs);
        logs.Select(log => log.Message).Should().Equal("started", "careful");
    }

    [Test]
    public void ToResponse_IncludesOrderedLogsAndTokenUsage()
    {
        var job = new AgentJobDB
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            ActionKey = "module.tool",
            Status = AgentJobStatus.Completed,
            EffectiveClearance = PermissionClearance.Independent,
            PromptTokens = 3,
            CompletionTokens = 4,
            CreatedAt = DateTimeOffset.Parse("2026-06-28T18:00:00Z")
        };
        job.LogEntries.Add(new AgentJobLogEntryDB
        {
            Message = "second",
            CreatedAt = DateTimeOffset.Parse("2026-06-28T18:00:02Z")
        });
        job.LogEntries.Add(new AgentJobLogEntryDB
        {
            Message = "first",
            CreatedAt = DateTimeOffset.Parse("2026-06-28T18:00:01Z")
        });

        var response = _engine.ToResponse(job);

        response.JobCost.Should().NotBeNull();
        response.JobCost!.TotalPromptTokens.Should().Be(3);
        response.JobCost.TotalCompletionTokens.Should().Be(4);
        response.JobCost.TotalTokens.Should().Be(7);
        response.Logs.Select(log => log.Message).Should().Equal("first", "second");
    }

    [Test]
    public void ApplyTokenUsage_SplitsRemaindersOntoFirstJob()
    {
        var first = new AgentJobDB();
        var second = new AgentJobDB();

        _engine.ApplyTokenUsage([first, second], promptTokens: 5, completionTokens: 7);

        first.PromptTokens.Should().Be(3);
        second.PromptTokens.Should().Be(2);
        first.CompletionTokens.Should().Be(4);
        second.CompletionTokens.Should().Be(3);
    }

    [Test]
    public void ApplyTokenUsage_WhenPromptTokensAreNegative_Throws()
    {
        var act = () => _engine.ApplyTokenUsage(
            [new AgentJobDB()],
            promptTokens: -1,
            completionTokens: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("Prompt tokens cannot be negative.*");
    }

    [Test]
    public void ChannelPreauthorizationRules_MatchClearanceLevels()
    {
        _engine.CanUseChannelPreauthorization(
                PermissionClearance.ApprovedByPermittedAgent)
            .Should().BeFalse();
        _engine.CanUseChannelPreauthorization(
                PermissionClearance.ApprovedBySameLevelUser)
            .Should().BeTrue();
        _engine.RequiresCallerGrantForChannelPreauthorization(
                PermissionClearance.ApprovedBySameLevelUser)
            .Should().BeTrue();
        _engine.RequiresCallerGrantForChannelPreauthorization(
                PermissionClearance.ApprovedByWhitelistedUser)
            .Should().BeFalse();
    }
}
