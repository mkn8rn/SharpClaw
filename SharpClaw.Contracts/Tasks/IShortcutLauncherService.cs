namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Abstracts OS shortcut creation and removal for <see cref="TriggerKind.OsShortcut"/> triggers.
/// </summary>
public interface IShortcutLauncherService
{
    /// <summary>
    /// Writes the OS shortcut (.lnk / .desktop) for the given trigger definition.
    /// </summary>
    Task WriteShortcutAsync(TaskTriggerDefinition definition, string customId, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the shortcut file for an existing task (label, icon, category).
    /// </summary>
    Task RefreshShortcutsAsync(TaskTriggerDefinition definition, string customId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all shortcut files and the stub launcher for <paramref name="customId"/>.
    /// </summary>
    Task RemoveShortcutsAsync(string customId, CancellationToken ct = default);
}
