using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.DTOs.Diagnostics;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Core.Tasks.Administration;
using SharpClaw.Core.Tasks.Models;
using SharpClaw.Runtime.INF.DurableStorage;

namespace SharpClaw.Runtime.BLL.Services;

/// <summary>
/// Manages task script definitions and their execution instances.
/// </summary>
public sealed class TaskService(
    TaskAdministrationWorkflowEngine administration,
    EfTaskAdministrationHost administrationHost,
    ExecutionQueryService executionQueries,
    ExecutionDiagnosticStore diagnostics) : ITaskAuthoring
{
    public TaskValidationResponse ValidateDefinition(string sourceText)
        => administration.ValidateDefinition(sourceText);

    public async Task<TaskDefinitionResponse> CreateDefinitionAsync(
        CreateTaskDefinitionRequest request,
        CancellationToken ct = default)
    {
        return await administration.CreateDefinitionAsync(
            request,
            administrationHost,
            ct);
    }

    public async Task<TaskDefinitionResponse?> GetDefinitionAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.GetDefinitionAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<IReadOnlyList<TaskRequirementDefinition>?> GetRequirementsAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.GetRequirementsAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<IReadOnlyList<TaskTriggerDefinition>?> GetTriggersAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.GetTriggersAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<IReadOnlyList<TaskDefinitionResponse>> ListDefinitionsAsync(
        CancellationToken ct = default)
    {
        return await administration.ListDefinitionsAsync(
            administrationHost,
            ct);
    }

    public async Task<TaskDefinitionResponse?> UpdateDefinitionAsync(
        Guid id,
        UpdateTaskDefinitionRequest request,
        CancellationToken ct = default)
    {
        return await administration.UpdateDefinitionAsync(
            id,
            request,
            administrationHost,
            ct);
    }

    public async Task<bool> DeleteDefinitionAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.DeleteDefinitionAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<int> SetTriggersEnabledAsync(
        Guid taskDefinitionId,
        bool enabled,
        CancellationToken ct = default)
    {
        return await administration.SetTriggersEnabledAsync(
            taskDefinitionId,
            enabled,
            administrationHost,
            ct);
    }

    public async Task<TaskInstanceDetailResponse> CreateInstanceAsync(
        StartTaskInstanceRequest request,
        Guid? callerUserId = null,
        Guid? callerAgentId = null,
        CancellationToken ct = default)
    {
        var instance = await administration.CreateInstanceAsync(
            request,
            callerUserId,
            callerAgentId,
            administrationHost,
            ct);
        return await executionQueries.GetTaskAsync(instance.Id, ct)
            ?? throw new InvalidOperationException(
                $"Task instance {instance.Id} was not persisted.");
    }

    public async Task<bool> PauseInstanceAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.PauseInstanceAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<bool> ResumeInstanceAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.ResumeInstanceAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<bool> TryMarkInstanceRunningAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.TryMarkInstanceRunningAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<bool> StopInstanceAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.StopInstanceAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task<TaskInstanceDetailResponse?> GetInstanceAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await executionQueries.GetTaskAsync(id, ct);
    }

    public async Task<TaskInstanceSummaryPageResponse> ListInstancesAsync(
        Guid? taskDefinitionId = null,
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default)
    {
        return await executionQueries.ListTasksAsync(
            taskDefinitionId,
            cursor,
            take,
            ct);
    }

    public async Task<bool> CancelInstanceAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await administration.CancelInstanceAsync(
            id,
            administrationHost,
            ct);
    }

    public async Task AppendLogAsync(
        Guid instanceId,
        string message,
        string level = "Info",
        CancellationToken ct = default)
    {
        await administration.AppendLogAsync(
            instanceId,
            message,
            level,
            administrationHost,
            ct);
    }

    public async Task<TaskOutputPageResponse> GetOutputsAsync(
        Guid instanceId,
        string? cursor = null,
        int take = 100,
        int maxBytes = 262_144,
        CancellationToken ct = default)
    {
        return await diagnostics.ReadTaskOutputsAsync(
            instanceId,
            cursor,
            take,
            maxBytes,
            ct);
    }

    internal Task<bool> ApplyCompilationFailureAsync(
        Guid id,
        string errors,
        CancellationToken ct = default) =>
        administration.ApplyCompilationFailureAsync(
            id,
            errors,
            administrationHost,
            ct);

    internal Task<bool> ApplyTerminalStatusAsync(
        Guid id,
        SharpClaw.Contracts.Enums.TaskInstanceStatus status,
        CancellationToken ct = default) =>
        administration.ApplyTerminalStatusAsync(
            id,
            status,
            administrationHost,
            ct);

    internal Task<bool> ApplyFailureAsync(
        Guid id,
        string error,
        CancellationToken ct = default) =>
        administration.ApplyFailureAsync(
            id,
            error,
            administrationHost,
            ct);

    internal Task<TaskRestartRecoveryPlan?> ApplyRestartRecoveryAsync(
        Guid id,
        CancellationToken ct = default) =>
        administration.ApplyRestartRecoveryAsync(
            id,
            administrationHost,
            ct);

    public ValueTask<TaskOutputRecordResponse?> GetLatestOutputAsync(
        Guid instanceId,
        CancellationToken ct = default) =>
        diagnostics.ReadLatestTaskOutputAsync(instanceId, ct);

    public ValueTask<DurableLogPageResponse> ReadLogsAsync(
        Guid instanceId,
        string? cursor,
        DurableLogQuery query,
        CancellationToken ct = default) =>
        diagnostics.ReadTaskLogsAsync(instanceId, cursor, query, ct);

    public Task<ExecutionAuditPageResponse> ReadAuditAsync(
        Guid instanceId,
        string? cursor,
        int take = 50,
        CancellationToken ct = default) =>
        executionQueries.ReadAuditAsync(
            SharpClaw.Contracts.Enums.ExecutionOwnerKind.TaskInstance,
            instanceId,
            cursor,
            take,
            ct);
}
