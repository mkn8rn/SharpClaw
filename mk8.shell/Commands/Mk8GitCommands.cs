using Mk8.Shell.Safety;

namespace Mk8.Shell;

/// <summary>
/// <c>git</c> command templates and word lists for <see cref="Mk8CommandWhitelist"/>.
/// <para>
/// Git was previously removed from mk8.shell entirely because its flag
/// interpreter surface was too large for a blocklist model.  The strict
/// command-template whitelist makes re-introduction safe — only the EXACT
/// templates below can be executed, and no unregistered flag can reach git.
/// </para>
/// <para>
/// <b>Defence-in-depth:</b> <see cref="Mk8PathSanitizer.ResolveForWrite"/>
/// blocks all writes to <c>.git/</c> paths, preventing hook injection
/// (<c>.git/hooks/pre-commit</c>) and config tampering (<c>.git/config</c>).
/// </para>
/// <para>
/// All data is compile-time constant.  To add or modify allowed commands,
/// edit this file and recompile — there is no runtime registration.
/// </para>
/// </summary>
public static class Mk8GitCommands
{
    // ── Word lists (edit these arrays to change what agents can use) ──

    /// <summary>
    /// Vocabulary for composing commit messages via <c>git commit -m</c>.
    /// The agent combines words from this list with spaces between them
    /// (max <see cref="Mk8CommandWhitelist.MaxComposedWords"/> words).
    /// <para>
    /// Spaces are safe — <c>ProcessStartInfo.ArgumentList.Add("Fix auth errors")</c>
    /// passes the entire string as one OS-level argument. No shell tokenizes it.
    /// </para>
    /// <para>
    /// Example compositions: <c>"Fix build errors"</c>, <c>"Add auth middleware"</c>,
    /// <c>"Refactor agent handler A"</c>, <c>"WIP"</c>.
    /// </para>
    /// </summary>
    public static readonly string[] CommitWords =
    [
        // ── Verbs ─────────────────────────────────────────────────
        "Add", "Fix", "Update", "Remove", "Delete", "Refactor",
        "Move", "Rename", "Clean", "Optimize", "Configure",
        "Wire", "Scaffold", "Extract", "Inline", "Format",
        "Implement", "Replace", "Merge", "Split", "Revert",
        "Enable", "Disable", "Restore", "Sync", "Simplify",
        "Improve", "Reorganize", "Consolidate", "Introduce",
        "Deprecate", "Drop", "Bump", "Pin", "Unpin",

        // ── Adjectives / modifiers ────────────────────────────────
        "Initial", "Minor", "Major", "Quick", "Temporary",
        "Deprecated", "Breaking", "Critical", "Partial",
        "Missing", "Unused", "Duplicate", "Stale", "Draft",
        "Experimental", "Internal", "Public", "Private",
        "New", "Old", "Default", "Custom", "Base",

        // ── Nouns — general ───────────────────────────────────────
        "commit", "migration", "migrations", "schema",
        "tests", "test", "build", "builds",
        "errors", "error", "typos", "typo",
        "code", "files", "file", "config", "configuration",
        "dependencies", "dependency", "imports", "import",
        "comments", "comment", "documentation", "docs",
        "README", "CHANGELOG",
        "endpoints", "endpoint", "commands", "command",
        "middleware", "project", "projects",
        "services", "service", "method", "methods",
        "class", "classes", "interface", "interfaces",
        "logging", "validation", "handling",
        "namespace", "namespaces", "package", "packages",
        "module", "modules", "component", "components",
        "feature", "features", "bugfix", "hotfix",
        "release", "version", "setup", "cleanup",
        "logic", "flow", "pipeline", "workflow",
        "response", "request", "route", "routes",
        "handler", "handlers", "controller",
        "query", "queries", "index", "indexes",
        "table", "tables", "column", "columns",
        "constraint", "constraints", "relation", "relations",
        "key", "keys", "property", "properties",
        "field", "fields", "parameter", "parameters",
        "type", "types", "enum", "enums",
        "seed", "data", "baseline", "snapshot",
        "template", "templates", "factory", "helper",
        "options", "settings", "constants",

        // ── Nouns — domain (SharpClaw-specific) ───────────────────
        "auth", "authentication", "authorization", "permissions",
        "agents", "agent", "channels", "channel",
        "contexts", "context", "models", "model",
        "providers", "provider", "jobs", "job",
        "tasks", "task", "skills", "skill",
        "resources", "resource", "containers", "container",
        "devices", "device", "messages", "message",
        "roles", "role", "users", "user",
        "transcription", "audio", "scheduler",
        "tokens", "token", "encryption",
        "API", "CLI", "DI", "EF", "DB", "UI",
        "REST", "gRPC", "JSON", "YAML", "XML",

        // ── Connectors / prepositions ─────────────────────────────
        "up", "in", "for", "to", "from", "with", "and", "of",
        "on", "at", "by", "as", "into", "across",

        // ── States / phrases ──────────────────────────────────────
        "work", "progress", "checkpoint", "WIP",

        // ── Single letters and digits (case-insensitive) ──────────
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
    ];

    /// <summary>
    /// Pre-approved branch names for <c>git checkout</c>, <c>git switch</c>.
    /// Agents cannot create or switch to arbitrary branch names.
    /// <para>
    /// <b>INTENTIONALLY EXCLUDED — protected branches:</b>
    /// <c>main</c>, <c>master</c>, <c>develop</c>, <c>staging</c>,
    /// <c>production</c>, <c>live</c>, <c>release</c>, <c>release/*</c>,
    /// <c>trunk</c>.
    /// Agents must NEVER operate on branches used for live or master
    /// development.  All agent work must happen in a dedicated
    /// feature/bugfix/hotfix branch.  Merging to protected branches
    /// requires the dangerous-shell path with human approval.
    /// </para>
    /// </summary>
    public static readonly string[] BranchNames =
    [
        // ── Protected branches — BANNED (do NOT add these) ────────
        // "main", "master", "develop", "staging", "production",
        // "live", "release", "trunk"
        // This includes release/* branches — release branches track
        // live-bound code and are equally dangerous.
        // Agents must never checkout, switch to, or create these.
        // All git write operations must happen in a safe branch.

        // Features
        "feature/auth", "feature/agents", "feature/channels",
        "feature/models", "feature/providers", "feature/jobs",
        "feature/tasks", "feature/skills", "feature/resources",
        "feature/api", "feature/cli", "feature/tests",
        "feature/docs", "feature/config", "feature/logging",
        "feature/permissions", "feature/transcription",
        "feature/shell", "feature/containers",

        // Bug fixes
        "bugfix/build", "bugfix/tests", "bugfix/auth",
        "bugfix/api", "bugfix/cli", "bugfix/data",
        "bugfix/permissions", "bugfix/models",

        // Hotfixes
        "hotfix/security", "hotfix/critical", "hotfix/data",

        // Single letters and digits (matching is case-insensitive).
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
    ];

    /// <summary>
    /// Pre-approved remote names for <c>git remote add</c> /
    /// <c>git remote remove</c>.  URLs come from
    /// <see cref="Mk8RuntimeConfig.GitRemoteUrls"/> (runtime).
    /// </summary>
    public static readonly string[] RemoteNames =
    [
        "origin", "upstream", "fork", "backup", "mirror",
    ];

    // ── Registration (called once at startup by Mk8CommandWhitelist) ──

    internal static KeyValuePair<string, string[]>[] GetWordLists() =>
    [
        new("CommitWords", CommitWords),
        new("BranchNames", BranchNames),
        new("RemoteNames", RemoteNames),
    ];

    internal static Mk8AllowedCommand[] GetCommands()
    {
        var branchSlot = new Mk8Slot("branch", Mk8SlotKind.AdminWord, WordListName: "BranchNames");
        var pathSlot = new Mk8Slot("path", Mk8SlotKind.SandboxPath);

        return
        [
            // ═════════════════════════════════════════════════════════
            // READ-ONLY — no repository state is changed
            // ═════════════════════════════════════════════════════════

            new("git version", "git", ["--version"]),

            // status
            new("git status", "git", ["status"],
                Flags: [new("--short"), new("-s"), new("--porcelain")]),

            // log (always --oneline to bound output size)
            new("git log", "git", ["log", "--oneline"],
                Flags: [
                    new("-n", new Mk8Slot("count", Mk8SlotKind.IntRange, MinValue: 1, MaxValue: 100)),
                    new("--all"),
                    new("--no-decorate"),
                ]),

            // diff (working tree)
            new("git diff", "git", ["diff"],
                Flags: [new("--staged"), new("--cached"), new("--stat"),
                        new("--name-only"), new("--name-status")]),
            new("git diff file", "git", ["diff"],
                Flags: [new("--staged"), new("--cached")],
                Params: [pathSlot]),

            // branch listing
            new("git branch", "git", ["branch"],
                Flags: [new("--list"), new("-a"), new("--all"), new("-r")]),

            // remote listing
            new("git remote", "git", ["remote"],
                Flags: [new("-v")]),

            // remote add/remove (URL MUST come from runtime config)
            new("git remote add", "git", ["remote", "add"],
                Params: [
                    new Mk8Slot("name", Mk8SlotKind.AdminWord, WordListName: "RemoteNames"),
                    new Mk8Slot("url", Mk8SlotKind.AdminWord, WordListName: "GitRemoteUrls"),
                ]),
            new("git remote remove", "git", ["remote", "remove"],
                Params: [
                    new Mk8Slot("name", Mk8SlotKind.AdminWord, WordListName: "RemoteNames"),
                ]),

            // rev-parse
            new("git rev-parse HEAD", "git", ["rev-parse", "HEAD"]),
            new("git rev-parse short HEAD", "git", ["rev-parse", "--short", "HEAD"]),

            // ls-files
            new("git ls-files", "git", ["ls-files"]),

            // tag listing
            new("git tag list", "git", ["tag", "--list"]),
            new("git tag list short", "git", ["tag", "-l"]),

            // describe
            new("git describe", "git", ["describe"],
                Flags: [new("--tags"), new("--always")]),

            // ═════════════════════════════════════════════════════════
            // WRITE — constrained to word-list values only
            // ═════════════════════════════════════════════════════════

            // add (sandbox paths only, or the fixed tokens "." / "-A")
            new("git add paths", "git", ["add"],
                Params: [new Mk8Slot("paths", Mk8SlotKind.SandboxPath, Variadic: true)]),
            new("git add all dot", "git", ["add", "."]),
            new("git add all flag", "git", ["add", "-A"]),

            // commit (message composed from vocabulary words with spaces)
            new("git commit", "git", ["commit"],
                Flags: [new("-m", new Mk8Slot("message", Mk8SlotKind.ComposedWords, WordListName: "CommitWords"))]),

            // stash
            new("git stash", "git", ["stash"]),
            new("git stash pop", "git", ["stash", "pop"]),
            new("git stash list", "git", ["stash", "list"]),
            new("git stash drop", "git", ["stash", "drop"]),

            // checkout / switch (branch MUST come from word list)
            new("git checkout branch", "git", ["checkout"],
                Params: [branchSlot]),
            new("git checkout new branch", "git", ["checkout", "-b"],
                Params: [branchSlot]),
            new("git switch branch", "git", ["switch"],
                Params: [branchSlot]),
            new("git switch new branch", "git", ["switch", "-c"],
                Params: [branchSlot]),

            // ═════════════════════════════════════════════════════════
            // NOT WHITELISTED (require dangerous-shell path):
            //   push, pull, merge, rebase, reset, clean, clone,
            //   config, submodule, am, apply, filter-branch,
            //   cherry-pick, bisect, gc, fsck, reflog
            // ═════════════════════════════════════════════════════════
        ];
    }
}
