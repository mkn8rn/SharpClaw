using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Entities.Core.Clearance;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.Providers;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class HeaderTagProcessor(
    SharpClawDbContext db,
    ChatHeaderTemplateEngine headerTemplates,
    IServiceProvider serviceProvider,
    IConfiguration configuration)
{
    private readonly bool _disableModuleHeaderTags =
        configuration.GetValue<bool>("Chat:DisableModuleHeaderTags");

    private readonly bool _disableHeaderTagExpansion =
        configuration.GetValue<bool>("Chat:DisableHeaderTagExpansion");

    private readonly IChatHeaderResourceTagResolver _resourceTags =
        new EfHeaderResourceTagResolver(db);

    public async Task<string> ExpandAsync(
        string template,
        ChannelDB channel,
        AgentDB agent,
        string clientType,
        Guid? userId,
        CancellationToken ct,
        CompletionParameters? completionParameters = null,
        string providerKey = "")
    {
        var options = new ChatHeaderExpansionOptions(
            DisableHeaderTagExpansion: _disableHeaderTagExpansion,
            DisableModuleHeaderTags: _disableModuleHeaderTags);

        if (options.DisableHeaderTagExpansion)
            return template;

        var tagNames = ChatHeaderTemplateEngine.ExtractTagNames(template);
        if (tagNames.Count == 0)
            return template;

        var context = await BuildContextAsync(
            channel,
            agent,
            clientType,
            userId,
            tagNames,
            ct,
            completionParameters,
            providerKey);

        return await headerTemplates.ExpandAsync(
            template,
            context,
            options,
            _resourceTags,
            serviceProvider,
            ct);
    }

    private async Task<ChatHeaderExpansionContext> BuildContextAsync(
        ChannelDB channel,
        AgentDB agent,
        string clientType,
        Guid? userId,
        IReadOnlyCollection<string> tagNames,
        CancellationToken ct,
        CompletionParameters? completionParameters = null,
        string providerKey = "")
    {
        var requiredTags = tagNames
            .Select(static tag => tag.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var needsUser = userId is not null
            && (requiredTags.Contains("user")
                || requiredTags.Contains("role")
                || requiredTags.Contains("bio")
                || requiredTags.Contains("grants"));
        var needsUserPermissionSet = requiredTags.Contains("role")
            || requiredTags.Contains("grants");
        var needsAgentPermissionSet = requiredTags.Contains("agent-role")
            || requiredTags.Contains("agent-grants");

        UserDB? user = null;
        PermissionSetDB? userPs = null;
        if (needsUser)
        {
            user = await db.Users
                .AsNoTracking()
                .Include(u => u.Role).ThenInclude(r => r!.PermissionSet)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (needsUserPermissionSet && user?.Role?.PermissionSetId is { } psId)
            {
                userPs = await db.PermissionSets
                    .AsNoTracking()
                    .Include(p => p.GlobalFlags)
                    .Include(p => p.ResourceAccesses)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(p => p.Id == psId, ct);
            }
        }

        RoleDB? agentRole = null;
        PermissionSetDB? agentPs = null;
        if (needsAgentPermissionSet)
        {
            var agentWithRole = await db.Agents
                .AsNoTracking()
                .Include(a => a.Role).ThenInclude(r => r!.PermissionSet)
                .FirstOrDefaultAsync(a => a.Id == agent.Id, ct);

            agentRole = agentWithRole?.Role;
            if (agentRole?.PermissionSetId is { } agentPsId)
            {
                agentPs = await db.PermissionSets
                    .AsNoTracking()
                    .Include(p => p.ResourceAccesses)
                    .Include(p => p.GlobalFlags)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(p => p.Id == agentPsId, ct);
            }
        }

        return new ChatHeaderExpansionContext(
            channel,
            agent,
            clientType,
            user,
            userPs,
            agentRole,
            agentPs,
            completionParameters,
            providerKey);
    }

    private sealed class EfHeaderResourceTagResolver(SharpClawDbContext db)
        : IChatHeaderResourceTagResolver
    {
        public async Task<IReadOnlyList<BaseEntity>?> LoadEntitiesAsync(
            string tagName,
            CancellationToken ct)
        {
            return tagName.ToLowerInvariant() switch
            {
                "agents" => Cast(await db.Agents.AsNoTracking().ToListAsync(ct)),
                "models" => Cast(await db.Models
                    .AsNoTracking()
                    .Include(m => m.Provider)
                    .ToListAsync(ct)),
                "providers" => Cast(await db.Providers.AsNoTracking().ToListAsync(ct)),
                "channels" => Cast(await db.Channels.AsNoTracking().ToListAsync(ct)),
                "threads" => Cast(await db.ChatThreads.AsNoTracking().ToListAsync(ct)),
                "roles" => Cast(await db.Roles.AsNoTracking().ToListAsync(ct)),
                "users" => Cast(await db.Users.AsNoTracking().ToListAsync(ct)),
                "tasks" or "taskdefinitions" => Cast(await db.TaskDefinitions
                    .AsNoTracking()
                    .ToListAsync(ct)),
                _ => null
            };
        }

        private static IReadOnlyList<BaseEntity> Cast<T>(List<T> items)
            where T : BaseEntity
        {
            return items.ConvertAll(static item => (BaseEntity)item);
        }
    }
}
