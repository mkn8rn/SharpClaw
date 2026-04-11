namespace SharpClaw.Contracts.Modules.Contracts;

/// <summary>
/// Contract interface for window management capabilities.
/// Provided by the Computer Use module. Other modules may resolve this
/// interface from DI to enumerate, focus, capture, or close windows.
/// </summary>
public interface IWindowManager
{
    /// <summary>Enumerate visible desktop windows. Returns JSON array.</summary>
    string EnumerateWindows();

    /// <summary>Bring a window to the foreground by PID, process name, or title substring.</summary>
    string FocusWindow(int? processId, string? processName, string? titleContains);

    /// <summary>Capture a single window as a base64 PNG by PID, process name, or title.</summary>
    string CaptureWindow(int? processId, string? processName, string? titleContains);

    /// <summary>Send a graceful close (WM_CLOSE) to a window.</summary>
    string CloseWindow(int? processId, string? processName, string? titleContains);
}
