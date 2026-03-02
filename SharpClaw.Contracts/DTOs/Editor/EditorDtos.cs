using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.Editor;

// ═══════════════════════════════════════════════════════════════
// Editor context — attached to chat/job requests from extensions
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Optional editor context sent with chat and job requests so the
/// agent knows the user's current editor state.
/// </summary>
public sealed record EditorContext(
    EditorType EditorType,
    string? EditorVersion = null,
    string? WorkspacePath = null,
    string? ActiveFilePath = null,
    string? ActiveFileLanguage = null,
    int? SelectionStartLine = null,
    int? SelectionEndLine = null,
    string? SelectedText = null);

// ═══════════════════════════════════════════════════════════════
// WebSocket protocol — messages between SharpClaw ↔ extension
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Registration message sent by the extension when it connects.
/// </summary>
public sealed record EditorRegistrationMessage(
    EditorType EditorType,
    string? EditorVersion,
    string? WorkspacePath);

/// <summary>
/// Request sent from SharpClaw to the extension to execute an
/// editor action.  The extension must respond with
/// <see cref="EditorActionResponse"/> keyed by <see cref="RequestId"/>.
/// </summary>
public sealed record EditorActionRequest(
    Guid RequestId,
    string Action,
    Dictionary<string, object?>? Params = null);

/// <summary>
/// Response sent from the extension back to SharpClaw after
/// executing an editor action.
/// </summary>
public sealed record EditorActionResponse(
    Guid RequestId,
    bool Success,
    string? Data = null,
    string? Error = null);

// ═══════════════════════════════════════════════════════════════
// Editor session resource
// ═══════════════════════════════════════════════════════════════

public sealed record EditorSessionResponse(
    Guid Id,
    string Name,
    EditorType EditorType,
    string? EditorVersion,
    string? WorkspacePath,
    string? Description,
    bool IsConnected,
    DateTimeOffset CreatedAt);

public sealed record CreateEditorSessionRequest(
    string Name,
    EditorType EditorType,
    string? EditorVersion = null,
    string? WorkspacePath = null,
    string? Description = null);

public sealed record UpdateEditorSessionRequest(
    string? Name = null,
    string? Description = null);

// ═══════════════════════════════════════════════════════════════
// Editor tool payloads (parsed from tool call JSON)
// ═══════════════════════════════════════════════════════════════

public sealed record EditorReadFilePayload(
    string? TargetId,
    string? FilePath,
    int? StartLine,
    int? EndLine);

public sealed record EditorApplyEditPayload(
    string? TargetId,
    string? FilePath,
    int StartLine,
    int EndLine,
    string? NewText);

public sealed record EditorCreateFilePayload(
    string? TargetId,
    string? FilePath,
    string? Content);

public sealed record EditorDeleteFilePayload(
    string? TargetId,
    string? FilePath);

public sealed record EditorShowDiffPayload(
    string? TargetId,
    string? FilePath,
    string? ProposedContent,
    string? DiffTitle);

public sealed record EditorRunTerminalPayload(
    string? TargetId,
    string? Command,
    string? WorkingDirectory);

public sealed record EditorResourceOnlyPayload(
    string? TargetId);
