# Creating a SharpClaw Task

> **API/script reference:** [Tasks-documentation.md](Tasks-documentation.md)
> **Agent skill:** [Task-Creation-skill.md](Task-Creation-skill.md)

This guide walks through writing, registering, running, and debugging SharpClaw tasks
from scratch — with practical examples, trigger patterns, and output strategies.

---

## Table of contents

- [What a task is](#what-a-task-is)
- [Your first task](#your-first-task)
  - [Write the script](#write-the-script)
  - [Register it](#register-it)
  - [Run it](#run-it)
  - [Watch the output](#watch-the-output)
- [Parameters](#parameters)
- [Structured output](#structured-output)
- [Triggers](#triggers)
  - [Scheduled tasks](#scheduled-tasks)
  - [Event-driven triggers](#event-driven-triggers)
  - [Module-owned triggers](#module-owned-triggers)
- [Requirements and preflight](#requirements-and-preflight)
- [Daemon tasks](#daemon-tasks)
- [Tool-call methods](#tool-call-methods)
- [Chaining agents](#chaining-agents)
- [HTTP calls](#http-calls)
- [Transcription tasks](#transcription-tasks)
- [Agent tool exposure](#agent-tool-exposure)
- [Ideas for what to build](#ideas-for-what-to-build)
- [Debugging and troubleshooting](#debugging-and-troubleshooting)

---

## What a task is

A task is a C# script registered in SharpClaw at runtime through the API or CLI. You
don't write any application code and you don't touch the solution — you write a
single class and post it. The task system parses it, validates it, and stores the
definition. When you run it, the system compiles the definition with the supplied
parameter values and hands the plan to the orchestrator.

A task definition and a task instance are distinct things. A definition is the stored
script. An instance is one execution of it — with its own status, logs, output, and
lifecycle.

---

## Your first task

### Write the script

```csharp
[Task("hello")]
[Description("Asks an agent to say hello and logs the response.")]
public class HelloTask
{
    public string AgentName { get; set; } = "Assistant";

    public async Task RunAsync(CancellationToken ct)
    {
        var agent    = await FindAgent(AgentName);
        var response = await Chat(agent, "Say hello in one sentence.");
        await Log(response);
    }
}
```

The class name doesn't matter. The canonical name is whatever you pass to `[Task]`.

### Register it

Via CLI:

```
task add --name hello --file hello.cs
```

Via API:

```http
POST /tasks
Content-Type: application/json

{ "sourceText": "<the script text>" }
```

The response gives you the task's `id`. The definition is now stored and inactive
by default.

### Run it

```
task run hello --param AgentName=Assistant
```

Or via API — first create an instance, then start it:

```http
POST /tasks/{id}/instances
{ "parameterValues": { "AgentName": "Assistant" } }

POST /tasks/{id}/instances/{iid}/start
```

### Watch the output

```
task logs hello --instance {iid}
```

Or stream in real time:

```http
GET /tasks/{id}/instances/{iid}/stream
Accept: text/event-stream
```

Each SSE frame has a `type` field: `Output`, `Log`, `StatusChange`, `Done`.

---

## Parameters

Every public property on the class is a parameter. The name, type, and XML-doc
summary are stored in the definition and surfaced in agent tool schemas.

```csharp
[Task("report")]
public class ReportTask
{
    /// <summary>Channel to post results to.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Maximum items to return.</summary>
    public int Limit { get; set; } = 20;

    /// <summary>Optional keyword filter.</summary>
    public string? Filter { get; set; }

    public async Task RunAsync(CancellationToken ct) { ... }
}
```

- Properties **without a default and not nullable** are required at launch.
  Missing required parameters produce a `TASK201` error and block the instance.
- Nullable types (`string?`, `int?`) and properties with defaults are optional.

Supported types: `string`, `int`, `long`, `float`, `double`, `decimal`, `bool`,
`Guid`, `DateTime`, `DateTimeOffset`, nullable variants of all of the above,
`List<T>`, and inner data-type classes you define in the same script.

---

## Structured output

Declare a nested class with `[Output]` to give the task a typed output shape.
`Emit(value)` publishes the value to the SSE stream and persists the last one as
`outputSnapshotJson` on the instance.

```csharp
[Task("summarise")]
public class SummariseTask
{
    [Output]
    public class Summary
    {
        public string Title    { get; set; } = "";
        public string Body     { get; set; } = "";
        public int    WordCount { get; set; }
    }

    public string SourceText { get; set; } = "";

    public async Task RunAsync(CancellationToken ct)
    {
        var agent  = await FindAgent("Summariser");
        var raw    = await Chat(agent, $"Summarise this:\n{SourceText}");
        var result = await ParseResponse<Summary>(raw);
        await Emit(result);
    }
}
```

Retrieve the final snapshot via `GET /tasks/{id}/instances/{iid}` —
`outputSnapshotJson` holds the last emitted value.

---

## Triggers

### Scheduled tasks

Register a cron trigger on the class and SharpClaw will fire instances automatically:

```csharp
[Task("daily-digest")]
[Schedule("0 8 * * *", Timezone = "Europe/London")]
[Description("Sends a morning summary every day at 8 AM.")]
public class DailyDigestTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var agent    = await FindAgent("News");
        var response = await Chat(agent, "Summarise today's priorities.");
        await Log(response);
    }
}
```

Enable the trigger after registering the definition:

```
task triggers enable {id}
```

Or via API:

```http
POST /tasks/{id}/triggers/enable
```

The `ConcurrencyPolicy` attribute controls what happens when a scheduled fire
arrives while a prior instance is still running:

```csharp
[ConcurrencyPolicy(TriggerConcurrency.SkipIfRunning)]
```

Options: `SkipIfRunning`, `QueueNext`, `ForceNew`.

### Event-driven triggers

```csharp
[Task("on-file-arrive")]
[OnFileChanged("C:\\Inbox", Pattern = "*.pdf")]
[ConcurrencyPolicy(TriggerConcurrency.QueueNext)]
public class OnFileArriveTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var agent    = await FindAgent("Processor");
        var response = await Chat(agent, "A new PDF arrived in the inbox.");
        await Log(response);
    }
}
```

Other useful event triggers:

| Attribute | Fires when |
|-----------|------------|
| `[OnEvent("EventType")]` | A SharpClaw event bus event fires |
| `[OnStartup]` | Application starts |
| `[OnShutdown]` | Application shuts down gracefully |
| `[OnWebhook("/route")]` | An HTTP POST hits the given route |

### Module-owned triggers

Some triggers are provided by modules rather than the core task host. The task
definition saves and validates normally, but the trigger only becomes active when
the owning module is enabled.

```csharp
[Task("on-hotkey")]
[OnHotkey("Ctrl+Shift+R")]
public class OnHotkeyTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var agent    = await FindAgent("Assistant");
        var response = await Chat(agent, "The user pressed Ctrl+Shift+R.");
        await Log(response);
    }
}
```

Module-owned triggers and their owners:

| Trigger attributes | Required module |
|---|---|
| `[OnHotkey]`, `[OnWindowFocused]`, `[OnSystemIdle]`, `[OnProcessStarted]`, `[OnDeviceConnected]`, `[OsShortcut]` | `sharpclaw_computer_use` |
| `[OnQueryReturnsRows]` | `sharpclaw_database_access` |

If the owning module is disabled, preflight will recommend enabling it and the
trigger will not fire. Check: `task trigger-sources` or `GET /tasks/trigger-sources`.

Install an OS shortcut for a task with `[OsShortcut]` enabled:

```
task shortcuts install {id}
```

---

## Requirements and preflight

Declare runtime prerequisites on the class so that misconfiguration is caught
before a broken instance is created:

```csharp
[Task("vision-caption")]
[RequiresProvider("OpenAI")]
[RequiresModelCapability("Vision")]
[RequiresModule("sharpclaw_computer_use")]
public class VisionCaptionTask
{
    [ModelId]
    [RequiresCapability("Vision")]
    public string Model { get; set; } = "gpt-4.1";

    public async Task RunAsync(CancellationToken ct) { ... }
}
```

Run a preflight check before launching — it tells you exactly what's missing:

```
task preflight {id} --param Model=gpt-4.1
```

Via API:

```http
GET /tasks/{id}/preflight?param.Model=gpt-4.1
```

The response includes `isBlocked` and a list of `findings` with severity and
message for each requirement. `Error` findings block instance creation.
`Warning` findings (e.g. `[RecommendsModule]`) are advisory only.

---

## Daemon tasks

A task that should run indefinitely calls `WaitUntilStopped()`. The orchestrator
blocks there until the instance is cancelled or stopped:

```csharp
[Task("watcher")]
[OnProcessStarted("notepad")]
[ConcurrencyPolicy(TriggerConcurrency.SkipIfRunning)]
public class WatcherTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        await Log("Watcher started.");
        await WaitUntilStopped();
        await Log("Watcher stopped.");
    }
}
```

Stop gracefully (signals the orchestrator to stop after the current step):

```
task stop {id} {iid}
```

Cancel immediately (signals the `CancellationToken`):

```
task cancel {id} {iid}
```

---

## Tool-call methods

A `[ToolCall]` method is exposed to agents as an inline tool while the task
instance is running. Agents can call it by name during any chat that has access
to the running instance:

```csharp
[Task("monitor")]
public class MonitorTask
{
    private bool _healthy = true;

    [ToolCall("get_health")]
    [AgentOutput("json")]
    public async Task<string> GetHealthAsync()
    {
        return _healthy ? """{"healthy":true}""" : """{"healthy":false}""";
    }

    [ToolCall("set_unhealthy")]
    public async Task SetUnhealthyAsync()
    {
        _healthy = false;
        await Log("Health set to false by agent.");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await WaitUntilStopped();
    }
}
```

`[AgentOutput("json")]` hints to the agent that the return value is JSON.
Other values: `"markdown"`, `"text"`.

---

## Chaining agents

Tasks can orchestrate multi-agent pipelines by calling multiple agents in sequence
or by routing output from one into the next:

```csharp
[Task("research-and-report")]
public class ResearchAndReportTask
{
    public string Topic { get; set; } = "";
    public Guid   ChannelId { get; set; }

    public async Task RunAsync(CancellationToken ct)
    {
        var researcher = await FindAgent("Researcher");
        var writer     = await FindAgent("Writer");

        var notes   = await Chat(researcher, $"Research: {Topic}");
        var report  = await Chat(writer, $"Write a short report based on:\n{notes}");
        var thread  = await CreateThread(ChannelId, $"Report: {Topic}");

        await ChatToThread(writer, thread, report);
        await Emit(report);
    }
}
```

---

## HTTP calls

Tasks can make outbound HTTP calls directly — useful for webhook integrations,
external API polling, or data ingestion:

```csharp
[Task("fetch-and-summarise")]
public class FetchAndSummariseTask
{
    public string Url { get; set; } = "";

    public async Task RunAsync(CancellationToken ct)
    {
        var html    = await HttpGet(Url);
        var agent   = await FindAgent("Summariser");
        var summary = await Chat(agent, $"Summarise this page content:\n{html}");
        await Emit(summary);
    }
}
```

Available: `HttpGet(url)`, `HttpPost(url, body)`, `HttpPut(url, body)`,
`HttpDelete(url)`. All return the response body as a string.

---

## Transcription tasks

Tasks can start and stop live transcription sessions using the Transcription module.
Require it explicitly so preflight catches a missing module early:

```csharp
[Task("live-notes")]
[RequiresModule("sharpclaw_transcription")]
public class LiveNotesTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var deviceId = await GetDefaultInputAudio();
        var model    = await FindModel("whisper-1");
        var jobId    = await StartTranscription(deviceId, model);

        await Delay(60_000);  // transcribe for 60 seconds

        var transcript = await StopTranscription(jobId);
        var agent      = await FindAgent("Notetaker");
        var notes      = await Chat(agent, $"Tidy up these notes:\n{transcript}");
        await Emit(notes);
    }
}
```

---

## Agent tool exposure

Active task definitions are automatically exposed as tools to agents that have
the `CanInvokeTasksAsTool` permission. The tool name is derived from the task name:

- Task named `summarise` → tool `task_invoke__summarise`
- Task named `fetch-and-report` → tool `task_invoke__fetch-and-report`

When an agent invokes the tool, an instance is created and started automatically.
The agent receives the output snapshot when the instance completes.

Activate a definition so it appears in agent tool schemas:

```
task update {id} --active true
```

---

## Ideas for what to build

- **Daily briefing** — `[Schedule("0 8 * * *")]`, chain a research agent and a
  writer agent, `[Output]` a structured briefing object.
- **Code review trigger** — `[OnFileChanged("src/", Pattern = "*.cs")]`, send
  changed files to a code-review agent, post results to a channel thread.
- **Meeting transcriber** — `[OsShortcut("Start Meeting Notes")]`, start transcription
  on hotkey, stop on second press, pipe transcript to a notetaker agent.
- **Price monitor** — `[Schedule("*/5 * * * *")]`, `HttpGet` a pricing API, compare
  to a stored threshold, `[OnTrigger]` a downstream alert task when it crosses.
- **Changelog generator** — `[OnEvent("DeployCompleted")]`, `HttpGet` a git diff URL,
  send to a writer agent, `Emit` a formatted changelog.
- **Multi-model comparison** — accept a `prompt` parameter, fan out `Chat` calls to
  three different agents backed by different models, `Emit` all three responses.
- **Inbox processor** — `[OnFileChanged("inbox/")]`, classify each file with an
  agent, route to one of three output folders based on the classification result.

---

## Debugging and troubleshooting

**Validation error on register — `TASK101` / `TASK102`**
The script has a syntax or attribute error. The `diagnostics[]` array in the
`POST /tasks` response lists the exact line and message.

**Instance stuck in `Queued`**
The orchestrator queue may be busy. Check `GET /tasks/{id}/instances/{iid}` for
`errorMessage`. If the instance is blocked by a failed preflight, the error message
says so.

**Instance status is `Failed`**
Check the instance `logs[]` array — step-level errors are logged there. Also
check `errorMessage` on the instance for the top-level failure reason.

**Required parameter not found — `TASK201`**
A required parameter was not supplied in `parameterValues`. Check the definition's
`parameters[]` for required fields (`isRequired: true`).

**Trigger never fires**
1. Check `GET /tasks/{id}` — confirm `triggers[].isEnabled` is `true`.
2. If the trigger is module-owned, confirm the owning module is enabled:
   `task trigger-sources` then `module get <id>`.
3. For scheduled tasks, verify the cron expression with a cron parser — SharpClaw
   uses standard 5-field cron (minute hour day month weekday).

**Preflight blocks with a module warning**
`[RecommendsModule]` is advisory — it won't block instance creation. If you see
an `Error`-severity module finding, you used `[RequiresModule]` and the module is
disabled. Enable it with `module enable <id>`.

**`ParseResponse<T>` returns null or wrong shape**
The agent's raw response didn't match the expected JSON shape. Use
`[AgentOutput("json")]` on the calling method to hint to the agent, and include
the target schema in the prompt to the agent.

**Output snapshot is empty**
`Emit(value)` was never called, or it was called with a type that doesn't match the
`[Output]` class. Check that the inner class carries `[Output]` and that `Emit` is
called at least once before the task ends.

**Daemon task exits immediately**
`WaitUntilStopped()` was not called, or there is an unhandled exception before it.
Check instance logs — exceptions from `RunAsync` surface in `logs[]`.
