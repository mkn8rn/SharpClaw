using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Contributes the scripting-language and runtime-control step descriptors
/// owned by the task scripting module to the central task step registry.
/// </summary>
public sealed class TaskScriptingStepDescriptorProvider : ITaskOperationDescriptorProvider
{
    public string ModuleId => "sharpclaw_agent_orchestration";

    public IReadOnlyList<TaskOperationDescriptor> Descriptors { get; } = Build();

    private static TaskOperationDescriptor[] Build()
    {
        var owner = "sharpclaw_agent_orchestration";
        return
        [
            // ── Statement constructs (registered by key only) ────────────
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.DeclareVariable, OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.Assign,          OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.EventHandler,    OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.Conditional,     OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.Loop,            OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.Return,          OwnerId = owner },
            new TaskOperationDescriptor { OperationKey = TaskScriptingStepKeys.Evaluate,        OwnerId = owner },

            // ── Runtime control ────────────────────────────────────────
            new TaskOperationDescriptor
            {
                MethodName           = "Delay",
                OperationKey         = TaskScriptingStepKeys.Delay,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "WaitUntilStopped",
                OperationKey         = TaskScriptingStepKeys.WaitUntilStopped,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
            new TaskOperationDescriptor
            {
                MethodName           = "Log",
                OperationKey         = TaskScriptingStepKeys.Log,
                OwnerId              = owner,
                FirstArgIsExpression = true,
            },
        ];
    }
}
