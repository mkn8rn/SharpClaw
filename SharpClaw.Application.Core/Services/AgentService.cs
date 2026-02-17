using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.DTOs.Agents;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class AgentService(SharpClawDbContext db)
{
    public async Task<AgentResponse> CreateAsync(CreateAgentRequest request, CancellationToken ct = default)
    {
        var model = await db.Models
            .Include(m => m.Provider)
            .FirstOrDefaultAsync(m => m.Id == request.ModelId, ct)
            ?? throw new ArgumentException($"Model {request.ModelId} not found.");

        var agent = new AgentDB
        {
            Name = request.Name,
            SystemPrompt = request.SystemPrompt,
            ModelId = model.Id
        };

        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);

        return ToResponse(agent, model);
    }

    public async Task<AgentResponse?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Name == name, ct);

        return agent is null ? null : ToResponse(agent, agent.Model);
    }

    public async Task<AgentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        return agent is null ? null : ToResponse(agent, agent.Model);
    }

    public async Task<IReadOnlyList<AgentResponse>> ListAsync(CancellationToken ct = default)
    {
        return await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .Select(a => new AgentResponse(
                a.Id, a.Name, a.SystemPrompt,
                a.ModelId, a.Model.Name, a.Model.Provider.Name))
            .ToListAsync(ct);
    }

    public async Task<AgentResponse?> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct = default)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return null;

        if (request.Name is not null) agent.Name = request.Name;
        if (request.SystemPrompt is not null) agent.SystemPrompt = request.SystemPrompt;
        if (request.ModelId is { } modelId)
        {
            var model = await db.Models.Include(m => m.Provider).FirstOrDefaultAsync(m => m.Id == modelId, ct)
                ?? throw new ArgumentException($"Model {modelId} not found.");
            agent.ModelId = model.Id;
            agent.Model = model;
        }

        await db.SaveChangesAsync(ct);
        return ToResponse(agent, agent.Model);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await db.Agents.FindAsync([id], ct);
        if (agent is null) return false;

        db.Agents.Remove(agent);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static AgentResponse ToResponse(AgentDB agent, ModelDB model) =>
        new(agent.Id, agent.Name, agent.SystemPrompt, model.Id, model.Name, model.Provider.Name);
}
