namespace SharpClaw.Application.Infrastructure.Tasks.Models;

/// <summary>
/// The OS platform(s) on which a task is permitted to run.
/// Declared via <c>[RequiresPlatform(TaskPlatform.Windows)]</c> on the task class.
/// </summary>
[Flags]
public enum TaskPlatform
{
    Windows    = 1,
    Linux      = 2,
    MacOS      = 4,
    AnyDesktop = Windows | Linux | MacOS,
}
