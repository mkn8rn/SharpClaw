using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SharpClaw.VS2026Extension;

internal sealed class ConnectCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    private readonly SharpClawPackage _package;

    private ConnectCommand(SharpClawPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static ConnectCommand? Instance { get; private set; }

    public static async Task InitializeAsync(SharpClawPackage package, OleMenuCommandService commandService)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        Instance = new ConnectCommand(package, commandService);
    }

    private void Execute(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                if (_package.IsConnected)
                {
                    await _package.SetStatusBarTextAsync("SharpClaw: Already connected");
                    await _package.WriteOutputAsync("Connect requested but already connected.");
                    return;
                }

                await _package.SetStatusBarTextAsync("SharpClaw: Connecting…");
                await _package.WriteOutputAsync("Connecting to SharpClaw backend…");

                await _package.ConnectAsync();

                await _package.SetStatusBarTextAsync("SharpClaw: Connected ✓");
                await _package.WriteOutputAsync("Connected to SharpClaw backend.");
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                await _package.SetStatusBarTextAsync("SharpClaw: Connection failed");
                await _package.WriteOutputAsync(
                    $"Connection failed: {detail} — is the SharpClaw backend running?");
            }
        });
    }
}
