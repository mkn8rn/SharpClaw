namespace SharpClaw.Modules.ComputerUse.Contracts;

internal interface IDesktopInput
{
    void PerformClick(int absoluteX, int absoluteY, string? button = null, string? clickType = null);

    void PerformType(string text);

    string SendHotkey(string keys, int? processId = null, string? titleContains = null);
}
