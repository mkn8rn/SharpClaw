using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SharpClaw.Contracts.Modules;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Modules.AgentOrchestration.Services;

namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Default module: agent lifecycle (create sub-agent, manage agent),
/// task editing, and skill access. All tools flow through the job pipeline.
/// </summary>
public sealed class AgentOrchestrationModule : ISharpClawModule
{
    public string Id => "sharpclaw_agent_orchestration";
    public string DisplayName => "Agent Orchestration";
    public string ToolPrefix => "ao";

    // ═══════════════════════════════════════════════════════════════
    // DI Registration
    // ═══════════════════════════════════════════════════════════════

    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<AgentOrchestrationService>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Contracts
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleContractExport> ExportedContracts => [];

    // ═══════════════════════════════════════════════════════════════
    // Resource Type Descriptors
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleResourceTypeDescriptor> GetResourceTypeDescriptors() =>
    [
        new("AoAgent", "ManageAgent", "ManageAgentAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SharpClawDbContext>();
            return await db.Agents.Select(a => a.Id).ToListAsync(ct);
        }),
        new("AoTask", "EditTask", "EditTaskAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SharpClawDbContext>();
            return await db.ScheduledTasks.Select(t => t.Id).ToListAsync(ct);
        }),
        new("AoSkill", "AccessSkill", "AccessSkillAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SharpClawDbContext>();
            return await db.Skills.Select(s => s.Id).ToListAsync(ct);
        }),
        new("AoAgentHeader", "EditAgentHeader", "EditAgentHeaderAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SharpClawDbContext>();
            return await db.Agents.Select(a => a.Id).ToListAsync(ct);
        }),
        new("AoChannelHeader", "EditChannelHeader", "EditChannelHeaderAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SharpClawDbContext>();
            return await db.Channels.Select(c => c.Id).ToListAsync(ct);
        }),
    ];

    // ═══════════════════════════════════════════════════════════════
    // Global Flag Descriptors
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleGlobalFlagDescriptor> GetGlobalFlagDescriptors() =>
    [
        new("CanCreateSubAgents", "Create Sub-Agents", "Create sub-agents with permissions ≤ the creator's.", "CreateSubAgentAsync"),
        new("CanEditAgentHeader", "Edit Agent Header", "Edit the custom chat header of specific agents.", "CanEditAgentHeaderAsync"),
        new("CanEditChannelHeader", "Edit Channel Header", "Edit the custom chat header of specific channels.", "CanEditChannelHeaderAsync"),
    ];

    // ═══════════════════════════════════════════════════════════════
    // Tool Definitions
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions()
    {
        var globalNoResource = new ModuleToolPermission(
            IsPerResource: false, Check: null,
            DelegateTo: "CreateSubAgentAsync");

        var perResourceManageAgent = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "ManageAgentAsync");

        var perResourceEditTask = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "EditTaskAsync");

        var perResourceAccessSkill = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "AccessSkillAsync");

        var perResourceEditAgentHeader = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "EditAgentHeaderAsync");

        var perResourceEditChannelHeader = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "EditChannelHeaderAsync");

        return
        [
            new("create_sub_agent",
                "Create a sub-agent (name, modelId, optional systemPrompt).",
                BuildCreateSubAgentSchema(), globalNoResource),

            new("ao_manage_agent",
                "Update agent name, systemPrompt, or modelId.",
                BuildManageAgentSchema(), perResourceManageAgent,
                Aliases: ["manage_agent"]),

            new("ao_edit_task",
                "Edit task name, interval, or retries.",
                BuildEditTaskSchema(), perResourceEditTask,
                Aliases: ["edit_task"]),

            new("ao_access_skill",
                "Retrieve a skill's instruction text.",
                BuildResourceOnlySchema(), perResourceAccessSkill,
                Aliases: ["access_skill"]),

            new("ao_edit_agent_header",
                "Set or clear the custom chat header for an agent.",
                BuildHeaderSchema(), perResourceEditAgentHeader,
                Aliases: ["edit_agent_header"]),

            new("ao_edit_channel_header",
                "Set or clear the custom chat header for a channel.",
                BuildHeaderSchema(), perResourceEditChannelHeader,
                Aliases: ["edit_channel_header"]),
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool Execution
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ExecuteToolAsync(
        string toolName, JsonElement parameters, AgentJobContext job,
        IServiceProvider sp, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<AgentOrchestrationService>();

        return toolName switch
        {
            "create_sub_agent"
                => await svc.CreateSubAgentAsync(parameters, ct),

            "ao_manage_agent" or "manage_agent"
                => await svc.ManageAgentAsync(
                    job.ResourceId ?? throw new InvalidOperationException(
                        "manage_agent requires a ResourceId (target agent)."),
                    parameters, ct),

            "ao_edit_task" or "edit_task"
                => await svc.EditTaskAsync(
                    job.ResourceId ?? throw new InvalidOperationException(
                        "edit_task requires a ResourceId (target task)."),
                    parameters, ct),

            "ao_access_skill" or "access_skill"
                => await svc.AccessSkillAsync(
                    job.ResourceId ?? throw new InvalidOperationException(
                        "access_skill requires a ResourceId (target skill)."),
                    ct),

            "ao_edit_agent_header" or "edit_agent_header"
                => await svc.EditAgentHeaderAsync(
                    job.ResourceId ?? throw new InvalidOperationException(
                        "edit_agent_header requires a ResourceId (target agent)."),
                    parameters, ct),

            "ao_edit_channel_header" or "edit_channel_header"
                => await svc.EditChannelHeaderAsync(
                    job.ResourceId ?? throw new InvalidOperationException(
                        "edit_channel_header requires a ResourceId (target channel)."),
                    parameters, ct),

            _ => throw new InvalidOperationException(
                $"Unknown Agent Orchestration tool: '{toolName}'."),
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Lifecycle
    // ═══════════════════════════════════════════════════════════════

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════════
    // Schema builders
    // ═══════════════════════════════════════════════════════════════

    private static JsonElement BuildCreateSubAgentSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Agent name."
                    },
                    "modelId": {
                        "type": "string",
                        "description": "Model GUID."
                    },
                    "systemPrompt": {
                        "type": "string",
                        "description": "System prompt."
                    }
                },
                "required": ["name", "modelId"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildManageAgentSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resource_id": {
                        "type": "string",
                        "description": "Agent GUID."
                    },
                    "name": {
                        "type": "string",
                        "description": "New name."
                    },
                    "systemPrompt": {
                        "type": "string",
                        "description": "New system prompt."
                    },
                    "modelId": {
                        "type": "string",
                        "description": "New model GUID."
                    }
                },
                "required": ["resource_id"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildEditTaskSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resource_id": {
                        "type": "string",
                        "description": "Task GUID."
                    },
                    "name": {
                        "type": "string",
                        "description": "New name."
                    },
                    "repeatIntervalMinutes": {
                        "type": "integer",
                        "description": "Minutes. 0=remove."
                    },
                    "maxRetries": {
                        "type": "integer",
                        "description": "Max retries."
                    }
                },
                "required": ["resource_id"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildResourceOnlySchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resource_id": {
                        "type": "string",
                        "description": "Resource GUID."
                    }
                },
                "required": ["resource_id"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildHeaderSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resource_id": {
                        "type": "string",
                        "description": "Target agent or channel GUID."
                    },
                    "header": {
                        "type": "string",
                        "description": "Header template text. Empty or null clears the custom header."
                    }
                },
                "required": ["resource_id"]
            }
            """);
        return doc.RootElement.Clone();
    }
}
