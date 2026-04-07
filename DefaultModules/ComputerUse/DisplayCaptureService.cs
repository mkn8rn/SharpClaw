using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpClaw.Modules.ComputerUse;

/// <summary>
/// Display capture: full-monitor screenshots, click-marker overlays, and
/// monitor bounds resolution. Windows only.
/// </summary>
public sealed class DisplayCaptureService
{
    /// <summary>
    /// Captures a single monitor using GDI+ <c>CopyFromScreen</c>.
    /// Returns lossless PNG bytes at native resolution.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public byte[] CaptureWindowsDisplay(int displayIndex)
    {
        var bounds = GetDisplayBounds(displayIndex);

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        }

        return EncodePng(bitmap);
    }

    /// <summary>
    /// Captures a display and draws a high-contrast crosshair annotation
    /// at the given display-relative click coordinates.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public byte[] CaptureWindowsDisplayWithClickMarker(
        int displayIndex, int clickX, int clickY)
    {
        var bounds = GetDisplayBounds(displayIndex);

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        }

        DrawClickMarker(bitmap, clickX, clickY);
        return EncodePng(bitmap);
    }

    /// <summary>
    /// Returns the bounding rectangle for the specified monitor index.
    /// Falls back to the virtual screen (all monitors combined).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public Rectangle GetDisplayBounds(int displayIndex)
    {
        var monitors = new List<MONITORINFO>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref info))
                monitors.Add(info);
            return true;
        }, IntPtr.Zero);

        if (displayIndex >= 0 && displayIndex < monitors.Count)
        {
            var r = monitors[displayIndex].rcMonitor;
            return new Rectangle(r.left, r.top, r.right - r.left, r.bottom - r.top);
        }

        // Fallback: virtual screen (all monitors combined).
        var vsX = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        var vsY = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
        var vsW = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
        var vsH = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        return new Rectangle(vsX, vsY, vsW, vsH);
    }

    // ── Drawing helpers ───────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void DrawClickMarker(Bitmap bitmap, int x, int y)
    {
        x = Math.Clamp(x, 0, bitmap.Width - 1);
        y = Math.Clamp(y, 0, bitmap.Height - 1);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var w = bitmap.Width;
        var h = bitmap.Height;

        using var outlinePen = new Pen(Color.FromArgb(180, 0, 0, 0), 3f);
        using var rulerPen = new Pen(Color.FromArgb(210, 255, 0, 0), 1.5f);

        // Horizontal ruler
        g.DrawLine(outlinePen, 0, y, w, y);
        g.DrawLine(rulerPen, 0, y, w, y);

        // Vertical ruler
        g.DrawLine(outlinePen, x, 0, x, h);
        g.DrawLine(rulerPen, x, 0, x, h);

        // Centre marker
        const int outerR = 20;
        const int innerR = 8;

        using var ringOutline = new Pen(Color.FromArgb(200, 0, 0, 0), 3f);
        using var ringBright = new Pen(Color.FromArgb(230, 255, 0, 0), 2f);
        using var fillBrush = new SolidBrush(Color.FromArgb(160, 255, 50, 50));
        using var dotBrush = new SolidBrush(Color.White);

        g.DrawEllipse(ringOutline,
            x - outerR, y - outerR, outerR * 2, outerR * 2);
        g.DrawEllipse(ringBright,
            x - outerR, y - outerR, outerR * 2, outerR * 2);
        g.FillEllipse(fillBrush,
            x - innerR, y - innerR, innerR * 2, innerR * 2);
        g.FillEllipse(dotBrush, x - 3, y - 3, 6, 6);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    // ── Win32 P/Invoke for monitor enumeration ────────────────────

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
