namespace SharpClaw.Modules.ComputerUse.Contracts;

internal interface IWindowManager
{
    string EnumerateWindows();

    string FocusWindow(int? processId, string? processName, string? titleContains);

    string CaptureWindow(int? processId, string? processName, string? titleContains);

    string CloseWindow(int? processId, string? processName, string? titleContains);
}
