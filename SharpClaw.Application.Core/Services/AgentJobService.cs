using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Enums;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

/// <summary>
/// Manages the lifecycle of agent action jobs: submission, permission
/// evaluation, optional approval, execution, and outcome tracking.
/// </summary>
public sealed class AgentJobService(
    SharpClawDbContext db,
    AgentActionService actions)
{
    // ═══════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Submit a new job.  Permission is evaluated immediately:
    /// <list type="bullet">
    ///   <item>Approved → executes inline, returns <see cref="AgentJobStatus.Completed"/>
    ///         or <see cref="AgentJobStatus.Failed"/>.</item>
    ///   <item>PendingApproval → returns <see cref="AgentJobStatus.AwaitingApproval"/>.</item>
    ///   <item>Denied → returns <see cref="AgentJobStatus.Denied"/>.</item>
    /// </list>
    /// </summary>
    public async Task<AgentJobResponse> SubmitAsync(
        Guid agentId,
        SubmitAgentJobRequest request,
        CancellationToken ct = default)
    {
        var job = new AgentJobDB
        {
            AgentId = agentId,
            CallerUserId = request.CallerUserId,
            CallerAgentId = request.CallerAgentId,
            ActionType = request.ActionType,
            ResourceId = request.ResourceId,
            Status = AgentJobStatus.Queued
        };

        db.AgentJobs.Add(job);
        AddLog(job, $"Job queued: {request.ActionType}.");
        await db.SaveChangesAsync(ct);

        var caller = new ActionCaller(request.CallerUserId, request.CallerAgentId);
        var result = await DispatchPermissionCheckAsync(
            agentId, job.ActionType, job.ResourceId, caller, ct);

        job.EffectiveClearance = result.EffectiveClearance;

        switch (result.Verdict)
        {
            case ClearanceVerdict.Approved:
                AddLog(job, $"Permission granted: {result.Reason}");
                await ExecuteJobAsync(job, ct);
                break;

            case ClearanceVerdict.PendingApproval:
                job.Status = AgentJobStatus.AwaitingApproval;
                AddLog(job, $"Awaiting approval: {result.Reason}");
                await db.SaveChangesAsync(ct);
                break;

            case ClearanceVerdict.Denied:
            default:
                job.Status = AgentJobStatus.Denied;
                AddLog(job, $"Denied: {result.Reason}", "Warning");
                await db.SaveChangesAsync(ct);
                break;
        }

        return ToResponse(job);
    }

    /// <summary>
    /// Approve a job that is <see cref="AgentJobStatus.AwaitingApproval"/>.
    /// The approver's identity is evaluated against the clearance requirement.
    /// </summary>
    public async Task<AgentJobResponse?> ApproveAsync(
        Guid jobId,
        ApproveAgentJobRequest request,
        CancellationToken ct = default)
    {
        var job = await LoadJobAsync(jobId, ct);
        if (job is null) return null;

        if (job.Status != AgentJobStatus.AwaitingApproval)
        {
            AddLog(job, $"Approve rejected: job is {job.Status}, not AwaitingApproval.", "Warning");
            await db.SaveChangesAsync(ct);
            return ToResponse(job);
        }

        var approver = new ActionCaller(request.ApproverUserId, request.ApproverAgentId);
        var result = await DispatchPermissionCheckAsync(
            job.AgentId, job.ActionType, job.ResourceId, approver, ct);

        switch (result.Verdict)
        {
            case ClearanceVerdict.Approved:
                job.ApprovedByUserId = request.ApproverUserId;
                job.ApprovedByAgentId = request.ApproverAgentId;
                AddLog(job, $"Approved by {FormatCaller(approver)}: {result.Reason}");
                await ExecuteJobAsync(job, ct);
                break;

            case ClearanceVerdict.PendingApproval:
                AddLog(job, $"Approval attempt by {FormatCaller(approver)} insufficient: {result.Reason}", "Warning");
                await db.SaveChangesAsync(ct);
                break;

            case ClearanceVerdict.Denied:
            default:
                AddLog(job, $"Approval attempt by {FormatCaller(approver)} denied: {result.Reason}", "Warning");
                await db.SaveChangesAsync(ct);
                break;
        }

        return ToResponse(job);
    }

    /// <summary>Cancel a job that has not yet completed.</summary>
    public async Task<AgentJobResponse?> CancelAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var job = await LoadJobAsync(jobId, ct);
        if (job is null) return null;

        if (job.Status is AgentJobStatus.Completed or AgentJobStatus.Failed
                       or AgentJobStatus.Denied or AgentJobStatus.Cancelled)
        {
            AddLog(job, $"Cancel rejected: job is already {job.Status}.", "Warning");
            await db.SaveChangesAsync(ct);
            return ToResponse(job);
        }

        job.Status = AgentJobStatus.Cancelled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        AddLog(job, "Job cancelled.");
        await db.SaveChangesAsync(ct);

        return ToResponse(job);
    }

    /// <summary>Retrieve a single job by ID.</summary>
    public async Task<AgentJobResponse?> GetAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var job = await LoadJobAsync(jobId, ct);
        return job is null ? null : ToResponse(job);
    }

    /// <summary>List all jobs for an agent, most recent first.</summary>
    public async Task<IReadOnlyList<AgentJobResponse>> ListAsync(
        Guid agentId, CancellationToken ct = default)
    {
        var jobs = await db.AgentJobs
            .Include(j => j.LogEntries)
            .Where(j => j.AgentId == agentId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

        return jobs.Select(ToResponse).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // Execution
    // ═══════════════════════════════════════════════════════════════

    private async Task ExecuteJobAsync(AgentJobDB job, CancellationToken ct)
    {
        job.Status = AgentJobStatus.Executing;
        job.StartedAt = DateTimeOffset.UtcNow;
        AddLog(job, "Execution started.");
        await db.SaveChangesAsync(ct);

        try
        {
            // Dispatch to the actual action implementation.
            // For now, each action type logs and completes successfully.
            // Real implementations will be plugged in per action type.
            var resultData = await DispatchExecutionAsync(job, ct);

            job.Status = AgentJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ResultData = resultData;
            AddLog(job, "Job completed successfully.");
        }
        catch (Exception ex)
        {
            job.Status = AgentJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorLog = ex.ToString();
            AddLog(job, $"Job failed: {ex.Message}", "Error");
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Placeholder execution dispatcher.  Returns optional result data.
    /// Each action type will be wired to real logic as it's implemented.
    /// </summary>
    private static Task<string?> DispatchExecutionAsync(AgentJobDB job, CancellationToken ct)
    {
        // TODO: wire each AgentActionType to its concrete implementation.
        _ = ct;
        return Task.FromResult<string?>(
            $"Action '{job.ActionType}' executed successfully (resource: {job.ResourceId?.ToString() ?? "n/a"}).");
    }

    // ═══════════════════════════════════════════════════════════════
    // Permission dispatch
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Routes to the correct <see cref="AgentActionService"/> method
    /// based on <paramref name="actionType"/>.
    /// </summary>
    private Task<AgentActionResult> DispatchPermissionCheckAsync(
        Guid agentId,
        AgentActionType actionType,
        Guid? resourceId,
        ActionCaller caller,
        CancellationToken ct)
    {
        return actionType switch
        {
            // Global flags (no resource ID)
            AgentActionType.ExecuteAsAdmin
                => actions.ExecuteAsAdminAsync(agentId, caller, ct: ct),
            AgentActionType.CreateSubAgent
                => actions.CreateSubAgentAsync(agentId, caller, ct: ct),
            AgentActionType.CreateContainer
                => actions.CreateContainerAsync(agentId, caller, ct: ct),
            AgentActionType.RegisterInfoStore
                => actions.RegisterInfoStoreAsync(agentId, caller, ct: ct),
            AgentActionType.EditAnyTask
                => actions.EditAnyTaskAsync(agentId, caller, ct: ct),

            // Per-resource grants
            AgentActionType.ExecuteAsSystemUser when resourceId.HasValue
                => actions.ExecuteAsSystemUserAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.AccessLocalInfoStore when resourceId.HasValue
                => actions.AccessLocalInfoStoreAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.AccessExternalInfoStore when resourceId.HasValue
                => actions.AccessExternalInfoStoreAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.AccessWebsite when resourceId.HasValue
                => actions.AccessWebsiteAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.QuerySearchEngine when resourceId.HasValue
                => actions.QuerySearchEngineAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.AccessContainer when resourceId.HasValue
                => actions.AccessContainerAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.ManageAgent when resourceId.HasValue
                => actions.ManageAgentAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.EditTask when resourceId.HasValue
                => actions.EditTaskAsync(agentId, resourceId.Value, caller, ct: ct),
            AgentActionType.AccessSkill when resourceId.HasValue
                => actions.AccessSkillAsync(agentId, resourceId.Value, caller, ct: ct),

            // Missing resource ID for per-resource action
            _ when IsPerResourceAction(actionType) && !resourceId.HasValue
                => Task.FromResult(
                    AgentActionResult.Denied($"ResourceId is required for {actionType}.")),

            _ => Task.FromResult(
                    AgentActionResult.Denied($"Unknown action type: {actionType}."))
        };
    }

    private static bool IsPerResourceAction(AgentActionType type) =>
        type is AgentActionType.ExecuteAsSystemUser
            or AgentActionType.AccessLocalInfoStore
            or AgentActionType.AccessExternalInfoStore
            or AgentActionType.AccessWebsite
            or AgentActionType.QuerySearchEngine
            or AgentActionType.AccessContainer
            or AgentActionType.ManageAgent
            or AgentActionType.EditTask
            or AgentActionType.AccessSkill;

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<AgentJobDB?> LoadJobAsync(Guid jobId, CancellationToken ct) =>
        await db.AgentJobs
            .Include(j => j.LogEntries)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

    private static void AddLog(AgentJobDB job, string message, string level = "Info")
    {
        job.LogEntries.Add(new AgentJobLogEntryDB
        {
            AgentJobId = job.Id,
            Message = message,
            Level = level
        });
    }

    private static string FormatCaller(ActionCaller caller) =>
        caller.UserId is not null ? $"user {caller.UserId}"
        : caller.AgentId is not null ? $"agent {caller.AgentId}"
        : "unknown";

    private static AgentJobResponse ToResponse(AgentJobDB job) =>
        new(
            Id: job.Id,
            AgentId: job.AgentId,
            ActionType: job.ActionType,
            ResourceId: job.ResourceId,
            Status: job.Status,
            EffectiveClearance: job.EffectiveClearance,
            ResultData: job.ResultData,
            ErrorLog: job.ErrorLog,
            Logs: job.LogEntries
                .OrderBy(l => l.CreatedAt)
                .Select(l => new AgentJobLogResponse(l.Message, l.Level, l.CreatedAt))
                .ToList(),
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt);
}
