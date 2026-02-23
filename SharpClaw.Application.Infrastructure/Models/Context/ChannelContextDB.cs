using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Contracts.Entities;
using SharpClaw.Infrastructure.Models;

namespace SharpClaw.Application.Infrastructure.Models.Context;

/// <summary>
/// Groups channels and tasks under a shared set of pre-authorised
/// permissions.  Context-level permission grants apply automatically to
/// every channel and task within the context unless overridden by a
/// per-channel or per-task grant.
/// </summary>
public class ChannelContextDB : BaseEntity
{
    public required string Name { get; set; }

    public Guid AgentId { get; set; }
    public AgentDB Agent { get; set; } = null!;

    /// <summary>
    /// Optional permission set for this context. Applies automatically to
    /// every channel and task within the context unless overridden.
    /// </summary>
    public Guid? PermissionSetId { get; set; }
    public PermissionSetDB? PermissionSet { get; set; }

    public ICollection<ChannelDB> Channels { get; set; } = [];
    public ICollection<ScheduledJobDB> Tasks { get; set; } = [];
}
