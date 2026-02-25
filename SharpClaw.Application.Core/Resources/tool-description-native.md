Sandboxed command execution via mk8.shell. No real shell, eval, pipes, chaining, or expansion. Arguments are structured arrays.
Script: {"operations":[{"verb":"...","args":["..."],"workingDirectory":"$WORKSPACE/subdir"}],"options":{...},"cleanup":[...]}
Per-step workingDirectory: set on any operation to override the CWD for that step. Use this for ProcRun commands that must run inside a subdirectory (e.g. git add/commit inside a cloned repo). Do NOT use flags like git -C — they are not in the template whitelist.
Variables: $WORKSPACE (sandbox root), $CWD, $USER, $PREV (when pipeStepOutput:true). Captures: "captureAs":"NAME" (max 16, blocked in ProcRun args).
Only ProcRun spawns processes (strict template whitelist). Everything else is in-memory.
Use introspection verbs to discover capabilities at runtime: Mk8Docs (full reference), Mk8Verbs (all verbs), Mk8Templates (ProcRun whitelist), Mk8Vocab/Mk8VocabList (word lists), Mk8Env (env vars), Mk8Info (runtime info), Mk8FreeText (free-text config), Mk8Blacklist (blocked patterns), Mk8Skills (skill reference).
You cannot create/manage sandboxes — tell user to register if missing.
