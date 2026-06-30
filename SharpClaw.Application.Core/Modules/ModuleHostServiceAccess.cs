using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Services;
using SharpClaw.Core.Modules;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Core.Modules;

/// <summary>
/// Host-owned service restrictions applied to in-process module execution.
/// </summary>
internal static class ModuleHostServiceAccess
{
    private static readonly Type[] BlockedTypes =
    [
        typeof(AgentJobService),
        typeof(AgentActionService),
        typeof(ChatService),
        typeof(ModuleService),
        typeof(ModuleRegistry),
        typeof(ModuleLoader),
        typeof(SharpClawDbContext),
        typeof(IServiceScopeFactory),
    ];

    public static IReadOnlyCollection<Type> BlockedServiceTypes => BlockedTypes;

    public static ModuleServiceScope CreateRestrictedScope(
        IServiceProvider inner,
        string moduleId) =>
        new(inner, moduleId, BlockedTypes);
}
