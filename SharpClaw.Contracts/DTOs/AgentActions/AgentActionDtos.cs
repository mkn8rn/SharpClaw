using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.AgentActions;

/// <summary>
/// Identifies the user or agent that triggered an agent action and whose
/// authority is evaluated against the clearance requirement.
/// </summary>
public sealed record ActionCaller(Guid? UserId = null, Guid? AgentId = null);

/// <summary>
/// Result of evaluating an agent action against the permission model.
/// </summary>
public sealed record AgentActionResult(
    ClearanceVerdict Verdict,
    string Reason,
    PermissionClearance EffectiveClearance = PermissionClearance.Unset)
{
    public static AgentActionResult Denied(string reason) =>
        new(ClearanceVerdict.Denied, reason);

    public static AgentActionResult Approve(string reason, PermissionClearance clearance) =>
        new(ClearanceVerdict.Approved, reason, clearance);

    public static AgentActionResult Pending(string reason, PermissionClearance clearance) =>
        new(ClearanceVerdict.PendingApproval, reason, clearance);
}
