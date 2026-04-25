namespace SharpClaw.Modules.EditorCommon.Models;

/// <summary>
/// Identifies the IDE/editor connected through the editor bridge.
/// Internal to the EditorCommon module; the public API surface uses string keys.
/// </summary>
public enum EditorType
{
    VisualStudio2026 = 0,
    VisualStudioCode = 1,
    Other = 2
}
