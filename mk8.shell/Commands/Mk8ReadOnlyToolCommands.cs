using Mk8.Shell.Safety;

namespace Mk8.Shell;

/// <summary>
/// Read-only tool command templates for <see cref="Mk8CommandWhitelist"/>.
/// <para>
/// All tools below accept ONLY sandbox-validated path arguments — no
/// free-text parameters.  Tools that require free text have been removed:
/// <list type="bullet">
///   <item><c>echo</c>  — use <c>FileWrite</c> verb instead</item>
///   <item><c>grep</c>  — use <c>TextRegex</c> verb instead</item>
///   <item><c>jq</c>    — use <c>JsonQuery</c> verb instead</item>
/// </list>
/// </para>
/// <para>
/// Several tools below have in-memory verb equivalents
/// (<c>cat</c> → <c>FileRead</c>, <c>sha256sum</c> → <c>FileHash</c>)
/// but are retained for convenience and parity with CLI expectations.
/// </para>
/// <para>
/// All data is compile-time constant.  To add or modify allowed commands,
/// edit this file and recompile — there is no runtime registration.
/// </para>
/// </summary>
public static class Mk8ReadOnlyToolCommands
{
    internal static KeyValuePair<string, string[]>[] GetWordLists() => [];

    internal static Mk8AllowedCommand[] GetCommands()
    {
        var readPath = new Mk8Slot("file", Mk8SlotKind.SandboxPath);
        var lineCount = new Mk8Slot("count", Mk8SlotKind.IntRange, MinValue: 1, MaxValue: 1000);

        return
        [
            // cat
            new("cat", "cat", [], Params: [readPath]),

            // head / tail
            new("head", "head", ["-n"], Params: [lineCount, readPath]),
            new("tail", "tail", ["-n"], Params: [lineCount, readPath]),

            // wc (mandatory mode flag as prefix)
            new("wc lines", "wc", ["-l"], Params: [readPath]),
            new("wc words", "wc", ["-w"], Params: [readPath]),
            new("wc bytes", "wc", ["-c"], Params: [readPath]),

            // sort / uniq
            new("sort", "sort", [], Params: [readPath]),
            new("uniq", "uniq", [], Params: [readPath]),

            // diff
            new("diff", "diff", [],
                Params: [readPath, new Mk8Slot("file2", Mk8SlotKind.SandboxPath)]),

            // sha256sum / md5sum
            new("sha256sum", "sha256sum", [], Params: [readPath]),
            new("md5sum", "md5sum", [], Params: [readPath]),

            // base64
            new("base64 encode", "base64", [], Params: [readPath]),
            new("base64 decode", "base64", ["-d"], Params: [readPath]),
        ];
    }
}
