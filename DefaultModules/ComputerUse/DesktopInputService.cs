using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpClaw.Contracts.Modules.Contracts;

namespace SharpClaw.Modules.ComputerUse;

/// <summary>
/// Desktop input simulation: mouse clicks and keyboard typing via SendInput.
/// Implements <see cref="IDesktopInput"/> for cross-module consumption.
/// </summary>
public sealed class DesktopInputService(DesktopAwarenessService desktopAwareness) : IDesktopInput
{
    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public void PerformClick(int absoluteX, int absoluteY, string? button = null, string? clickType = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Desktop click is only supported on Windows.");

        var btn = (button ?? "left").ToLowerInvariant();
        var click = (clickType ?? "single").ToLowerInvariant();

        SetCursorPos(absoluteX, absoluteY);

        uint downFlag, upFlag;
        switch (btn)
        {
            case "right":
                downFlag = MOUSEEVENTF_RIGHTDOWN;
                upFlag = MOUSEEVENTF_RIGHTUP;
                break;
            case "middle":
                downFlag = MOUSEEVENTF_MIDDLEDOWN;
                upFlag = MOUSEEVENTF_MIDDLEUP;
                break;
            default:
                downFlag = MOUSEEVENTF_LEFTDOWN;
                upFlag = MOUSEEVENTF_LEFTUP;
                break;
        }

        var clicks = click == "double" ? 2 : 1;
        for (var i = 0; i < clicks; i++)
        {
            var inputs = new INPUT[2];
            inputs[0] = CreateMouseInput(downFlag);
            inputs[1] = CreateMouseInput(upFlag);
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public void PerformType(string text)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Desktop typing is only supported on Windows.");

        var inputs = new List<INPUT>(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, 0));
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, KEYEVENTF_KEYUP));
                continue;
            }

            if (c == '\n')
            {
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, 0));
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, KEYEVENTF_KEYUP));
                continue;
            }

            if (c == '\t')
            {
                inputs.Add(CreateVirtualKeyInput(VK_TAB, 0));
                inputs.Add(CreateVirtualKeyInput(VK_TAB, KEYEVENTF_KEYUP));
                continue;
            }

            inputs.Add(CreateKeyboardInput(c, KEYEVENTF_UNICODE));
            inputs.Add(CreateKeyboardInput(c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        if (inputs.Count > 0)
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <inheritdoc />
    public string SendHotkey(string keys, int? processId = null, string? titleContains = null) =>
        desktopAwareness.SendHotkey(keys, processId, titleContains);

    // ── Win32 SendInput helpers ───────────────────────────────────

    private static INPUT CreateMouseInput(uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } }
    };

    private static INPUT CreateKeyboardInput(char c, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = flags } }
    };

    private static INPUT CreateVirtualKeyInput(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };

    // ── Constants ─────────────────────────────────────────────────

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    // ── P/Invoke ──────────────────────────────────────────────────

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── Structs ───────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }
}
