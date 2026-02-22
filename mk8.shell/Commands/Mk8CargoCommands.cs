using Mk8.Shell.Safety;

namespace Mk8.Shell;

/// <summary>
/// <c>cargo</c> command templates for <see cref="Mk8CommandWhitelist"/>.
/// <para>
/// Only <c>--version</c> is whitelisted.  <c>cargo build/test/run</c>
/// execute <c>build.rs</c> scripts and are excluded.
/// </para>
/// <para>
/// All data is compile-time constant.  To add or modify allowed commands,
/// edit this file and recompile — there is no runtime registration.
/// </para>
/// </summary>
public static class Mk8CargoCommands
{
    internal static KeyValuePair<string, string[]>[] GetWordLists() => [];

    internal static Mk8AllowedCommand[] GetCommands() =>
    [
        new("cargo version", "cargo", ["--version"]),
    ];
}
