using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SharpClaw.VS2026Extension;

internal sealed class DisconnectCommand
{
    public const int CommandId = 0x0101;
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    private readonly SharpClawPackage _package;

    private DisconnectCommand(SharpClawPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static DisconnectCommand? Instance { get; private set; }

    public static async Task InitializeAsync(SharpClawPackage package, OleMenuCommandService commandService)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        Instance = new DisconnectCommand(package, commandService);
    }

    private void Execute(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                if (!_package.IsConnected)
                {
                    await _package.SetStatusBarTextAsync("SharpClaw: Not connected");
                    await _package.WriteOutputAsync("Disconnect requested but not connected.");
                    return;
                }

                await _package.SetStatusBarTextAsync("SharpClaw: Disconnecting…");
                await _package.WriteOutputAsync("Disconnecting from SharpClaw backend…");

                await _package.DisconnectAsync();

                await _package.SetStatusBarTextAsync("SharpClaw: Disconnected");
                await _package.WriteOutputAsync("Disconnected from SharpClaw backend.");
            }
            catch (Exception ex)
            {
                await _package.SetStatusBarTextAsync("SharpClaw: Disconnect failed");
                await _package.WriteOutputAsync($"Disconnect failed: {ex.Message}");
            }
        });
    }
}
