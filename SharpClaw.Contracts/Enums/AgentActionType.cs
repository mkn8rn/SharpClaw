namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Identifies which <see cref="AgentActionService"/> method a job dispatches to.
/// </summary>
public enum AgentActionType
{
    // ── Global flags ──────────────────────────────────────────────
    CreateSubAgent = 0,
    [Obsolete("Moved to mk8.shell module. Use ModuleAction + ActionKey.")]
    CreateContainer = 1,
    RegisterDatabase = 2,
    AccessLocalhostInBrowser = 3,
    AccessLocalhostCli = 4,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    ClickDesktop = 5,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    TypeOnDesktop = 6,

    // ── Per-resource grants ───────────────────────────────────────
    [Obsolete("Moved to Dangerous Shell module. Use ModuleAction + ActionKey.")]
    UnsafeExecuteAsDangerousShell = 7,
    [Obsolete("Moved to mk8.shell module. Use ModuleAction + ActionKey.")]
    ExecuteAsSafeShell = 8,
    AccessInternalDatabases = 9,
    AccessExternalDatabase = 10,
    AccessWebsite = 11,
    QuerySearchEngine = 12,
    AccessContainer = 13,
    ManageAgent = 14,
    EditTask = 15,
    AccessSkill = 16,

    // ── Transcription actions (per-resource: audio device) ────────
    TranscribeFromAudioDevice = 17,
    TranscribeFromAudioStream = 18,
    TranscribeFromAudioFile = 19,

    // ── Display capture (per-resource: display device) ────────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    CaptureDisplay = 20,

    // ── Editor actions (per-resource: editor session) ─────────────
    EditorReadFile = 21,
    EditorGetOpenFiles = 22,
    EditorGetSelection = 23,
    EditorGetDiagnostics = 24,
    EditorApplyEdit = 25,
    EditorCreateFile = 26,
    EditorDeleteFile = 27,
    EditorShowDiff = 28,
    EditorRunBuild = 29,
    EditorRunTerminal = 30,

    // ── Cross-thread context access (global flag) ─────────────────
    ReadCrossThreadHistory = 31,

    // ── Header editing (per-resource: target agent / channel) ──────
    EditAgentHeader = 32,
    EditChannelHeader = 33,

    // ── Bot messaging (per-resource: bot integration) ──────────────
    SendBotMessage = 34,

    // ── Document session management (global flag) ─────────────────
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    CreateDocumentSession = 35,

    // ── File-based spreadsheet actions (per-resource: document session)
    // Uses ClosedXML for .xlsx/.xlsm, CsvHelper for .csv.
    // Operates on the file directly — fails if file is locked.
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetReadRange = 36,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetWriteRange = 37,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetListSheets = 38,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetCreateSheet = 39,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetDeleteSheet = 40,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetGetInfo = 41,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetCreateWorkbook = 42,

    // ── Live spreadsheet actions via COM Interop (per-resource: document session)
    // Operates on the running Excel instance — Windows only.
    // Agent explicitly chooses this when the file is open in Excel.
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetLiveReadRange = 43,
    [Obsolete("Moved to Office Apps module. Use ModuleAction + ActionKey.")]
    SpreadsheetLiveWriteRange = 44,

    // ── Desktop awareness (global flags + per-resource) ───────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    EnumerateWindows = 45,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    LaunchNativeApplication = 46,

    // ── Window management (global flags) ─────────────────────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    FocusWindow = 47,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    CloseWindow = 48,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    ResizeWindow = 49,

    // ── Hotkey (global flag) ─────────────────────────────────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    SendHotkey = 50,

    // ── Window capture (per-resource: display device) ────────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    CaptureWindow = 51,

    // ── Clipboard (global flags) ─────────────────────────────────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    ReadClipboard = 52,
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    WriteClipboard = 53,

    // ── Process control (per-resource: native application) ───────
    [Obsolete("Moved to Computer Use module. Use ModuleAction + ActionKey.")]
    StopProcess = 54,

    // ── Module system ─────────────────────────────────────────────
    // Values 55–99 are reserved for future built-in actions.

    /// <summary>
    /// Category tag for all module-provided tool calls.
    /// The specific tool is identified by <c>AgentJobDB.ActionKey</c>,
    /// which holds the prefixed tool name (e.g. "cu_enumerate_windows").
    /// <c>ScriptJson</c> carries the module envelope for parameter extraction:
    /// <c>{ "module": "computer_use", "tool": "enumerate_windows", "params": { ... } }</c>
    /// </summary>
    ModuleAction = 100,
}
