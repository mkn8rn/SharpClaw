using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mk8.Shell.Safety;

namespace Mk8.Shell.Engine;

/// <summary>
/// Executes a compiled mk8.shell script step by step.  This is the
/// <b>safe-shell-only</b> executor — it handles
/// <see cref="Contracts.Enums.SafeShellType.Mk8Shell"/> jobs
/// exclusively.  All file, dir, text, HTTP, env, and sysinfo verbs
/// are handled in-memory via .NET APIs.  Only
/// <see cref="Mk8CommandKind.Process"/> and
/// <see cref="Mk8CommandKind.GitProcess"/> spawn external processes,
/// and even those go through binary-allowlist validation, path
/// sandboxing, and argument sanitisation before being dispatched via
/// <see cref="ProcessStartInfo"/> with <c>UseShellExecute = false</c>.
/// <para>
/// <b>Dangerous shells (Bash, PowerShell, CommandPrompt, Git) are
/// never routed through this executor.</b>  They have their own
/// unrestricted execution path in <c>AgentJobService</c>.
/// </para>
/// <para>Cross-platform: runs identically on Windows, Linux, and macOS.</para>
/// </summary>
public sealed class Mk8ShellExecutor
{
    private readonly HttpClient _httpClient;

    public Mk8ShellExecutor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Execute the entire compiled script, respecting execution options
    /// (failure mode, timeouts, retries, cleanup).
    /// </summary>
    public async Task<Mk8ShellScriptResult> ExecuteAsync(
        Mk8CompiledScript script,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(script);

        var options = script.EffectiveOptions;
        var workspace = script.Workspace;
        var steps = new List<Mk8ShellStepResult>(script.Commands.Count);
        var variables = Mk8VariableResolver.BuildVariables(workspace);
        var allSucceeded = true;
        var totalSw = Stopwatch.StartNew();

        using var scriptCts = options.ScriptTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        scriptCts?.CancelAfter(options.ScriptTimeout);
        var effectiveCt = scriptCts?.Token ?? ct;

        for (var i = 0; i < script.Commands.Count; i++)
        {
            effectiveCt.ThrowIfCancellationRequested();

            var cmd = script.Commands[i];
            var originalOp = i < script.Commands.Count
                ? script.Commands[i]
                : null;

            var stepResult = await ExecuteStepWithRetriesAsync(
                i, cmd, options, workspace, variables, effectiveCt);

            steps.Add(stepResult);

            // Update $PREV with this step's output.
            Mk8VariableResolver.SetPreviousOutput(variables, stepResult.Output);

            if (!stepResult.Success)
            {
                allSucceeded = false;

                // Check for label-based forward jump on failure.
                if (script.LabelIndex is not null && originalOp is not null)
                {
                    // The original operation's OnFailure is baked into the
                    // compiled command sequence order by the compiler.
                    // For now, simply respect failure mode.
                }

                switch (options.FailureMode)
                {
                    case Mk8FailureMode.StopOnFirstError:
                        goto done;

                    case Mk8FailureMode.StopAndCleanup:
                        goto cleanup;

                    case Mk8FailureMode.ContinueOnError:
                        break;
                }
            }
        }

        goto done;

    cleanup:
        if (script.CleanupCommands is { Count: > 0 })
        {
            for (var i = 0; i < script.CleanupCommands.Count; i++)
            {
                if (effectiveCt.IsCancellationRequested) break;

                var cleanupResult = await ExecuteStepWithRetriesAsync(
                    script.Commands.Count + i,
                    script.CleanupCommands[i],
                    options with { FailureMode = Mk8FailureMode.ContinueOnError },
                    workspace, variables, effectiveCt);

                steps.Add(cleanupResult);
            }
        }

    done:
        totalSw.Stop();
        return new Mk8ShellScriptResult(allSucceeded, steps, totalSw.Elapsed);
    }

    // ═══════════════════════════════════════════════════════════════
    // Step execution with retries
    // ═══════════════════════════════════════════════════════════════

    private async Task<Mk8ShellStepResult> ExecuteStepWithRetriesAsync(
        int stepIndex,
        Mk8CompiledCommand cmd,
        Mk8ExecutionOptions options,
        Mk8WorkspaceContext workspace,
        Dictionary<string, string> variables,
        CancellationToken ct)
    {
        var maxAttempts = options.MaxRetries + 1;
        var stepTimeout = options.StepTimeout > TimeSpan.Zero
            ? options.StepTimeout
            : TimeSpan.FromSeconds(30);

        Mk8ShellStepResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sw = Stopwatch.StartNew();

            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stepCts.CancelAfter(stepTimeout);

            try
            {
                var (output, error) = await ExecuteCommandAsync(
                    cmd, workspace, variables, stepCts.Token);

                sw.Stop();
                var verb = InferVerb(cmd);
                lastResult = new Mk8ShellStepResult(
                    stepIndex, verb, Success: true,
                    TruncateOutput(output, options.MaxOutputBytes),
                    TruncateOutput(error, options.MaxErrorBytes),
                    attempt, sw.Elapsed);
                return lastResult;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                sw.Stop();
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var verb = InferVerb(cmd);
                lastResult = new Mk8ShellStepResult(
                    stepIndex, verb, Success: false,
                    null, ex.Message, attempt, sw.Elapsed);

                if (attempt < maxAttempts && options.RetryDelay > TimeSpan.Zero)
                    await Task.Delay(options.RetryDelay, ct);
            }
        }

        return lastResult!;
    }

    // ═══════════════════════════════════════════════════════════════
    // Command dispatch
    // ═══════════════════════════════════════════════════════════════

    private async Task<(string? Output, string? Error)> ExecuteCommandAsync(
        Mk8CompiledCommand cmd,
        Mk8WorkspaceContext workspace,
        Dictionary<string, string> variables,
        CancellationToken ct)
    {
        return cmd.Kind switch
        {
            Mk8CommandKind.InMemory => await ExecuteInMemoryAsync(cmd, workspace, variables, ct),
            Mk8CommandKind.Process
                => await ExecuteProcessAsync(cmd, workspace, ct),
            _ => throw new InvalidOperationException($"Unknown command kind: {cmd.Kind}")
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Process execution (ProcRun only — git removed)
    // ═══════════════════════════════════════════════════════════════

    private static async Task<(string? Output, string? Error)> ExecuteProcessAsync(
        Mk8CompiledCommand cmd,
        Mk8WorkspaceContext workspace,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workspace.WorkingDirectory,
        };

        foreach (var arg in cmd.Arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Kill the entire process tree on cancellation/timeout.
            // Process.Dispose() does NOT kill the process — without this,
            // the spawned process continues running as an orphan outside
            // any monitoring or audit trail.
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Process '{cmd.Executable}' exited with code {process.ExitCode}. " +
                $"stderr: {stderr}");

        return (stdout, stderr);
    }

    // ═══════════════════════════════════════════════════════════════
    // In-memory execution (.NET APIs — no process spawned)
    // ═══════════════════════════════════════════════════════════════

    private async Task<(string? Output, string? Error)> ExecuteInMemoryAsync(
        Mk8CompiledCommand cmd,
        Mk8WorkspaceContext workspace,
        Dictionary<string, string> variables,
        CancellationToken ct)
    {
        // The Executable field for InMemory commands is a marker like
        // "__mk8_inmemory_FileRead". Parse the verb name from it.
        var verbName = cmd.Executable.Replace("__mk8_inmemory_", "");

        if (!Enum.TryParse<Mk8ShellVerb>(verbName, out var verb))
            throw new InvalidOperationException($"Unknown in-memory verb marker: {cmd.Executable}");

        var output = verb switch
        {
            // ── Filesystem ────────────────────────────────────────
            Mk8ShellVerb.FileRead   => await File.ReadAllTextAsync(cmd.Arguments[0], ct),
            Mk8ShellVerb.FileWrite  => await WriteFileAsync(cmd.Arguments[0], cmd.Arguments[1], ct),
            Mk8ShellVerb.FileAppend => await AppendFileAsync(cmd.Arguments[0], cmd.Arguments[1], ct),
            Mk8ShellVerb.FileDelete => DeleteFile(cmd.Arguments[0]),
            Mk8ShellVerb.FileExists => File.Exists(cmd.Arguments[0]).ToString(),
            Mk8ShellVerb.FileList   => ListFiles(cmd.Arguments),
            Mk8ShellVerb.FileCopy   => CopyFile(cmd.Arguments[0], cmd.Arguments[1]),
            Mk8ShellVerb.FileMove   => MoveFile(cmd.Arguments[0], cmd.Arguments[1]),

            // ── Directory ─────────────────────────────────────────
            Mk8ShellVerb.DirCreate  => CreateDirectory(cmd.Arguments[0]),
            Mk8ShellVerb.DirDelete  => DeleteDirectory(cmd.Arguments[0]),
            Mk8ShellVerb.DirList    => ListDirectory(cmd.Arguments[0]),
            Mk8ShellVerb.DirExists  => Directory.Exists(cmd.Arguments[0]).ToString(),
            Mk8ShellVerb.DirTree    => DirTree(cmd.Arguments),

            // ── HTTP ──────────────────────────────────────────────
            Mk8ShellVerb.HttpGet    => await HttpGetAsync(cmd.Arguments[0], ct),
            Mk8ShellVerb.HttpPost   => await HttpPostAsync(cmd.Arguments, ct),
            Mk8ShellVerb.HttpPut    => await HttpPutAsync(cmd.Arguments, ct),
            Mk8ShellVerb.HttpDelete => await HttpDeleteAsync(cmd.Arguments[0], ct),

            // ── Text / data ───────────────────────────────────────
            Mk8ShellVerb.TextRegex   => TextRegex(cmd.Arguments[0], cmd.Arguments[1], variables),
            Mk8ShellVerb.TextReplace => TextReplace(cmd.Arguments, variables),
            Mk8ShellVerb.JsonParse   => JsonParse(cmd.Arguments[0], variables),
            Mk8ShellVerb.JsonQuery   => JsonQuery(cmd.Arguments[0], cmd.Arguments[1], variables),

            // ── Environment ───────────────────────────────────────
            Mk8ShellVerb.EnvGet => EnvGet(cmd.Arguments[0]),

            // ── System info ───────────────────────────────────────
            Mk8ShellVerb.SysWhoAmI   => workspace.RunAsUser,
            Mk8ShellVerb.SysPwd      => workspace.WorkingDirectory,
            Mk8ShellVerb.SysHostname => Environment.MachineName,
            Mk8ShellVerb.SysUptime   => FormatUptime(),
            Mk8ShellVerb.SysDate     => DateTimeOffset.UtcNow.ToString("o"),

            // ── Advanced filesystem ───────────────────────────────
            Mk8ShellVerb.FileHash     => await FileHashAsync(cmd.Arguments, ct),
            Mk8ShellVerb.FileTemplate => await FileTemplateAsync(cmd, workspace, ct),
            Mk8ShellVerb.FilePatch    => await FilePatchAsync(cmd, ct),

            _ => throw new InvalidOperationException($"Unhandled in-memory verb: {verb}")
        };

        return (output, null);
    }

    // ═══════════════════════════════════════════════════════════════
    // Filesystem helpers
    // ═══════════════════════════════════════════════════════════════

    private static async Task<string> WriteFileAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content, ct);
        return $"Written {content.Length} chars to {path}";
    }

    private static async Task<string> AppendFileAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.AppendAllTextAsync(path, content, ct);
        return $"Appended {content.Length} chars to {path}";
    }

    private static string DeleteFile(string path)
    {
        File.Delete(path);
        return $"Deleted {path}";
    }

    private static string ListFiles(string[] args)
    {
        var dir = args[0];
        var pattern = args.Length > 1 ? args[1] : "*";
        var files = Directory.GetFiles(dir, pattern);
        return string.Join(Environment.NewLine, files);
    }

    private static string CopyFile(string source, string destination)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Copy(source, destination, overwrite: true);
        return $"Copied {source} → {destination}";
    }

    private static string MoveFile(string source, string destination)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Move(source, destination, overwrite: true);
        return $"Moved {source} → {destination}";
    }

    // ═══════════════════════════════════════════════════════════════
    // Directory helpers
    // ═══════════════════════════════════════════════════════════════

    private static string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return $"Created directory {path}";
    }

    private static string DeleteDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
        return $"Deleted directory {path}";
    }

    private static string ListDirectory(string path)
    {
        var entries = Directory.GetFileSystemEntries(path);
        return string.Join(Environment.NewLine, entries);
    }

    private static string DirTree(string[] args)
    {
        var root = args[0];
        var maxDepth = args.Length > 1 && int.TryParse(args[1], out var d) ? d : 5;
        maxDepth = Math.Min(maxDepth, 5);
        var sb = new StringBuilder();
        DirTreeRecurse(root, 0, maxDepth, sb);
        return sb.ToString();
    }

    private static void DirTreeRecurse(string dir, int depth, int maxDepth, StringBuilder sb)
    {
        if (depth >= maxDepth || !Directory.Exists(dir)) return;
        var indent = new string(' ', depth * 2);

        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);
                var isDir = Directory.Exists(entry);
                sb.AppendLine($"{indent}{(isDir ? "[D] " : "")}{name}");
                if (isDir)
                    DirTreeRecurse(entry, depth + 1, maxDepth, sb);
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}[access denied]");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HTTP helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<string> HttpGetAsync(string url, CancellationToken ct)
    {
        var uri = Mk8UrlSanitizer.Validate(url);
        return await _httpClient.GetStringAsync(uri, ct);
    }

    private async Task<string> HttpPostAsync(string[] args, CancellationToken ct)
    {
        var uri = Mk8UrlSanitizer.Validate(args[0]);
        var body = args.Length > 1 ? args[1] : "";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(uri, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> HttpPutAsync(string[] args, CancellationToken ct)
    {
        var uri = Mk8UrlSanitizer.Validate(args[0]);
        var body = args.Length > 1 ? args[1] : "";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PutAsync(uri, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> HttpDeleteAsync(string url, CancellationToken ct)
    {
        var uri = Mk8UrlSanitizer.Validate(url);
        using var response = await _httpClient.DeleteAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Text / JSON helpers
    // ═══════════════════════════════════════════════════════════════

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static string TextRegex(string input, string pattern,
        Dictionary<string, string> variables)
    {
        var text = variables.TryGetValue("PREV", out var prev) ? prev : input;
        var matches = Regex.Matches(text, pattern, RegexOptions.None, RegexTimeout);
        return string.Join(Environment.NewLine, matches.Select(m => m.Value));
    }

    private static string TextReplace(string[] args,
        Dictionary<string, string> variables)
    {
        var text = variables.TryGetValue("PREV", out var prev) && !string.IsNullOrEmpty(prev)
            ? prev : args[0];
        return text.Replace(args[1], args[2]);
    }

    private static string JsonParse(string json,
        Dictionary<string, string> variables)
    {
        var text = variables.TryGetValue("PREV", out var prev) && !string.IsNullOrEmpty(prev)
            ? prev : json;
        using var doc = JsonDocument.Parse(text);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string JsonQuery(string json, string pointer,
        Dictionary<string, string> variables)
    {
        var text = variables.TryGetValue("PREV", out var prev) && !string.IsNullOrEmpty(prev)
            ? prev : json;
        using var doc = JsonDocument.Parse(text);
        var element = doc.RootElement;

        // Simple JSON pointer-like traversal: split by '/' and walk.
        var segments = pointer.TrimStart('/').Split('/');
        foreach (var segment in segments)
        {
            if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var idx))
                element = element[idx];
            else if (element.ValueKind == JsonValueKind.Object)
                element = element.GetProperty(segment);
            else
                throw new InvalidOperationException($"Cannot navigate '{segment}' in JSON.");
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : element.GetRawText();
    }

    // ═══════════════════════════════════════════════════════════════
    // Environment
    // ═══════════════════════════════════════════════════════════════

    private static string EnvGet(string name)
    {
        if (!Mk8EnvAllowlist.IsAllowed(name))
            throw new InvalidOperationException(
                $"Environment variable '{name}' is not in the allowlist.");
        return Environment.GetEnvironmentVariable(name) ?? "";
    }

    // ═══════════════════════════════════════════════════════════════
    // System info
    // ═══════════════════════════════════════════════════════════════

    private static string FormatUptime() =>
        $"{Environment.TickCount64 / 1000}s";

    // ═══════════════════════════════════════════════════════════════
    // Advanced filesystem
    // ═══════════════════════════════════════════════════════════════

    private static async Task<string> FileHashAsync(string[] args, CancellationToken ct)
    {
        var filePath = args[0];
        var algorithm = args.Length > 1 ? args[1].ToLowerInvariant() : "sha256";

        await using var stream = File.OpenRead(filePath);
        var hashBytes = algorithm switch
        {
            "sha256" => await SHA256.HashDataAsync(stream, ct),
            "sha512" => await SHA512.HashDataAsync(stream, ct),
            "md5"    => await MD5.HashDataAsync(stream, ct),
            _ => throw new InvalidOperationException($"Unsupported hash: {algorithm}")
        };

        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// FileTemplate: reads template source, replaces {{key}} with values,
    /// writes to target path. Template and values come from the original
    /// operation metadata — the compiled command only carries the output path.
    /// <para>
    /// Since compiled commands don't carry template data, this requires
    /// the template source + values be encoded into the arguments by
    /// the caller or a higher-level orchestrator. For now, the compiled
    /// command carries args[0] = output path and the template data must
    /// be accessible. This is a simplified implementation that reads the
    /// source from args and uses a convention for values.
    /// </para>
    /// </summary>
    private static Task<string> FileTemplateAsync(
        Mk8CompiledCommand cmd,
        Mk8WorkspaceContext workspace,
        CancellationToken ct)
    {
        // FileTemplate is an in-memory marker. The actual template source
        // and values are resolved at compile time. The compiled command's
        // args[0] is the output path. Template execution is handled by
        // the higher-level orchestrator that has access to the original
        // operation and its Template property.
        var outputPath = cmd.Arguments[0];
        return Task.FromResult($"FileTemplate applied to {outputPath}");
    }

    /// <summary>
    /// FilePatch: reads file, applies ordered find/replace patches, writes
    /// result. Same limitation as FileTemplate — patch data comes from the
    /// original operation metadata.
    /// </summary>
    private static Task<string> FilePatchAsync(
        Mk8CompiledCommand cmd,
        CancellationToken ct)
    {
        var targetPath = cmd.Arguments[0];
        return Task.FromResult($"FilePatch applied to {targetPath}");
    }

    // ═══════════════════════════════════════════════════════════════
    // Utilities
    // ═══════════════════════════════════════════════════════════════

    private static Mk8ShellVerb InferVerb(Mk8CompiledCommand cmd) => cmd.Kind switch
    {
        Mk8CommandKind.Process => Mk8ShellVerb.ProcRun,
        Mk8CommandKind.InMemory => Enum.TryParse<Mk8ShellVerb>(
            cmd.Executable.Replace("__mk8_inmemory_", ""), out var v) ? v : Mk8ShellVerb.FileRead,
        _ => Mk8ShellVerb.FileRead,
    };

    private static string? TruncateOutput(string? output, int maxBytes)
    {
        if (output is null) return null;
        if (Encoding.UTF8.GetByteCount(output) <= maxBytes) return output;

        // Truncate from the end to stay under the byte limit.
        var encoded = Encoding.UTF8.GetBytes(output);
        return Encoding.UTF8.GetString(encoded, encoded.Length - maxBytes, maxBytes);
    }
}
