namespace Mk8.Shell;

/// <summary>
/// The closed set of mk8.shell verbs. Every agent command must map to
/// exactly one verb — there is no generic "execute" or "shell" verb.
/// </summary>
public enum Mk8ShellVerb
{
    // ── Filesystem ────────────────────────────────────────────────
    FileRead,
    FileWrite,
    FileAppend,
    FileDelete,
    FileExists,
    FileList,
    FileCopy,
    FileMove,

    // ── Batch filesystem ──────────────────────────────────────────
    FileWriteMany,
    FileCopyMany,
    FileDeleteMany,

    // ── Directory ─────────────────────────────────────────────────
    DirCreate,
    DirDelete,
    DirList,
    DirExists,

    // ── Process (allowlisted binaries only) ───────────────────────
    ProcRun,

    // ── Git (dedicated verbs so args are tightly scoped) ──────────
    GitStatus,
    GitLog,
    GitDiff,
    GitAdd,
    GitCommit,
    GitPush,
    GitPull,
    GitClone,
    GitCheckout,
    GitBranch,

    // ── HTTP ──────────────────────────────────────────────────────
    HttpGet,
    HttpPost,
    HttpPut,
    HttpDelete,

    // ── Text / data manipulation ─────────────────────────────────
    TextRegex,
    TextReplace,
    JsonParse,
    JsonQuery,

    // ── Environment (read-only allowlist) ─────────────────────────
    EnvGet,

    // ── System info (read-only) ──────────────────────────────────
    SysWhoAmI,
    SysPwd,
    SysHostname,
    SysUptime,
    SysDate,

    // ── Filesystem — advanced ─────────────────────────────────────

    /// <summary>
    /// Reads a template file, replaces <c>{{key}}</c> placeholders with
    /// provided values, and writes the result. No eval, no expression
    /// language — just literal string replacement with a closed key set.
    /// </summary>
    FileTemplate,

    /// <summary>
    /// Applies ordered find/replace patches to a file. Each patch is a
    /// literal string match — no regex. Reads the file, applies patches
    /// in order, writes the result atomically.
    /// </summary>
    FilePatch,

    /// <summary>
    /// Recursive directory listing up to a bounded depth (max 5).
    /// Read-only, in-memory. No glob — just a bounded tree walk.
    /// </summary>
    DirTree,

    /// <summary>
    /// Computes a cryptographic hash of a file. In-memory via
    /// <c>System.Security.Cryptography</c>. Read-only, no external process.
    /// Supported algorithms: sha256, sha512, md5.
    /// </summary>
    FileHash,

    // ── Script composition (compile-time inlining) ────────────────

    /// <summary>
    /// References an admin-approved script fragment by ID. Resolved at
    /// expand time from a server-side registry — agents cannot define
    /// fragments, only reference them. Expansion depth is counted and
    /// total operation cap still applies.
    /// </summary>
    Include,

    // ── Control flow (limited, compile-time expansion) ────────────
    /// <summary>
    /// Expands a template operation over an item list at compile time.
    /// Not a real "loop" — the compiler unrolls it into N concrete
    /// operations before execution. The agent cannot create unbounded
    /// iteration; <see cref="Mk8ForEach.MaxExpansion"/> caps it.
    /// </summary>
    ForEach,

    /// <summary>
    /// Conditional guard — only compiles the inner operation if a
    /// compile-time predicate is true. No branching at runtime.
    /// </summary>
    If,
}
