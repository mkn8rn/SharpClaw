using FluentAssertions;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Entities.Core.Jobs;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;
using SharpClaw.Core.Jobs;

namespace SharpClaw.Tests.Core;

[TestFixture]
public sealed class AgentJobLifecycleEngineTests
{
    private readonly AgentJobLifecycleEngine _engine = new();

    [Test]
    public void ResolveSubmissionPermission_WhenPendingIsPreauthorized_ExecutesWithoutAwaitingApproval()
    {
        var result = AgentActionResult.Pending(
            "Needs approval.",
            PermissionClearance.ApprovedByWhitelistedUser);

        var decision = _engine.ResolveSubmissionPermission(
            result,
            channelPreauthorized: true);

        decision.ShouldExecute.Should().BeTrue();
        decision.Status.Should().BeNull();
        decision.Logs.Should().ContainSingle(log =>
            log.Message == "Pre-authorized by channel/context permission set."
            && log.Level == JobLogLevels.Info);
    }

    [Test]
    public void ResolveApproval_WhenPermissionWasRevoked_DeniesAndCompletesAtNow()
    {
        var now = DateTimeOffset.Parse("2026-06-28T17:20:00Z");
        var approverId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var result = AgentActionResult.Denied("Agent does not have permission.");

        var decision = _engine.ResolveApproval(
            result,
            new ActionCaller(UserId: approverId),
            now);

        decision.Status.Should().Be(AgentJobStatus.Denied);
        decision.UpdateCompletedAt.Should().BeTrue();
        decision.CompletedAt.Should().Be(now);
        decision.ShouldExecute.Should().BeFalse();
        decision.Logs.Should().ContainSingle(log =>
            log.Level == JobLogLevels.Warning
            && log.Message.Contains($"user {approverId}")
            && log.Message.Contains("permission revoked"));
    }

    [Test]
    public void Cancel_WhenAlreadyTerminal_OnlyEmitsWarning()
    {
        var decision = _engine.Cancel(
            AgentJobStatus.Completed,
            DateTimeOffset.UtcNow);

        decision.Status.Should().BeNull();
        decision.UpdateCompletedAt.Should().BeFalse();
        decision.Logs.Should().ContainSingle(log =>
            log.Level == JobLogLevels.Warning
            && log.Message == "Cancel rejected: job is already Completed.");
    }

    [Test]
    public void Stop_WhenActionPrefixDoesNotMatch_OnlyEmitsWarning()
    {
        var decision = _engine.Stop(
            AgentJobStatus.Executing,
            "curativa.transcribe.start",
            "curativa.audio.",
            DateTimeOffset.UtcNow);

        decision.Status.Should().BeNull();
        decision.UpdateCompletedAt.Should().BeFalse();
        decision.Logs.Should().ContainSingle(log =>
            log.Level == JobLogLevels.Warning
            && log.Message == "Stop rejected: job action does not match the requested action prefix.");
    }

    [Test]
    public void CompleteExecution_WhenModuleRemainsExecuting_ClearsCompletedAtAndStoresResult()
    {
        var decision = _engine.CompleteExecution(
            "started",
            ModuleJobCompletionBehavior.RemainExecuting,
            DateTimeOffset.UtcNow);

        decision.Status.Should().Be(AgentJobStatus.Executing);
        decision.UpdateCompletedAt.Should().BeTrue();
        decision.CompletedAt.Should().BeNull();
        decision.UpdateResultData.Should().BeTrue();
        decision.ResultData.Should().Be("started");
        decision.Logs.Should().ContainSingle(log =>
            log.Level == JobLogLevels.Info
            && log.Message.Contains("job remains Executing"));
    }

    [Test]
    public void PauseThenResume_ProducesExpectedStatusTransitions()
    {
        var pause = _engine.Pause(AgentJobStatus.Executing);
        var resume = _engine.Resume(AgentJobStatus.Paused);

        pause.Status.Should().Be(AgentJobStatus.Paused);
        pause.Logs.Should().ContainSingle(log => log.Message == "Job paused.");
        resume.Status.Should().Be(AgentJobStatus.Executing);
        resume.Logs.Should().ContainSingle(log => log.Message == "Job resumed.");
    }
}
