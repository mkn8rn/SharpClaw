SharpClaw Task Creation — Agent Skill Reference

Full human-readable guide: guides/Task-Creation-Guide.md
API/script reference: Tasks-documentation.md

────────────────────────────────────────
WHAT A TASK SCRIPT IS
────────────────────────────────────────
A C# class posted to the API at runtime. One public class per script.
Entry point: public async Task RunAsync(CancellationToken ct)
Public properties = parameters. Inner classes = data types.
Task name comes from [Task("name")] attribute on the class, not the class name.

Definition = stored script. Instance = one execution of a definition.

────────────────────────────────────────
REGISTERING AND RUNNING
────────────────────────────────────────
Validate (no store):   POST /tasks/validate  { sourceText }  → { isValid, diagnostics[] }
Register:              POST /tasks           { sourceText }  → TaskDefinitionResponse
Update script:         PUT  /tasks/{id}      { sourceText?, isActive? }
Delete:                DELETE /tasks/{id}
List:                  GET  /tasks
Get:                   GET  /tasks/{id}

Create instance:       POST /tasks/{id}/instances  { channelId?, parameterValues? }
Start instance:        POST /tasks/{id}/instances/{iid}/start
Stop (graceful):       POST /tasks/{id}/instances/{iid}/stop
Cancel (hard):         POST /tasks/{id}/instances/{iid}/cancel
Stream output (SSE):   GET  /tasks/{id}/instances/{iid}/stream
Get outputs:           GET  /tasks/{id}/instances/{iid}/outputs?since={datetime}

Statuses: Queued, Running, Paused, Completed, Failed, Cancelled

────────────────────────────────────────
PARAMETERS
────────────────────────────────────────
Every public property is a parameter.
No default + not nullable = required (missing → TASK201 compile error).
Nullable or has default = optional.

Supported types: string, int, long, float, double, decimal, bool, Guid,
  DateTime, DateTimeOffset, nullable variants, List<T>, task-defined inner classes.

────────────────────────────────────────
ATTRIBUTES — CLASS LEVEL
────────────────────────────────────────
[Task("name")]                          required; canonical task name
[Description("...")]                    stored in definition and agent schemas
[RequiresProvider("Name")]              preflight: provider must exist with usable key
[RequiresModelCapability("Capability")] preflight: at least one model with capability
[RequiresModel("NameOrId")]             preflight: specific model must exist
[RequiresModule("module-id")]           preflight error if module disabled
[RecommendsModule("module-id")]         preflight warning only (non-blocking)
[RequiresPlatform(TaskPlatform.X)]      restrict to host platform
[RequiresPermission("Key")]             caller must have permission/global flag
[Schedule("cron", Timezone="...")]      cron trigger
[OnEvent("EventType")]                  event bus trigger
[OnFileChanged(path, Pattern="...")]    file watcher trigger
[OnProcessStarted("name")]              process lifecycle trigger
[OnWebhook("/route")]                   HTTP webhook trigger
[OnStartup] / [OnShutdown]              app lifecycle triggers
[OnHotkey("Ctrl+Shift+R")]              hotkey trigger (requires sharpclaw_computer_use)
[OsShortcut("Label")]                   OS shortcut (requires sharpclaw_computer_use)
[OnQueryReturnsRows]                    DB trigger (requires sharpclaw_database_access)
[ConcurrencyPolicy(TriggerConcurrency.X)] SkipIfRunning | QueueNext | ForceNew

────────────────────────────────────────
ATTRIBUTES — MEMBER LEVEL
────────────────────────────────────────
[Output]            on inner class: marks structured output type (max one per script)
[ToolCall("name")]  on public method: exposed as inline agent tool during execution
[AgentOutput("x")]  on method: output hint to model; values: json, markdown, text
[ModelId]           on property: marks as model lookup for preflight capability checks
[RequiresCapability("X")]  on [ModelId] property: model must have this capability

────────────────────────────────────────
BUILT-IN STEP METHODS (inside RunAsync)
────────────────────────────────────────
Agent interaction:
  await Chat(agent, message)                 → string
  await ChatStream(agent, message)           → streams to SSE output channel
  await ChatToThread(agent, threadId, msg)   → string

Lookup / creation:
  await FindAgent(nameOrId)    await FindModel(nameOrId)    await FindProvider(nameOrId)
  await CreateAgent(name, modelId)           → agent ref
  await CreateThread(channelId, name)        → thread ref

Output and parsing:
  await Emit(value)                          → publishes to SSE; updates outputSnapshotJson
  await ParseResponse<T>(text)               → T

HTTP:
  await HttpGet(url)           await HttpPost(url, body)
  await HttpPut(url, body)     await HttpDelete(url)        → string (response body)

Control:
  await Delay(ms)              await WaitUntilStopped()     await Log(message)

Transcription (requires sharpclaw_transcription):
  await GetDefaultInputAudio()               → deviceId
  await StartTranscription(deviceId, model)  → jobId
  await StopTranscription(jobId)             → transcript string

────────────────────────────────────────
OUTPUT
────────────────────────────────────────
Declare one nested class with [Output] for structured output.
Last Emit(value) matching [Output] type persists as outputSnapshotJson on instance.
Retrieve: GET /tasks/{id}/instances/{iid}  → outputSnapshotJson field.
SSE Output events carry the emitted value in real time.

────────────────────────────────────────
PREFLIGHT
────────────────────────────────────────
GET /tasks/{id}/preflight?param.Name=value  → { isBlocked, findings[] }
findings[]: { requirementKind, severity, passed, message, parameterName? }
severity Error = blocks instance creation. Warning = advisory only.
Validate syntax only (no store): POST /tasks/validate { sourceText }

────────────────────────────────────────
TRIGGERS
────────────────────────────────────────
Enable:    POST /tasks/{id}/triggers/enable   → { enabled }
Disable:   POST /tasks/{id}/triggers/disable  → { disabled }
List sources: GET /tasks/trigger-sources      → TaskTriggerSourceResponse[]

Module-owned trigger kinds will not fire if owning module is disabled.
  sharpclaw_computer_use:  OnWindowFocused, OnHotkey, OnSystemIdle, OnProcessStarted,
                           OnDeviceConnected, OsShortcut, OnDesktopSessionChanged
  sharpclaw_database_access: OnQueryReturnsRows

OS shortcuts: POST /tasks/{id}/shortcuts/install  |  DELETE /tasks/{id}/shortcuts

────────────────────────────────────────
AGENT TOOL EXPOSURE
────────────────────────────────────────
Active definitions exposed to agents with CanInvokeTasksAsTool.
Tool name pattern: task_invoke__{task-name}
Invoking the tool creates and starts an instance automatically.
Set active: PUT /tasks/{id} { "isActive": true }

────────────────────────────────────────
PERMISSIONS
────────────────────────────────────────
CanManageTasks        create, update, delete definitions
CanExecuteTasks       start, stop, cancel instances
CanInvokeTasksAsTool  see active definitions as agent tool schemas

────────────────────────────────────────
DIAGNOSTIC CODES
────────────────────────────────────────
TASK101  parse error (syntax / unknown attribute)
TASK102  validation error (semantic / bad attribute usage)
TASK201  required parameter missing at compile time
