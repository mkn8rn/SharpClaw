# mk8.shell — Agent Command Language

## Overview

mk8.shell is a restricted pseudocode shell language that agents use to execute
system operations. By design, **there is no generic shell execution verb** — agents
can only use the closed set of verbs defined in the `Mk8ShellVerb` enum.

Scripts are compiled server-side into either in-memory .NET API calls or
tightly-validated process invocations. Agents never see or interact with a real
shell. Arguments are always passed as structured arrays — never concatenated
into a command string — eliminating the injection surface entirely.

**Only ProcRun and Git* verbs spawn external processes.** Everything else
(file I/O, HTTP, text, env, sysinfo) is executed in-memory via .NET APIs.
The compiler never emits bash, cmd, or powershell as an executable.

## Execution Model

mk8.shell runs entirely within .NET — **no terminal or shell process is
ever involved**.

The compiler (`Mk8ShellCompiler`) produces `Mk8CompiledCommand` records.
Each record carries an `Mk8CommandKind` that tells the executor how to
dispatch it:

| `Mk8CommandKind` | Verb categories | Runtime mechanism |
|---|---|---|
| `InMemory` | File, Dir, HTTP, Text, Env, Sys, FileTemplate, FilePatch, FileHash, DirTree | .NET APIs directly (`File.ReadAllTextAsync`, `HttpClient`, `System.Security.Cryptography`, etc.) — no process spawned |
| `Process` | ProcRun | `System.Diagnostics.Process` with `UseShellExecute = false`, args via `ArgumentList` |
| `GitProcess` | Git* | Same mechanism as `Process` — executable is always `git`, args validated by `Mk8GitFlagValidator` |

**Why no shell?** `ProcessStartInfo.UseShellExecute = false` combined with
`ArgumentList.Add(arg)` passes each argument individually to the OS
process-creation syscall (`execve` on Linux, `CreateProcessW` on Windows).
Shell metacharacters in argument values (`; rm -rf /`, `$(cmd)`, backticks)
are treated as literal text — they are never interpreted because no shell
parses them.

## Design Principles

1. **Closed verb set** — if it's not in `Mk8ShellVerb`, it cannot be executed
2. **Structured arguments** — `string[]`, never interpolated into a shell string
3. **No shell involved** — compilation targets `ProcessStartInfo.ArgumentList`
4. **Per-verb sanitization** — each verb handler validates its own argument types
5. **Platform-agnostic input** — one script runs on Linux, Windows, or macOS
6. **Scoped paths** — paths can only descend into workspace subdirectories
7. **Compile-time expansion** — ForEach/If are unrolled before execution

## Script Format

A script is an ordered list of operations:

```json
{
  "operations": [
    { "verb": "DirList",   "args": ["$WORKSPACE/app"] },
    { "verb": "FileRead",  "args": ["$WORKSPACE/app/config.json"] },
    { "verb": "GitStatus", "args": [] },
    { "verb": "GitAdd",    "args": [".", "--dry-run"] },
    {
      "verb": "ProcRun", "args": ["dotnet", "build"],
      "maxRetries": 2,
      "stepTimeout": "00:02:00",
      "label": "build",
      "onFailure": "goto:cleanup",
      "captureAs": "BUILD_OUTPUT"
    },
    { "verb": "FileWrite", "args": ["$WORKSPACE/out.txt", "$BUILD_OUTPUT"] },
    { "verb": "DirDelete", "args": ["$WORKSPACE/tmp"], "label": "cleanup" }
  ],
  "cleanup": [
    { "verb": "DirDelete",  "args": ["$WORKSPACE/publish"] },
    { "verb": "FileDelete", "args": ["$WORKSPACE/build.lock"] }
  ],
  "options": {
    "maxRetries": 1,
    "retryDelay": "00:00:02",
    "stepTimeout": "00:00:30",
    "scriptTimeout": "00:05:00",
    "failureMode": "StopAndCleanup",
    "pipeStepOutput": true
  }
}
```

### Per-Step Fields

| Field        | Type        | Default | Description                                                     |
|--------------|-------------|--------|-----------------------------------------------------------------|
| verb         | Mk8ShellVerb| —      | Required. The operation to perform.                              |
| args         | string[]    | —      | Required. Structured arguments (never shell-concatenated).       |
| maxRetries   | int?        | null   | Per-step retry count. Overrides script-level default. Capped by SystemUser ceiling. |
| stepTimeout  | TimeSpan?   | null   | Per-step timeout. Overrides script-level default. Capped by `SystemUserDB.DefaultStepTimeoutSeconds`. |
| label        | string?     | null   | Step label for jump targets. Unique within script.              |
| onFailure    | string?     | null   | Forward jump on failure, e.g. `"goto:cleanup"`.                 |
| captureAs    | string?     | null   | Capture stdout into a named variable (e.g. `"BUILD_OUTPUT"`).   |
| forEach      | Mk8ForEach? | null   | Loop body for `ForEach` verb.                                    |
| if           | Mk8Conditional? | null | Predicate + guarded op for `If` verb.                           |
| template     | Mk8FileTemplate? | null | Template definition for `FileTemplate` verb.                   |
| patches      | Mk8PatchEntry[]? | null | Patch list for `FilePatch` verb.                               |

## Execution Options

| Option          | Type           | Default         | Description                                                  |
|-----------------|----------------|-----------------|--------------------------------------------------------------|
| maxRetries      | int            | 0               | Default per-step retry count. Per-step `maxRetries` overrides. |
| retryDelay      | TimeSpan       | 2s              | Base delay between retries (exponential back-off).           |
| stepTimeout     | TimeSpan       | 30s             | Max wall-clock time per step before kill.                    |
| scriptTimeout   | TimeSpan       | 5min            | Max wall-clock time for the entire script.                   |
| failureMode     | enum           | StopOnFirstError| `StopOnFirstError`, `ContinueOnError`, `StopAndCleanup`.    |
| maxOutputBytes  | int            | 1 MB            | Max stdout captured per step (truncated).                    |
| maxErrorBytes   | int            | 256 KB          | Max stderr captured per step (truncated).                    |
| pipeStepOutput  | bool           | false           | Makes `$PREV` available — stdout of last step.              |

### Retry Behaviour

- Steps retry on non-zero exit code only (not on compile errors).
- Per-step `maxRetries` overrides the script-level default.
- Delay doubles each attempt: `retryDelay * 2^(attempt-1)`.
- SystemUser-level `DefaultMaxRetries` acts as a **ceiling** — agents cannot
  request more retries than the administrator configured.
- SystemUser-level `DefaultStepTimeoutSeconds` acts as a **ceiling** for
  step timeout.

### Failure Modes

| Mode              | Behaviour                                              |
|-------------------|--------------------------------------------------------|
| StopOnFirstError  | Abort immediately. Remaining steps are skipped.        |
| ContinueOnError   | Log failure, continue to next step.                    |
| StopAndCleanup    | Abort, then run the `cleanup` operation list.          |

#### StopAndCleanup

When `failureMode` is `StopAndCleanup`, the script accepts a separate
`cleanup` operation list that runs after any failure. Cleanup steps execute
with `ContinueOnError` semantics (best-effort) and go through the same
compilation pipeline — same path validation, same verb restrictions, no
elevated privileges.

```json
{
  "operations": [
    { "verb": "ProcRun", "args": ["dotnet", "build"] },
    { "verb": "ProcRun", "args": ["dotnet", "publish", "-o", "$WORKSPACE/publish"] }
  ],
  "cleanup": [
    { "verb": "DirDelete",  "args": ["$WORKSPACE/publish"] },
    { "verb": "FileDelete", "args": ["$WORKSPACE/build.lock"] }
  ],
  "options": { "failureMode": "StopAndCleanup" }
}
```

Cleanup operations are compiled identically to main operations and included
in `Mk8CompiledScript.CleanupCommands`. If no `cleanup` list is provided
and a step fails, `StopAndCleanup` behaves like `StopOnFirstError`.

#### Step Labels + Goto-on-Failure

For finer-grained failure handling, individual steps can declare a `label`
and an `onFailure` forward jump:

```json
{ "verb": "ProcRun", "args": ["dotnet", "build"], "label": "build", "onFailure": "goto:deploy" },
{ "verb": "ProcRun", "args": ["dotnet", "test"] },
{ "verb": "ProcRun", "args": ["dotnet", "publish"], "label": "deploy" }
```

**Rules** (enforced by `Mk8LabelValidator` at compile time):
- Labels must be unique within a script.
- Jump targets must reference existing labels.
- Only **forward jumps** allowed — no backward jumps, preventing loops.
- The jump graph is validated as a DAG — no cycles possible.
- Label names: alphanumeric, hyphens, underscores only (max 64 chars).
- Labels are metadata — not executable. No new attack surface.

The compiled script includes a `LabelIndex` (label → step index mapping)
that the executor uses to skip ahead on failure.

### Per-Step Timeout Overrides

Per-step `stepTimeout` overrides the script-level `options.stepTimeout`.
The value is **capped** by `SystemUserDB.DefaultStepTimeoutSeconds` — agents
cannot exceed the administrator-configured ceiling.

```json
{ "verb": "ProcRun",  "args": ["dotnet", "build"],       "stepTimeout": "00:02:00" },
{ "verb": "FileRead", "args": ["$WORKSPACE/output.json"], "stepTimeout": "00:00:05" }
```

This allows slow steps (builds) to have generous timeouts while fast steps
(file reads) that hang are killed promptly.

## Workspace Context

The workspace is **built server-side at job startup** from the `SystemUserDB`
resource — the agent never supplies sandbox paths or working directories.

### Where It Loads From

```
SystemUserDB
├── Username           → $USER, ProcessStartInfo.UserName
├── SandboxRoot        → $WORKSPACE, Mk8PathSanitizer boundary
├── WorkingDirectory   → $CWD, ProcessStartInfo.WorkingDirectory
├── DefaultStepTimeoutSeconds  → ceiling for options.stepTimeout
└── DefaultMaxRetries  → ceiling for options.maxRetries
```

If `SandboxRoot` is not configured, it defaults to:
- Linux: `/home/{username}/sandbox`
- Windows: `C:\Users\{username}\sandbox`

### CLI Configuration

```
systemuser add deploy --sandbox /home/deploy/sandbox --workdir /home/deploy/sandbox/app --timeout 60 --retries 3
systemuser update deploy --sandbox /opt/deploy/workspace
```

### Variable Shortcuts

Variables are resolved at **compile time** — before any process is spawned.
They are not shell environment variables.

| Variable     | Source                           | Example value                    |
|--------------|----------------------------------|----------------------------------|
| `$WORKSPACE` | `SystemUserDB.SandboxRoot`       | `/home/deploy/sandbox`           |
| `$CWD`       | `SystemUserDB.WorkingDirectory`  | `/home/deploy/sandbox/app`       |
| `$USER`      | `SystemUserDB.Username`          | `deploy`                         |
| `$PREV`      | stdout of previous step          | `Build succeeded. 0 warnings.`   |

#### How `$PREV` Works

When `pipeStepOutput: true`:

```json
{ "verb": "ProcRun",  "args": ["dotnet", "build"] }
  → stdout: "Build succeeded.\n    0 Warning(s)\n    0 Error(s)"
  → $PREV = "Build succeeded.\n    0 Warning(s)\n    0 Error(s)"

{ "verb": "FileWrite", "args": ["$WORKSPACE/build.log", "$PREV"] }
  → writes the build output to a file
```

When `pipeStepOutput: false` (default), `$PREV` is always empty.

#### Custom Variables

Additional variables can be injected from `ContainerDB` or
`ChannelContextDB` in the future. Custom variables cannot override
built-in variables (`WORKSPACE`, `CWD`, `USER`, `PREV`).

#### Named Captures (`captureAs`)

Any step can capture its stdout into a named variable via `captureAs`.
The captured value is stored in the variable dictionary and accessible
via `$NAME` in subsequent steps:

```json
{ "verb": "ProcRun", "args": ["dotnet", "build"], "captureAs": "BUILD_OUTPUT" },
{ "verb": "ProcRun", "args": ["dotnet", "test"],  "captureAs": "TEST_OUTPUT" },
{ "verb": "FileWrite", "args": ["$WORKSPACE/report.txt", "$BUILD_OUTPUT\n$TEST_OUTPUT"] }
```

**Capture rules** (enforced by `Mk8VariableResolver.ValidateCaptures`):
- Max 16 captured variables per script (`Mk8CaptureRules.MaxCaptures`).
- Names may only contain letters, digits, and underscores.
- Cannot override built-ins: `WORKSPACE`, `CWD`, `USER`, `PREV`, `ITEM`, `INDEX`.
- Capture names must be unique within a script.
- Captured variables from process-spawning steps (ProcRun, Git*) are
  **blocked in ProcRun arguments** — same injection prevention as `$PREV`.
- Capture size is enforced by existing `maxOutputBytes`.

## Verb Reference

### Filesystem

| Verb       | Args                      | Description                          | Linux equivalent      |
|------------|---------------------------|--------------------------------------|-----------------------|
| FileRead   | `[path]`                  | Read entire file contents            | `cat <path>`          |
| FileWrite  | `[path, content]`         | Overwrite file with content          | `echo "content" > f`  |
| FileAppend | `[path, content]`         | Append content to file               | `echo "content" >> f` |
| FileDelete | `[path]`                  | Delete a file                        | `rm <path>`           |
| FileExists | `[path]`                  | Check if file exists (returns bool)  | `test -f <path>`      |
| FileList   | `[path, pattern?]`        | List files matching optional glob    | `ls <path>`           |
| FileCopy   | `[source, destination]`   | Copy a file                          | `cp <src> <dst>`      |
| FileMove   | `[source, destination]`   | Move or rename a file                | `mv <src> <dst>`      |
| FileHash   | `[path, algorithm?]`      | Compute file hash (sha256/sha512/md5)| `sha256sum <path>`    |

`FileHash` returns the hex digest. The algorithm argument defaults to
`sha256`. Executed in-memory via `System.Security.Cryptography` — no
external process, read-only, path must be in sandbox.

### Filesystem — Structured Edits

| Verb         | Args              | Description                               |
|--------------|-------------------|-------------------------------------------|
| FileTemplate | `[outputPath]`    | Write file from template + replacement values |
| FilePatch    | `[targetPath]`    | Apply ordered find/replace patches to a file  |

#### FileTemplate

Reads a template file, replaces `{{KEY}}` placeholders with literal values,
and writes the result. No eval, no expression language.

```json
{
  "verb": "FileTemplate",
  "args": ["$WORKSPACE/app/appsettings.json"],
  "template": {
    "source": "$WORKSPACE/templates/appsettings.template.json",
    "values": {
      "DB_HOST": "postgres.internal",
      "APP_PORT": "8080"
    }
  }
}
```

**Constraints:**
- Template source must be in sandbox (validated by `Mk8PathSanitizer.Resolve`).
- Output path goes through `ResolveForWrite` (extension blocking applies).
- Values are literal strings only — `$` characters in values are **blocked**
  to prevent variable injection (`{{$PREV}}` attack).
- Max 64 replacement keys (`Mk8FileTemplate.MaxKeys`).

#### FilePatch

Applies ordered find/replace patches to an existing file. Each patch is a
literal string match — no regex (use `TextRegex` separately if needed).
The verb reads the file, applies patches sequentially, writes the result.

```json
{
  "verb": "FilePatch",
  "args": ["$WORKSPACE/app/config.yaml"],
  "patches": [
    { "find": "port: 3000", "replace": "port: 8080" },
    { "find": "debug: true", "replace": "debug: false" }
  ]
}
```

**Constraints:**
- File path goes through `ResolveForWrite` (extension blocking applies).
- Patch values are literal strings — `$` characters are **blocked**.
- Max 32 patches per operation (`Mk8PatchEntry.MaxPatches`).
- `find` cannot be empty.

### Directory

| Verb      | Args              | Description                          | Linux equivalent      |
|-----------|-------------------|--------------------------------------|-----------------------|
| DirCreate | `[path]`          | Create directory (recursive)         | `mkdir -p <path>`     |
| DirDelete | `[path]`          | Delete directory recursively         | `rm -rf <path>`       |
| DirList   | `[path]`          | List immediate children              | `ls <path>`           |
| DirExists | `[path]`          | Check if directory exists            | `test -d <path>`      |
| DirTree   | `[path, depth?]`  | Recursive listing up to depth N      | `tree -L <N> <path>`  |

`DirTree` returns a structured recursive listing (one path per line,
indented). Depth defaults to 3, max 5. Read-only, in-memory, no glob.
Path must be in sandbox.

### Process (Allowlisted)

| Verb    | Args                         | Description                            |
|---------|------------------------------|----------------------------------------|
| ProcRun | `[binary, arg0, arg1, ...]`  | Run an allowlisted binary with args    |

The binary name (`args[0]`) is checked against a `HashSet<string>`.
Default allowlist: `dotnet`, `node`, `npm`, `npx`, `cargo`, `make`, `cmake`,
`jq`, `tar`, `gzip`, `gunzip`, `unzip`, `zip`, `git`, `echo`, `cat`, `head`,
`tail`, `wc`, `sort`, `uniq`, `grep`, `diff`, `sha256sum`, `md5sum`, `base64`.

**Per-binary flag restrictions** (even allowed binaries have blocked flags):

| Binary  | Blocked flags                              | Reason                        |
|---------|--------------------------------------------|-------------------------------|
| dotnet  | `run`, `script`, `exec`                    | Can execute arbitrary code    |
| node    | `-e`, `--eval`, `-p`, `--print`            | Inline JS execution           |
| npm     | `exec`, `x`                               | Arbitrary package execution   |
| npx     | `-c`                                       | Inline command                 |
| cargo   | `run`, `script`                            | Arbitrary code execution       |
| make    | `SHELL=`, `--eval`                         | Shell override / eval          |
| git     | `-c`, `--exec`, `--upload-pack`            | Config/command injection       |
| tar     | `--checkpoint-action`, `--to-command`      | Arbitrary command on extract   |

**Permanently excluded (all categories):** `bash`, `sh`, `zsh`, `fish`, `dash`,
`cmd`, `powershell`, `pwsh`, `python`, `python2`, `python3`, `pip`, `perl`,
`ruby`, `lua`, `php`, `env`, `xargs`, `nohup`, `sudo`, `su`, `doas`, `pkexec`,
`curl`, `wget`, `find`, `ssh`, `scp`, `rsync`, `nc`, `socat`, `crontab`,
`chmod`, `chown`, `systemctl`, `dd`, `strace`.

### Git

| Verb        | Args                          | Description                    | Equivalent               |
|-------------|-------------------------------|--------------------------------|--------------------------|
| GitStatus   | `[]`                          | Show working tree status       | `git status`             |
| GitLog      | `[maxCount?]`                 | Show commit log                | `git log -n <count>`    |
| GitDiff     | `[path?]`                     | Show unstaged changes          | `git diff [path]`        |
| GitAdd      | `[pathspec, flags?]`          | Stage files                    | `git add <pathspec>`     |
| GitCommit   | `[message]`                   | Commit staged changes          | `git commit -m "msg"`   |
| GitPush     | `[remote?, branch?]`          | Push to remote                 | `git push`               |
| GitPull     | `[remote?, branch?]`          | Pull from remote               | `git pull`               |
| GitClone    | `[url, dest?]`                | Clone a repository             | `git clone <url>`        |
| GitCheckout | `[branchOrPath]`              | Switch branch or restore file  | `git checkout <ref>`     |
| GitBranch   | `[name?, flags?]`             | List or create branches        | `git branch [name]`      |

Git verbs compile to `git` as the executable and are **not routed through a
shell**. Flags like `--exec`, `-c core.sshCommand`, and `--upload-pack` are
blocked to prevent command injection through git's own flag interpreter.

### HTTP

| Verb       | Args                          | Description        |
|------------|-------------------------------|--------------------|
| HttpGet    | `[url]`                       | GET request        |
| HttpPost   | `[url, body?]`                | POST request       |
| HttpPut    | `[url, body?]`                | PUT request        |
| HttpDelete | `[url]`                       | DELETE request      |

URL validation: scheme must be `https` (or `http` if opt-in). Host must not
resolve to a private/link-local IP. Port must be 80 or 443.

### Text / Data

| Verb        | Args                       | Description                   |
|-------------|----------------------------|-------------------------------|
| TextRegex   | `[input, pattern]`         | Match regex (2s timeout)      |
| TextReplace | `[input, old, new]`        | Simple string replacement     |
| JsonParse   | `[input]`                  | Validate and pretty-print     |
| JsonQuery   | `[input, jsonpath]`        | Extract value by JSONPath     |

### Environment (Read-Only)

| Verb   | Args     | Description                           |
|--------|----------|---------------------------------------|
| EnvGet | `[name]` | Read env var from allowlist only      |

Allowed: `HOME`, `USER`, `PATH`, `LANG`, `TZ`, `TERM`, `PWD`, `HOSTNAME`.
**Blocked:** anything containing `KEY`, `SECRET`, `TOKEN`, `PASSWORD`, `CONN`.

### System Info (Read-Only)

| Verb        | Args | Description              | Linux equivalent |
|-------------|------|--------------------------|------------------|
| SysWhoAmI   | `[]` | Current OS username      | `whoami`         |
| SysPwd      | `[]` | Current working directory| `pwd`            |
| SysHostname | `[]` | Machine hostname         | `hostname`       |
| SysUptime   | `[]` | System uptime            | `uptime`         |
| SysDate     | `[]` | Current UTC datetime     | `date -u`        |

## Security Model

### What Cannot Be Expressed

The following are **structurally impossible** in mk8.shell:

- `sudo` / privilege escalation — no verb exists
- Arbitrary shell execution — no `bash -c` verb
- Pipe chains — scripts are sequential, no `|`
- Shell expansion — `$()`, backticks, glob in args are literal strings
- Background processes — no `&` or `nohup`
- Redirection — no `>`, `>>`, `2>&1` (file write is a dedicated verb)
- Chaining — no `&&`, `||`, `;` (operations are a typed array)

### Compilation Safety

The compiler **never** passes arguments through a shell. On all platforms:

```
ProcessStartInfo.UseShellExecute = false
ProcessStartInfo.ArgumentList.Add(arg)  // each arg individually
```

This means shell metacharacters in argument values (`; rm -rf /`) are treated
as literal text by the OS — they are never interpreted.

### Variable Safety

`$VARIABLE` shortcuts are resolved at **compile time** by simple string
replacement — they are not passed to the process environment. This means:

- `$WORKSPACE` in an argument becomes `/home/deploy/sandbox` before the
  process is ever spawned.
- Unknown variables are left as-is (e.g. `$UNKNOWN` stays literal) so the
  executor can report a clear error.
- Agents cannot define variables that override built-ins.
- Variable values are subject to the same per-verb sanitization as any other
  argument — a `$WORKSPACE` that resolves to a path still goes through
  `Mk8PathSanitizer`.
- **`$PREV` is blocked in ProcRun arguments.** The resolver throws a
  `Mk8CompileException` if `$PREV` appears in any ProcRun arg. This prevents
  the attack: `FileRead malicious.txt` → `$PREV` = file contents →
  `ProcRun dotnet $PREV`.
- **Named captures from process steps are also blocked in ProcRun arguments.**
  If `captureAs: "BUILD_OUT"` is set on a ProcRun or Git* step, `$BUILD_OUT`
  is blocked in all subsequent ProcRun args — same injection prevention.

### Write Protection (Two-Tier Model)

Write protection uses a **two-tier** model that distinguishes between files
the agent itself could execute vs. files only humans/external tools can run.

#### Tier 1: ALLOWED to write (interpreter-blocked scripts)

These script extensions are writable because **every interpreter that could
run them is permanently blocked** in the ProcRun allowlist:

`.sh`, `.bash`, `.zsh`, `.fish`, `.csh`, `.ksh` — shells blocked
`.bat`, `.cmd`, `.ps1`, `.psm1`, `.psd1` — cmd/powershell blocked
`.py`, `.rb`, `.pl`, `.lua`, `.php` — all interpreters blocked
`.service`, `.timer`, `.cron`, `.crontab` — systemctl/crontab blocked

Use case: agent writes a deployment script for a human operator, or an
admin-level `.sh` file that a CI pipeline picks up independently.

```
FileWrite  ["$WORKSPACE/deploy.sh", "#!/bin/bash\napt update && ..."]  → OK
FileWrite  ["$WORKSPACE/setup.py", "from setuptools import ..."]       → OK
```

The agent cannot execute these because `bash`, `sh`, `python`, `python3`,
`powershell`, and all other interpreters are permanently blocked.

#### Tier 2: BLOCKED from writing (executable by OS or allowed binaries)

| Category | Extensions / Names | Why |
|---|---|---|
| Native executables | `.exe`, `.com`, `.scr`, `.msi`, `.msp`, `.dll`, `.bin`, `.run`, `.elf`, `.so`, `.dylib`, `.appimage` | OS runs directly |
| Node.js code files | `.js`, `.mjs`, `.cjs` | `node` is on the allowlist |
| Windows script host | `.jse`, `.wsf`, `.wsh`, `.msh`, `.vbs`, `.vbe` | OS can invoke via file association |
| Build config files | `Makefile`, `GNUmakefile`, `CMakeLists.txt`, `Dockerfile`, `.npmrc` | `make`/`cmake`/`npm` execute these implicitly |

##### Why these are blocked: the binary planting attack

The mk8.shell sandbox is a real directory on a real server. Other software
often shares that directory — CI runners, web servers, application runtimes,
container bind-mounts. If the agent could write native executables or
shared libraries, a **separate process** could load them without the agent
ever invoking them itself.

**Concrete scenario — DLL hijacking via shared build directory:**

```
/home/deploy/sandbox/           ← mk8.shell $WORKSPACE
├── app/
│   ├── MyApp.csproj
│   └── bin/Release/net10.0/
│       ├── MyApp.dll           ← legitimate app
│       └── Newtonsoft.Json.dll ← legitimate dependency
```

A CI pipeline (Jenkins, GitHub Actions self-hosted runner, cron job) runs
`dotnet MyApp.dll` in this directory on a schedule. If the agent could
write `.dll` files:

```
Step 1: FileWrite ["$WORKSPACE/app/bin/Release/net10.0/Newtonsoft.Json.dll",
                    "<trojanized assembly bytes>"]
        → Replaces the real dependency with a malicious one.

Step 2: (Agent does nothing — just waits.)

Step 3: CI runner executes: dotnet MyApp.dll
        → .NET runtime loads Newtonsoft.Json.dll from bin/
        → Trojanized code runs as the CI service account (often root or
          a deployment user with production credentials).
```

The agent never executed anything dangerous. It only wrote a file. The
damage happens when a completely separate process trusts the filesystem.

**Other variants of the same pattern:**

| Shared process | Agent writes | What happens |
|---|---|---|
| nginx with CGI directory in sandbox | `.exe` CGI binary | HTTP request triggers execution as `www-data` |
| Python app importing from sandbox | `.so` native extension | `import somelib` loads the `.so` at import time |
| Any Linux process with `LD_LIBRARY_PATH` including sandbox | `.so` named `libcurl.so.4` | Process loads it instead of the real library at startup |
| Docker host with bind-mount to sandbox | `.exe` in mounted volume | Another container or the host process runs it |
| Java app with classpath in sandbox | `.jar` file | JVM loads it, `static {}` initializer runs |
| Windows service monitoring a folder | `.dll` plugin | Service dynamically loads new DLLs from watched directory |

Tier 1 scripts (`.sh`, `.py`, `.ps1`) don't have this problem — no process
accidentally interprets a `.sh` file. Shell scripts require explicit
invocation (`bash script.sh`) or the execute bit (`chmod +x`, which is
permanently blocked). Native executables and shared libraries, on the
other hand, are loaded implicitly by runtimes, linkers, and OS loaders.

#### Tier 3: ProcRun code-file argument blocking

Even if a file extension is allowed for writing, ProcRun validates that
no argument points to a code file an allowed binary would interpret:

```
ProcRun ["node", "$WORKSPACE/script.js"]  → BLOCKED (code-file argument)
ProcRun ["node", "--version"]             → OK (no code file)
ProcRun ["make", "-f", "Makefile"]        → BLOCKED (dangerous config)
```

### Execution Limits

System user limits act as **ceilings** that agents cannot exceed:

```
SystemUserDB.DefaultMaxRetries = 3
  + Script requests maxRetries = 5
  → Effective: 3 (capped by system user)

SystemUserDB.DefaultStepTimeoutSeconds = 60
  + Script requests stepTimeout = 120s
  → Effective: 60s (capped by system user)
```

## Control Flow

### ForEach (compile-time unroll)

ForEach is **not a runtime loop** — it's syntactic sugar that the expander
unrolls into N concrete operations before compilation. The agent cannot
observe intermediate results between iterations.

```json
{
  "verb": "ForEach",
  "forEach": {
    "items": ["src/a.txt", "src/b.txt", "src/c.txt"],
    "body": {
      "verb": "FileCopy",
      "args": ["$WORKSPACE/$ITEM", "$WORKSPACE/backup/$ITEM"]
    }
  }
}
```

Expands to 3 FileCopy operations at compile time. Available placeholders:
- `$ITEM` — the current item string
- `$INDEX` — 0-based iteration index

**Limits:**
- Max 256 items per ForEach (`Mk8ForEach.MaxExpansion`)
- Max 1024 total operations after expansion (`Mk8ScriptExpander.MaxExpandedOperations`)
- Max nesting depth: 3
- Nested ForEach is forbidden (no `ForEach` inside `ForEach.Body`)

### If (conditional guard)

If evaluates a predicate and either includes or skips the guarded operation.
No else branch, no boolean operators, no nesting beyond depth 3.

```json
{
  "verb": "If",
  "if": {
    "predicate": { "kind": "PrevContains", "args": ["Build succeeded"] },
    "then": { "verb": "GitAdd", "args": ["."] }
  }
}
```

**Available predicates:**

| Kind          | Args               | Evaluated at | Description                              |
|---------------|--------------------|--------------|------------------------------------------|
| PrevContains  | `[substring]`      | Compile time | True if `$PREV` contains substring       |
| PrevEmpty     | `[]`               | Compile time | True if `$PREV` is empty/whitespace      |
| EnvEquals     | `[name, expected]` | Compile time | True if env var equals value (allowlist)  |
| FileExists    | `[path]`           | Runtime      | Deferred — executor checks at step time  |
| DirExists     | `[path]`           | Runtime      | Deferred — executor checks at step time  |

## Batch Operations

Batch verbs reduce script verbosity when operating on multiple files.
They expand to individual operations at compile time — same validation
applies per file.

### FileWriteMany

```json
{
  "verb": "FileWriteMany",
  "args": [
    "$WORKSPACE/src/index.ts", "export {}",
    "$WORKSPACE/src/config.ts", "export const config = {}",
    "$WORKSPACE/README.md", "# My Project"
  ]
}
```

Args are pairs: `[path1, content1, path2, content2, ...]`. Max 64 files.
Each pair expands to a `FileWrite` — executable extension blocking applies.

### FileCopyMany

```json
{
  "verb": "FileCopyMany",
  "args": [
    "$WORKSPACE/template/a.txt", "$WORKSPACE/out/a.txt",
    "$WORKSPACE/template/b.txt", "$WORKSPACE/out/b.txt"
  ]
}
```

Args are pairs: `[src1, dst1, src2, dst2, ...]`. Max 64 pairs.

### FileDeleteMany

```json
{ "verb": "FileDeleteMany", "args": ["$WORKSPACE/tmp/a.log", "$WORKSPACE/tmp/b.log"] }
```

Args are paths. Max 64 paths.

## Script Composition (Include)

The `Include` verb references admin-approved script fragments by ID.
Fragments are resolved at expand time from a server-side registry
(`IMk8FragmentRegistry`) — agents cannot define fragments, only reference
them. This is compile-time inlining, not runtime function calls.

```json
{
  "operations": [
    { "verb": "Include", "args": ["setup-workspace"] },
    { "verb": "ProcRun", "args": ["dotnet", "build"] },
    { "verb": "Include", "args": ["deploy-to-staging"] }
  ]
}
```

**Rules:**
- Agents reference fragments by ID only (e.g. `"setup-workspace"`).
- Admins register fragments via `Mk8InMemoryFragmentRegistry.Register`.
- Fragment IDs: letters, digits, hyphens, underscores, periods (max 128 chars).
- Fragments cannot contain `Include` (no nested composition).
- Expansion depth counts toward the nesting limit (max 3).
- Total expanded operation cap (1024) still applies.
- Fragments go through the same compilation pipeline — same path
  validation, same verb restrictions, same security model.

## Scoped Path Builder

`Mk8ScopedPath` constructs workspace-relative paths that can **only descend**
into subdirectories. The agent never needs to know the absolute sandbox path.

```
Mk8ScopedPath.Join("src", "components", "App.tsx")
→ "$WORKSPACE/src/components/App.tsx"

Mk8ScopedPath.JoinCwd("output", "build.log")
→ "$CWD/output/build.log"

Mk8ScopedPath.Join("..", "..", "etc", "passwd")
→ THROWS Mk8CompileException
```

**Validation rules:**
- No `..` segments (throws immediately)
- No absolute path segments
- No null bytes, control characters, or Windows reserved names
- No trailing dots (Windows device name tricks)
- Forward slashes in a segment are split into sub-segments
- The resulting `$WORKSPACE/...` path is STILL validated by
  `Mk8PathSanitizer` after variable resolution — double defence

## Compilation Pipeline

```
Agent submits Mk8ShellScript
       │
       ▼
Mk8ScriptExpander.Expand()           ← unrolls ForEach/If/Batch/Include
       │                                (Include inlines from IMk8FragmentRegistry)
       │                                (cleanup operations expanded separately)
       ▼
Mk8VariableResolver.ValidateCaptures()← validates captureAs names, detects process captures
       │
       ▼
Mk8LabelValidator.Validate()         ← checks label uniqueness, forward-only jumps, DAG
       │
       ▼
Mk8VariableResolver.ResolveArgs()    ← resolves $WORKSPACE, $CWD, $USER, $CAPTURES
       │                                ($PREV + process captures blocked for ProcRun)
       ▼
Mk8PathSanitizer.Resolve()           ← validates paths in sandbox
Mk8PathSanitizer.ResolveForWrite()   ← + blocks executable extensions
       │
       ▼
Mk8ShellCompiler.CompileOperation()  ← verb dispatch
       │
       ├─ File/Dir/HTTP/Text/Env/Sys  → InMemory marker (.NET APIs)
       ├─ FileTemplate/FilePatch       → InMemory (template/patch validation)
       ├─ FileHash/DirTree             → InMemory (algorithm/depth validation)
       ├─ ProcRun                      → Mk8BinaryAllowlist + ValidateArgs
       └─ Git*                         → Mk8GitFlagValidator
       │
       ▼
Mk8CompiledScript
├── Commands          (flat list of Mk8CompiledCommand)
├── CleanupCommands   (compiled cleanup ops, if StopAndCleanup)
└── LabelIndex        (label → step index, if labels used)
```

## Audit Log

Every compiled script can produce a complete audit trail via
`Mk8AuditLog.CreateEntries()`. Each step records what the agent
requested, what was compiled, and what actually ran:

```
Mk8ShellAuditEntry
├── JobId                              unique job identifier
├── StepIndex                          0-based step position
├── RequestedVerb + RequestedArgs      pre-compilation (agent's input)
├── CompiledExecutable + CompiledArgs  post-compilation (resolved paths, validated args)
├── ExitCode                           process exit code or 0 for in-memory ops
├── Output / Error                     captured stdout/stderr (truncated to limits)
├── StartedAt / CompletedAt            UTC timestamps
├── Attempts                           retry count (1 = no retries)
└── SandboxRoot                        workspace boundary at execution time
```

This enables:
- **Post-incident analysis:** "the agent wrote `deploy.sh` with these
  contents at 14:23, then committed it at 14:24."
- **Rate limiting:** detecting agents that issue suspiciously many write
  operations.
- **Compliance:** full traceability from agent request → compiled command →
  execution result.
