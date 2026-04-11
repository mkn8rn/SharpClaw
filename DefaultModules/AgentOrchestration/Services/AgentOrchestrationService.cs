using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SharpClaw.Infrastructure.Models;
using SharpClaw.Application.Infrastructure.Models.Context;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Modules.AgentOrchestration.Services;

/// <summary>
/// Wraps agent lifecycle, task editing, and skill access DB operations
/// for the Agent Orchestration module.
/// </summary>
internal sealed class AgentOrchestrationService(SharpClawDbContext db)
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ═══════════════════════════════════════════════════════════════
    // CREATE SUB-AGENT
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> CreateSubAgentAsync(
        JsonElement parameters, CancellationToken ct)
    {
        var name = parameters.TryGetProperty("name", out var nProp)
            ? nProp.GetString() : null;
        var modelIdStr = parameters.TryGetProperty("modelId", out var mProp)
            ? mProp.GetString() : null;
        var systemPrompt = parameters.TryGetProperty("systemPrompt", out var sProp)
            ? sProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                "create_sub_agent requires a 'name' parameter.");

        if (!Guid.TryParse(modelIdStr, out var modelId))
            throw new InvalidOperationException(
                "create_sub_agent requires a valid 'modelId' GUID.");

        var model = await db.Models
            .Include(m => m.Provider)
            .FirstOrDefaultAsync(m => m.Id == modelId, ct)
            ?? throw new InvalidOperationException(
                $"Model {modelId} not found.");

        var agent = new AgentDB
        {
            Name = name,
            SystemPrompt = systemPrompt,
            ModelId = model.Id,
        };

        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);

        return $"Created sub-agent '{agent.Name}' (id={agent.Id}, model={model.Name}).";
    }

    // ═══════════════════════════════════════════════════════════════
    // MANAGE AGENT
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ManageAgentAsync(
        Guid resourceId, JsonElement parameters, CancellationToken ct)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Id == resourceId, ct)
            ?? throw new InvalidOperationException(
                $"Agent {resourceId} not found.");

        var changes = new List<string>();

        if (parameters.TryGetProperty("name", out var nameProp)
            && nameProp.GetString() is { Length: > 0 } newName)
        {
            agent.Name = newName;
            changes.Add($"name='{newName}'");
        }

        if (parameters.TryGetProperty("systemPrompt", out var spProp))
        {
            agent.SystemPrompt = spProp.GetString();
            changes.Add("systemPrompt updated");
        }

        if (parameters.TryGetProperty("modelId", out var midProp)
            && Guid.TryParse(midProp.GetString(), out var newModelId))
        {
            var model = await db.Models
                .Include(m => m.Provider)
                .FirstOrDefaultAsync(m => m.Id == newModelId, ct)
                ?? throw new InvalidOperationException(
                    $"Model {newModelId} not found.");
            agent.ModelId = model.Id;
            agent.Model = model;
            changes.Add($"model='{model.Name}'");
        }

        if (changes.Count == 0)
            return $"Agent '{agent.Name}' (id={agent.Id}) — no changes applied.";

        await db.SaveChangesAsync(ct);

        var summary = string.Join(", ", changes);
        return $"Updated agent '{agent.Name}' (id={agent.Id}): {summary}.";
    }

    // ═══════════════════════════════════════════════════════════════
    // EDIT TASK
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> EditTaskAsync(
        Guid resourceId, JsonElement parameters, CancellationToken ct)
    {
        var task = await db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == resourceId, ct)
            ?? throw new InvalidOperationException(
                $"ScheduledTask {resourceId} not found.");

        var changes = new List<string>();

        if (parameters.TryGetProperty("name", out var nameProp)
            && nameProp.GetString() is { Length: > 0 } newName)
        {
            task.Name = newName;
            changes.Add($"name='{newName}'");
        }

        if (parameters.TryGetProperty("repeatIntervalMinutes", out var intervalProp)
            && intervalProp.TryGetInt32(out var intervalMinutes))
        {
            task.RepeatInterval = intervalMinutes > 0
                ? TimeSpan.FromMinutes(intervalMinutes)
                : null;
            changes.Add($"repeatInterval={task.RepeatInterval?.ToString() ?? "none"}");
        }

        if (parameters.TryGetProperty("maxRetries", out var retriesProp)
            && retriesProp.TryGetInt32(out var retries))
        {
            task.MaxRetries = retries;
            changes.Add($"maxRetries={retries}");
        }

        if (changes.Count == 0)
            return $"Task '{task.Name}' (id={task.Id}) — no changes applied.";

        await db.SaveChangesAsync(ct);

        var summary = string.Join(", ", changes);
        return $"Updated task '{task.Name}' (id={task.Id}): {summary}.";
    }

    // ═══════════════════════════════════════════════════════════════
    // ACCESS SKILL
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> AccessSkillAsync(
        Guid resourceId, CancellationToken ct)
    {
        var skill = await db.Skills
            .FirstOrDefaultAsync(s => s.Id == resourceId, ct)
            ?? throw new InvalidOperationException(
                $"Skill {resourceId} not found.");

        return $"Skill: {skill.Name}\n\n{skill.SkillText}";
    }

    // ═══════════════════════════════════════════════════════════════
    // EDIT AGENT HEADER
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> EditAgentHeaderAsync(
        Guid resourceId, JsonElement parameters, CancellationToken ct)
    {
        var agent = await db.Agents
            .FirstOrDefaultAsync(a => a.Id == resourceId, ct)
            ?? throw new InvalidOperationException(
                $"Agent {resourceId} not found.");

        var header = parameters.TryGetProperty("header", out var hProp)
            ? hProp.GetString()
            : null;

        agent.CustomChatHeader = string.IsNullOrEmpty(header) ? null : header;
        await db.SaveChangesAsync(ct);

        return agent.CustomChatHeader is null
            ? $"Cleared custom chat header for agent '{agent.Name}' (id={agent.Id})."
            : $"Updated custom chat header for agent '{agent.Name}' (id={agent.Id}).";
    }

    // ═══════════════════════════════════════════════════════════════
    // EDIT CHANNEL HEADER
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> EditChannelHeaderAsync(
        Guid resourceId, JsonElement parameters, CancellationToken ct)
    {
        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == resourceId, ct)
            ?? throw new InvalidOperationException(
                $"Channel {resourceId} not found.");

        var header = parameters.TryGetProperty("header", out var hProp)
            ? hProp.GetString()
            : null;

        channel.CustomChatHeader = string.IsNullOrEmpty(header) ? null : header;
        await db.SaveChangesAsync(ct);

        return channel.CustomChatHeader is null
            ? $"Cleared custom chat header for channel '{channel.Title}' (id={channel.Id})."
            : $"Updated custom chat header for channel '{channel.Title}' (id={channel.Id}).";
    }
}
