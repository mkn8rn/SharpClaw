namespace SharpClaw.Contracts.Modules.Contracts;

/// <summary>
/// Contract interface for desktop input simulation capabilities.
/// Provided by the Computer Use module. Other modules may resolve this
/// interface from DI to simulate mouse clicks, keyboard input, and hotkeys.
/// </summary>
public interface IDesktopInput
{
    /// <summary>Simulate a mouse click at the given absolute screen coordinates.</summary>
    void PerformClick(int absoluteX, int absoluteY, string? button = null, string? clickType = null);

    /// <summary>Simulate keyboard text input. Characters are sent individually using Unicode input events.</summary>
    void PerformType(string text);

    /// <summary>
    /// Send a keyboard shortcut combination (e.g. "Ctrl+S").
    /// Optionally targets a specific window by PID or title substring.
    /// </summary>
    string SendHotkey(string keys, int? processId = null, string? titleContains = null);
}
