using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Contributes chat/output and entity provisioning step descriptors owned by
/// the agent orchestration module to the central task step registry.
/// </summary>
public sealed class AgentOrchestrationStepDescriptorProvider : ITaskOperationDescriptorProvider
{
    public string ModuleId => "sharpclaw_agent_orchestration";

    public IReadOnlyList<TaskOperationDescriptor> Descriptors { get; } = Build();

    private static TaskOperationDescriptor[] Build()
    {
        const string owner = "sharpclaw_agent_orchestration";
        return
        [
            // ── Agent interaction ────────────────────────────────────
            new TaskOperationDescriptor
            {
                MethodName         = "Chat",
                OperationKey       = AgentOrchestrationStepKeys.Chat,
                OwnerId            = owner,
                ExpressionArgIndex = 1,
            },
            new TaskOperationDescriptor
            {
                MethodName         = "ChatStream",
                OperationKey       = AgentOrchestrationStepKeys.ChatStream,
                OwnerId            = owner,
                ExpressionArgIndex = 1,
            },
            new TaskOperationDescriptor
            {
                MethodName         = "ChatToThread",
                OperationKey       = AgentOrchestrationStepKeys.ChatToThread,
                OwnerId            = owner,
                ExpressionArgIndex = 1,
            },

            // ── Output ──────────────────────────────────────────────
            new TaskOperationDescriptor
            {
                MethodName           = "Emit",
                OperationKey         = AgentOrchestrationStepKeys.Emit,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "ParseResponse",
                OperationKey         = AgentOrchestrationStepKeys.ParseResponse,
                OwnerId              = owner,
                CapturesGenericType  = true,
            },

            // ── Entity lookup / creation ────────────────────────────
            new TaskOperationDescriptor
            {
                MethodName           = "FindModel",
                OperationKey         = AgentOrchestrationStepKeys.FindModel,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "FindProvider",
                OperationKey         = AgentOrchestrationStepKeys.FindProvider,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "FindAgent",
                OperationKey         = AgentOrchestrationStepKeys.FindAgent,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "CreateAgent",
                OperationKey         = AgentOrchestrationStepKeys.CreateAgent,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "CreateThread",
                OperationKey         = AgentOrchestrationStepKeys.CreateThread,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },

            // ── Roles / permissions / channels ──────────────────────
            new TaskOperationDescriptor
            {
                MethodName           = "CreateRole",
                OperationKey         = AgentOrchestrationStepKeys.CreateRole,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "FindRole",
                OperationKey         = AgentOrchestrationStepKeys.FindRole,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "SetRolePermissions",
                OperationKey         = AgentOrchestrationStepKeys.SetRolePermissions,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "AssignRole",
                OperationKey         = AgentOrchestrationStepKeys.AssignRole,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "CreateChannel",
                OperationKey         = AgentOrchestrationStepKeys.CreateChannel,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "FindChannel",
                OperationKey         = AgentOrchestrationStepKeys.FindChannel,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "AddAllowedAgent",
                OperationKey         = AgentOrchestrationStepKeys.AddAllowedAgent,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
        ];
    }
}
