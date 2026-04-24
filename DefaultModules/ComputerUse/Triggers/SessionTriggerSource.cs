using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.ComputerUse.Triggers;

/// <summary>
/// Fires when the screen is locked or unlocked.
/// Uses <c>WTSRegisterSessionNotification</c> on Windows; D-Bus
/// <c>org.freedesktop.ScreenSaver.ActiveChanged</c> on Linux.
/// Not supported on macOS.
/// </summary>
public sealed class SessionTriggerSource(
    ILogger<SessionTriggerSource> logger) : ITaskTriggerSource, IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private IReadOnlyList<ITaskTriggerSourceContext> _contexts = [];

    public IReadOnlyList<TriggerKind> SupportedKinds { get; } =
        [TriggerKind.ScreenLocked, TriggerKind.ScreenUnlocked];

    public Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            logger.LogWarning("TASK441: SessionTriggerSource is not supported on macOS.");
            return Task.CompletedTask;
        }

        _contexts = contexts;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            _listenTask = ListenLinuxAsync(_cts.Token);

        // Windows: session-change notifications require a window message pump,
        // which is not available in a headless service. Poll WTS state instead.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _listenTask = PollWindowsAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_listenTask is not null)
            {
                try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { /* ignore */ }
            }

            _cts.Dispose();
            _cts = null;
        }

        _contexts = [];
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    [SupportedOSPlatform("windows")]
    private async Task PollWindowsAsync(CancellationToken ct)
    {
        var wasLocked = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                var isLocked = IsWindowsSessionLocked();

                if (isLocked != wasLocked)
                {
                    wasLocked = isLocked;
                    await FireMatchingAsync(isLocked
                        ? TriggerKind.ScreenLocked
                        : TriggerKind.ScreenUnlocked);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SessionTriggerSource Windows poll error.");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsSessionLocked()
    {
        try
        {
            nint hDesktop = OpenDesktop("Default", 0, false, 0x0100 /* DESKTOP_READOBJECTS */);
            if (hDesktop == nint.Zero) return true;
            CloseDesktop(hDesktop);
            return false;
        }
        catch { return false; }
    }

    [SupportedOSPlatform("linux")]
    private async Task ListenLinuxAsync(CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "dbus-monitor",
                "--session \"type='signal',interface='org.freedesktop.ScreenSaver'\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return;

            while (!ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line is null) break;

                if (line.Contains("boolean true"))
                    await FireMatchingAsync(TriggerKind.ScreenLocked);
                else if (line.Contains("boolean false"))
                    await FireMatchingAsync(TriggerKind.ScreenUnlocked);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SessionTriggerSource Linux D-Bus listener error.");
        }
    }

    private async Task FireMatchingAsync(TriggerKind kind)
    {
        foreach (var ctx in _contexts.Where(c => c.Definition.Kind == kind))
        {
            try { await ctx.FireAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "SessionTriggerSource failed to fire context for definition {Id}.", ctx.TaskDefinitionId);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [SupportedOSPlatform("windows")]
    private static extern nint OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool CloseDesktop(nint hDesktop);
}
