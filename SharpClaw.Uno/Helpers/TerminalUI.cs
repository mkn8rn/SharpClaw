using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;

namespace SharpClaw.Helpers;

/// <summary>
/// Shared terminal-style UI constants, cached brushes, and permission metadata
/// used across MainPage, SettingsPage, and FirstSetupPage.
/// </summary>
internal static class TerminalUI
{
    // ── JSON ─────────────────────────────────────────────────────
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    // ── Fonts ────────────────────────────────────────────────────
    public static readonly FontFamily Mono = new("Consolas, Courier New, monospace");

    // ── Brush cache ─────────────────────────────────────────────
    public static readonly SolidColorBrush Transparent = new(Microsoft.UI.Colors.Transparent);
    private static readonly Dictionary<int, SolidColorBrush> _brushCache = [];

    public static SolidColorBrush Brush(int rgb)
    {
        if (!_brushCache.TryGetValue(rgb, out var brush))
        {
            brush = new SolidColorBrush(ColorFrom(rgb));
            _brushCache[rgb] = brush;
        }
        return brush;
    }

    public static Windows.UI.Color ColorFrom(int rgb)
        => Windows.UI.Color.FromArgb(255,
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));

    // ── Wildcard resource ID ────────────────────────────────────
    public static readonly Guid AllResourcesId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    // ── Provider type names (enum order) ────────────────────────
    public static readonly string[] ProviderTypeNames =
        ["OpenAI", "Anthropic", "OpenRouter", "GoogleGemini", "GoogleVertexAI",
         "ZAI", "VercelAIGateway", "XAI", "Groq", "Cerebras", "Mistral", "GitHubCopilot", "Minimax", "Custom"];

    public static readonly HashSet<string> DeviceCodeProviderTypes = ["GitHubCopilot"];

    // ── Permission metadata ─────────────────────────────────────

    public static readonly (string ApiName, string DisplayName)[] ResourceAccessTypes =
    [
        ("DsShell", "Dangerous Shell"),
        ("Mk8Shell", "Safe Shell"),
        ("Container", "Containers"),
        ("WaWebsite", "Websites"),
        ("WaSearch", "Search Engines"),
        ("DbInternal", "Internal Databases"),
        ("DbExternal", "External Databases"),
        ("TrAudio", "Input Audios"),
        ("CuDisplay", "Display Devices"),
        ("EditorSession", "Editor Sessions"),
        ("AoAgent", "Agent Management"),
        ("AoTask", "Task Management"),
        ("AoSkill", "Skill Management"),
        ("AoAgentHeader", "Agent Header Editing"),
        ("AoChannelHeader", "Channel Header Editing"),
        ("OaDocument", "Document Sessions"),
        ("CuNativeApp", "Native Applications"),
        ("BiChannel", "Bot Integrations"),
    ];

    public static readonly string[] GlobalFlagNames =
        ["CanCreateSubAgents", "CanCreateContainers", "CanRegisterDatabases",
         "CanAccessLocalhostInBrowser", "CanAccessLocalhostCli",
         "CanClickDesktop", "CanTypeOnDesktop", "CanReadCrossThreadHistory",
         "CanEditAgentHeader", "CanEditChannelHeader",
         "CanCreateDocumentSessions", "CanEnumerateWindows",
         "CanFocusWindow", "CanCloseWindow", "CanResizeWindow",
         "CanSendHotkey", "CanReadClipboard", "CanWriteClipboard"];

    public static readonly Dictionary<string, string> GlobalFlagTooltips = new()
    {
        ["CanCreateSubAgents"] = "Allow the agent to spawn child agents on its own",
        ["CanCreateContainers"] = "Allow the agent to create sandboxed execution containers",
        ["CanRegisterDatabases"] = "Allow the agent to register internal or external databases",
        ["CanAccessLocalhostInBrowser"] = "Allow the agent to open localhost URLs in a headless browser",
        ["CanAccessLocalhostCli"] = "Allow the agent to make direct HTTP requests to localhost",
        ["CanClickDesktop"] = "Allow the agent to simulate mouse clicks on the desktop",
        ["CanTypeOnDesktop"] = "Allow the agent to simulate keyboard input on the desktop",
        ["CanReadCrossThreadHistory"] = "Allow the agent to read conversation history from other threads and channels",
        ["CanEditAgentHeader"] = "Allow editing the custom chat header of specific agents",
        ["CanEditChannelHeader"] = "Allow editing the custom chat header of specific channels",
        ["CanCreateDocumentSessions"] = "Allow the agent to register document files (spreadsheets, CSV) as sessions",
        ["CanEnumerateWindows"] = "Allow the agent to list visible desktop windows (title, process, path)",
        ["CanFocusWindow"] = "Allow the agent to bring windows to the foreground",
        ["CanCloseWindow"] = "Allow the agent to send close signals to windows (graceful)",
        ["CanResizeWindow"] = "Allow the agent to move, resize, minimize, or maximize windows",
        ["CanSendHotkey"] = "Allow the agent to send keyboard shortcuts (Ctrl+S, Alt+Tab, etc.)",
        ["CanReadClipboard"] = "Allow the agent to read clipboard contents (text, files, images)",
        ["CanWriteClipboard"] = "Allow the agent to set clipboard contents (text or file paths)",
    };

    public static readonly Dictionary<string, string> ResourceAccessTooltips = new()
    {
        ["DsShell"] = "Unrestricted shell commands \u2014 use with extreme caution",
        ["Mk8Shell"] = "Shell commands restricted to the mk8.shell allowlist",
        ["Container"] = "Access to sandboxed execution containers",
        ["WaWebsite"] = "Access to registered website resources",
        ["WaSearch"] = "Access to registered search engine resources",
        ["DbInternal"] = "Access to SharpClaw-managed internal databases",
        ["DbExternal"] = "Access to registered external database endpoints",
        ["TrAudio"] = "Access to audio capture devices for transcription",
        ["CuDisplay"] = "Access to display devices for screen capture",
        ["EditorSession"] = "Access to IDE editor sessions via the editor bridge",
        ["AoAgent"] = "Manage other agents (create, update, delete)",
        ["AoTask"] = "Manage scheduled tasks and jobs",
        ["AoSkill"] = "Access registered skills and their definitions",
        ["AoAgentHeader"] = "Edit the custom chat header of specific agents",
        ["AoChannelHeader"] = "Edit the custom chat header of specific channels",
        ["OaDocument"] = "Access to registered document files for spreadsheet operations",
        ["CuNativeApp"] = "Access to registered desktop applications for launch and process control",
        ["BiChannel"] = "Access to registered bot platform integrations",
    };

    public static readonly (string Tag, string Label)[] ClearanceOptions =
    [
        ("Independent",                "Can act without approval"),
        ("ApprovedByWhitelistedAgent", "Only with approval from a managing agent"),
        ("ApprovedByPermittedAgent",   "Only with approval from an agent that has clearance to act"),
        ("ApprovedByWhitelistedUser",  "Only with approval from a user"),
        ("ApprovedBySameLevelUser",    "Only with approval from a user that can grant the permission"),
    ];

    // ── Helpers ──────────────────────────────────────────────────

    public static string FormatFlagName(string flagKey)
    {
        var s = flagKey.AsSpan();
        if (s.StartsWith("Can")) s = s[3..];
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]))
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpper(s[i]) : s[i]);
        }
        return sb.ToString();
    }

    public static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "\u2026";

    public static void CopyToClipboard(string text)
    {
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
    }

    // ── Reusable control factories ──────────────────────────────

    /// <summary>Creates a clearance <see cref="ComboBox"/> pre-populated with the standard options.</summary>
    public static ComboBox MakeClearanceCombo(string selected, bool includeUnset = true)
    {
        var box = new ComboBox
        {
            FontFamily = Mono, FontSize = 10,
            Background = Brush(0x1A1A1A), Foreground = Brush(0xCCCCCC),
            BorderBrush = Brush(0x333333), BorderThickness = new Thickness(1),
            MinWidth = 280, Padding = new Thickness(4, 2),
        };
        var selIdx = 0;
        var idx = 0;
        if (includeUnset)
        {
            box.Items.Add(new ComboBoxItem { Content = "Unset", Tag = "Unset" });
            if (string.Equals("Unset", selected, StringComparison.OrdinalIgnoreCase)) selIdx = 0;
            idx = 1;
        }
        for (var i = 0; i < ClearanceOptions.Length; i++, idx++)
        {
            box.Items.Add(new ComboBoxItem { Content = ClearanceOptions[i].Label, Tag = ClearanceOptions[i].Tag });
            if (string.Equals(ClearanceOptions[i].Tag, selected, StringComparison.OrdinalIgnoreCase)) selIdx = idx;
        }
        box.SelectedIndex = selIdx;
        return box;
    }

    /// <summary>Creates a small "✕" remove button in terminal style.</summary>
    public static Button RemoveButton(Action onClick)
    {
        var btn = new Button
        {
            Content = new TextBlock { Text = "\u2715", FontFamily = Mono, FontSize = 10, Foreground = Brush(0xFF4444) },
            Background = Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4), MinWidth = 0, MinHeight = 0,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
