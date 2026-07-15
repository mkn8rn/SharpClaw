using SharpClaw.Contracts.Entities.Core.Jobs;
using SharpClaw.Contracts.Entities.Core.Tasks;
using SharpClaw.Core.Jobs;
using SharpClaw.Core.Tasks.Administration;

namespace SharpClaw.Runtime.BLL.Services;

/// <summary>
/// Maps Runtime-owned persistence entities to Core-owned execution state.
/// Persistence navigation properties and provider metadata do not cross this
/// boundary.
/// </summary>
internal static class ExecutionStateMapper
{
    public static AgentJobState ToCoreState(AgentJobDB entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new AgentJobState
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            ChannelId = entity.ChannelId,
            CallerUserId = entity.CallerUserId,
            CallerAgentId = entity.CallerAgentId,
            ActionKey = entity.ActionKey,
            ResourceId = entity.ResourceId,
            ScriptJson = entity.ScriptJson,
            WorkingDirectory = entity.WorkingDirectory,
            Status = entity.Status,
            EffectiveClearance = entity.EffectiveClearance,
            PromptTokens = entity.PromptTokens,
            CompletionTokens = entity.CompletionTokens,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            ApprovedByUserId = entity.ApprovedByUserId,
            ApprovedByAgentId = entity.ApprovedByAgentId,
        };
    }

    public static AgentJobDB ToEntity(AgentJobState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = new AgentJobDB();
        Apply(state, entity);
        return entity;
    }

    public static void Apply(AgentJobState state, AgentJobDB entity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(entity);
        entity.Id = state.Id;
        entity.AgentId = state.AgentId;
        entity.ChannelId = state.ChannelId;
        entity.CallerUserId = state.CallerUserId;
        entity.CallerAgentId = state.CallerAgentId;
        entity.ActionKey = state.ActionKey;
        entity.ResourceId = state.ResourceId;
        entity.ScriptJson = state.ScriptJson;
        entity.WorkingDirectory = state.WorkingDirectory;
        entity.Status = state.Status;
        entity.EffectiveClearance = state.EffectiveClearance;
        entity.PromptTokens = state.PromptTokens;
        entity.CompletionTokens = state.CompletionTokens;
        entity.CreatedAt = state.CreatedAt;
        entity.StartedAt = state.StartedAt;
        entity.CompletedAt = state.CompletedAt;
        entity.ApprovedByUserId = state.ApprovedByUserId;
        entity.ApprovedByAgentId = state.ApprovedByAgentId;
    }

    public static TaskInstanceState ToCoreState(TaskInstanceDB entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new TaskInstanceState
        {
            Id = entity.Id,
            TaskDefinitionId = entity.TaskDefinitionId,
            Status = entity.Status,
            ParameterValuesJson = entity.ParameterValuesJson,
            ErrorMessage = entity.ErrorMessage,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            CallerUserId = entity.CallerUserId,
            CallerAgentId = entity.CallerAgentId,
            ChannelId = entity.ChannelId,
            ContextId = entity.ContextId,
        };
    }

    public static TaskInstanceDB ToEntity(TaskInstanceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = new TaskInstanceDB();
        Apply(state, entity);
        return entity;
    }

    public static void Apply(TaskInstanceState state, TaskInstanceDB entity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(entity);
        entity.Id = state.Id;
        entity.TaskDefinitionId = state.TaskDefinitionId;
        entity.Status = state.Status;
        entity.ParameterValuesJson = state.ParameterValuesJson;
        entity.ErrorMessage = state.ErrorMessage;
        entity.CreatedAt = state.CreatedAt;
        entity.StartedAt = state.StartedAt;
        entity.CompletedAt = state.CompletedAt;
        entity.CallerUserId = state.CallerUserId;
        entity.CallerAgentId = state.CallerAgentId;
        entity.ChannelId = state.ChannelId;
        entity.ContextId = state.ContextId;
    }
}
