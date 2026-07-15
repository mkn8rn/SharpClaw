using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;
using SharpClaw.Core.Jobs;
using SharpClaw.Core.Modules;

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
        var channelId = Guid.NewGuid();
        var channel = new AgentJobChannelContext(
            channelId,
            defaultAgentId,
            ContextAgentId: null,
            AllowedAgentIds: new HashSet<Guid> { requestedAgentId },
            PermissionSetId: null,
            ContextPermissionSetId: null);

        var resolved = _engine.ResolveSubmissionAgent(
            channel,
            channelId,
            requestedAgentId);

        resolved.Should().Be(requestedAgentId);
    }

    [Test]
    public void ResolveSubmissionAgent_WhenChannelHasNoAgent_UsesContextAgent()
    {
        var contextAgentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var channel = new AgentJobChannelContext(
            channelId,
            AgentId: null,
            ContextAgentId: contextAgentId,
            AllowedAgentIds: new HashSet<Guid>(),
            PermissionSetId: null,
            ContextPermissionSetId: null);

        var resolved = _engine.ResolveSubmissionAgent(
            channel,
            channelId,
            requestedAgentId: null);

        resolved.Should().Be(contextAgentId);
    }

    [Test]
    public void ResolveSubmissionAgent_WhenRequestedAgentIsNotAllowed_Throws()
    {
        var defaultAgentId = Guid.NewGuid();
        var requestedAgentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var channel = new AgentJobChannelContext(
            channelId,
            defaultAgentId,
            ContextAgentId: null,
            AllowedAgentIds: new HashSet<Guid>(),
            PermissionSetId: null,
            ContextPermissionSetId: null);

        var act = () => _engine.ResolveSubmissionAgent(
            channel,
            channelId,
            requestedAgentId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Agent {requestedAgentId} is not allowed on channel {channelId}*");
    }

    [Test]
    public void CreateSubmissionState_MapsRequestAndCreatesIdentityBeforePersistence()
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

        var job = _engine.CreateSubmissionState(
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
        job.Id.Should().NotBeEmpty();
        job.CreatedAt.Should().NotBe(default);
    }

    [Test]
    public void ApplyLifecycleState_UpdatesCompactStateAndLeavesPayloadOnDecision()
    {
        var started = DateTimeOffset.Parse("2026-06-28T18:00:00Z");
        var job = new AgentJobState();
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

        _engine.ApplyLifecycleState(job, decision);

        job.Status.Should().Be(AgentJobStatus.Executing);
        job.StartedAt.Should().Be(started);
        decision.Logs.Select(log => log.Message)
            .Should().Equal("started", "careful");
    }

    [Test]
    public void ToResponse_IncludesTransientOutcomeAndTokenUsageWithoutStoredLogs()
    {
        var job = new AgentJobState
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
        var response = _engine.ToResponse(
            job,
            new AgentJobExecutionOutcome(
                ResultData: "result",
                ErrorCode: null,
                ErrorMessage: null));

        response.JobCost.Should().NotBeNull();
        response.JobCost!.TotalPromptTokens.Should().Be(3);
        response.JobCost.TotalCompletionTokens.Should().Be(4);
        response.JobCost.TotalTokens.Should().Be(7);
        response.ResultData.Should().Be("result");
    }

    [Test]
    public void ApplyTokenUsage_SplitsRemaindersOntoFirstJob()
    {
        var first = new AgentJobState();
        var second = new AgentJobState();

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
            [new AgentJobState()],
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

    [Test]
    public void EvaluateChannelPreauthorization_WhenClearanceIsAgentOnly_IsNotApplicable()
    {
        var decision = _engine.EvaluateChannelPreauthorization(
            PermissionClearance.ApprovedByPermittedAgent,
            callerHasGrant: true,
            channelHasGrant: true,
            contextHasGrant: true);

        decision.IsPreauthorized.Should().BeFalse();
        decision.Source.Should().Be(AgentJobChannelPreauthorizationSource.NotApplicable);
        decision.RequiresCallerGrant.Should().BeFalse();
    }

    [Test]
    public void EvaluateChannelPreauthorization_WhenSameLevelUserCallerLacksGrant_RejectsBeforeChannel()
    {
        var decision = _engine.EvaluateChannelPreauthorization(
            PermissionClearance.ApprovedBySameLevelUser,
            callerHasGrant: false,
            channelHasGrant: true,
            contextHasGrant: true);

        decision.IsPreauthorized.Should().BeFalse();
        decision.Source.Should().Be(AgentJobChannelPreauthorizationSource.CallerGrantMissing);
        decision.RequiresCallerGrant.Should().BeTrue();
    }

    [Test]
    public void EvaluateChannelPreauthorization_WhenChannelMatches_UsesChannelBeforeContext()
    {
        var decision = _engine.EvaluateChannelPreauthorization(
            PermissionClearance.ApprovedBySameLevelUser,
            callerHasGrant: true,
            channelHasGrant: true,
            contextHasGrant: true);

        decision.IsPreauthorized.Should().BeTrue();
        decision.Source.Should().Be(AgentJobChannelPreauthorizationSource.Channel);
        decision.RequiresCallerGrant.Should().BeTrue();
    }

    [Test]
    public void EvaluateChannelPreauthorization_WhenOnlyContextMatches_UsesContext()
    {
        var decision = _engine.EvaluateChannelPreauthorization(
            PermissionClearance.ApprovedByWhitelistedUser,
            callerHasGrant: false,
            channelHasGrant: false,
            contextHasGrant: true);

        decision.IsPreauthorized.Should().BeTrue();
        decision.Source.Should().Be(AgentJobChannelPreauthorizationSource.Context);
        decision.RequiresCallerGrant.Should().BeFalse();
    }

    [Test]
    public void EvaluateChannelPreauthorization_WhenNoChannelOrContextGrant_ReturnsNoGrant()
    {
        var decision = _engine.EvaluateChannelPreauthorization(
            PermissionClearance.ApprovedByWhitelistedAgent,
            callerHasGrant: false,
            channelHasGrant: false,
            contextHasGrant: false);

        decision.IsPreauthorized.Should().BeFalse();
        decision.Source.Should().Be(AgentJobChannelPreauthorizationSource.NoGrant);
        decision.RequiresCallerGrant.Should().BeFalse();
    }

    [Test]
    public void IsPerResourceAction_WhenRegisteredDescriptorIsPerResource_ReturnsTrue()
    {
        var registry = CreateRegistry();

        _engine.IsPerResourceAction(registry, "docs_open")
            .Should().BeTrue();
    }

    [Test]
    public void ResolveDelegateTo_WhenActionIsRegistered_ReturnsDescriptorDelegate()
    {
        var registry = CreateRegistry();

        _engine.ResolveDelegateTo(registry, "docs_open")
            .Should().Be("UseDocumentAsync");
    }

    [Test]
    public void HasMatchingGrant_WhenResourceGrantMatchesDelegate_ReturnsTrue()
    {
        var registry = CreateRegistry();
        var resourceId = Guid.NewGuid();
        var permissionSet = new PermissionSetState
        {
            ResourceAccesses =
            [
                new ResourceAccessState
                {
                    ResourceType = "documents",
                    ResourceId = resourceId,
                    Clearance = PermissionClearance.Independent
                }
            ]
        };

        _engine.HasMatchingGrant(
                registry,
                permissionSet,
                resourceId,
                "docs_open")
            .Should().BeTrue();
    }

    [Test]
    public void HasGrantByDelegateName_WhenGlobalFlagMatches_ReturnsTrue()
    {
        var registry = CreateRegistry();
        var permissionSet = new PermissionSetState
        {
            GlobalFlags =
            [
                new GlobalFlagState
                {
                    FlagKey = "CanAuditJobs",
                    Clearance = PermissionClearance.Independent
                }
            ]
        };

        _engine.HasGrantByDelegateName(
                registry,
                permissionSet,
                "AuditJobsAsync",
                resourceId: null)
            .Should().BeTrue();
    }

    private static ModuleRegistry CreateRegistry()
    {
        var registry = new ModuleRegistry();
        registry.Register(new JobRulesModule());
        return registry;
    }

    private sealed class JobRulesModule : ISharpClawCoreModule
    {
        private static readonly JsonElement EmptySchema =
            JsonDocument.Parse("{}").RootElement.Clone();

        public string Id => "job_rules";
        public string DisplayName => "Job Rules";
        public string ToolPrefix => "jobrules";
        public void ConfigureServices(IServiceCollection services) { }

        public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() =>
        [
            new(
                "docs_open",
                "Open a document.",
                EmptySchema,
                new ModuleToolPermission(
                    IsPerResource: true,
                    Check: null,
                    DelegateTo: "UseDocumentAsync"))
        ];

        public IReadOnlyList<ModuleResourceTypeDescriptor> GetResourceTypeDescriptors() =>
        [
            new(
                "documents",
                "Documents",
                "UseDocumentAsync",
                (_, _) => Task.FromResult(new List<Guid>()))
        ];

        public IReadOnlyList<ModuleGlobalFlagDescriptor> GetGlobalFlagDescriptors() =>
        [
            new(
                "CanAuditJobs",
                "Audit Jobs",
                "Audit job execution.",
                "AuditJobsAsync")
        ];

        public Task<string> ExecuteToolAsync(
            string toolName,
            JsonElement parameters,
            AgentJobContext job,
            IServiceProvider scopedServices,
            CancellationToken ct) =>
            Task.FromResult("");
    }
}
