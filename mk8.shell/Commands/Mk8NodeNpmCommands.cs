using Mk8.Shell.Safety;

namespace Mk8.Shell;

/// <summary>
/// <c>node</c> and <c>npm</c> command templates for <see cref="Mk8CommandWhitelist"/>.
/// <para>
/// Only read-only / informational subcommands are whitelisted.
/// <c>npm install</c> and all lifecycle-triggering subcommands are excluded.
/// <c>npx</c> is permanently blocked (downloads + executes arbitrary packages).
/// </para>
/// <para>
/// All data is compile-time constant.  To add or modify allowed commands,
/// edit this file and recompile — there is no runtime registration.
/// </para>
/// </summary>
public static class Mk8NodeNpmCommands
{
    internal static KeyValuePair<string, string[]>[] GetWordLists() => [];

    internal static Mk8AllowedCommand[] GetCommands() =>
    [
        // ── node ──────────────────────────────────────────────────
        new("node version", "node", ["--version"]),

        // ── npm ───────────────────────────────────────────────────
        new("npm version", "npm", ["--version"]),

        new("npm ls", "npm", ["ls"],
            Flags: [
                new Mk8FlagDef("--depth",
                    new Mk8Slot("depth", Mk8SlotKind.IntRange, MinValue: 0, MaxValue: 10)),
                new Mk8FlagDef("--all"),
                new Mk8FlagDef("--json"),
            ]),

        new("npm outdated", "npm", ["outdated"],
            Flags: [new Mk8FlagDef("--json")]),
    ];
}
