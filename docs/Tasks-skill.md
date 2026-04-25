SharpClaw Tasks — Agent Skill Reference

Full human-readable reference: Tasks-documentation.md
All bodies JSON. Enums as strings. Timestamps ISO 8601.

────────────────────────────────────────
TASK DEFINITIONS
────────────────────────────────────────
POST   /tasks/validate                 { sourceText }  → { isValid, diagnostics[] }
POST   /tasks                          { sourceText }  → TaskDefinitionResponse
GET    /tasks                          → TaskDefinitionResponse[]
GET    /tasks/{id}                     → TaskDefinitionResponse
PUT    /tasks/{id}                     { sourceText?, isActive? }  → TaskDefinitionResponse
DELETE /tasks/{id}                     → 204
GET    /tasks/{id}/preflight?param.Name=value  → TaskPreflightResponse
GET    /tasks/trigger-sources          → TaskTriggerSourceResponse[]
POST   /tasks/{id}/triggers/enable     → { enabled }
POST   /tasks/{id}/triggers/disable    → { disabled }
POST   /tasks/{id}/shortcuts/install   → 204
DELETE /tasks/{id}/shortcuts           → 204

TaskDefinitionResponse fields:
  id, name, description?, outputTypeName?, isActive, createdAt, updatedAt, customId?,
  parameters[]: { name, typeName, description?, defaultValue?, isRequired }
  requirements[]: { kind, severity, value?, capabilityValue?, parameterName? }
  triggers[]: { kind, triggerValue?, filter?, isEnabled }

TaskPreflightResponse fields:
  isBlocked, findings[]: { requirementKind, severity, passed, message, parameterName? }

TaskTriggerSourceResponse fields:
  sourceName?, supportedKinds[], type, isCustom

────────────────────────────────────────
TASK INSTANCES
────────────────────────────────────────
POST   /tasks/{id}/instances           { channelId?, parameterValues? }  → TaskInstanceResponse
GET    /tasks/{id}/instances           → TaskInstanceResponse[]
GET    /tasks/{id}/instances/{iid}     → TaskInstanceResponse (channelCost populated if bound to channel)
POST   /tasks/{id}/instances/{iid}/start    → 204  (Queued → Running, or Paused → Running)
POST   /tasks/{id}/instances/{iid}/cancel   → 204  (hard cancel via CancellationToken)
POST   /tasks/{id}/instances/{iid}/stop     → 204  (graceful stop via orchestrator)
GET    /tasks/{id}/instances/{iid}/outputs?since={datetime}  → TaskOutputEntryResponse[]
GET    /tasks/{id}/instances/{iid}/stream   → SSE text/event-stream

Statuses: Queued, Running, Paused, Completed, Failed, Cancelled

TaskInstanceResponse fields:
  id, taskDefinitionId, taskName, status, outputSnapshotJson?, errorMessage?,
  logs[]: { message, level, timestamp },
  createdAt, startedAt?, completedAt?, channelId?, channelCost?

TaskOutputEntryResponse fields:
  id, sequence, data?, timestamp

────────────────────────────────────────
SSE STREAMING
────────────────────────────────────────
Connect: GET /tasks/{id}/instances/{iid}/stream
Each frame: data:{type, sequence?, timestamp, data?}

Event types:
  Output       — Emit or ChatStream produced output
  Log          — Log step wrote a message
  StatusChange — Instance status changed
  Done         — Terminal status reached; payload is final TaskInstanceResponse

────────────────────────────────────────
PERMISSIONS
────────────────────────────────────────
CanManageTasks         — create, update, delete definitions
CanExecuteTasks        — start, stop, pause, resume instances
CanInvokeTasksAsTool   — see active definitions as agent tool schemas

────────────────────────────────────────
AGENT TOOL EXPOSURE
────────────────────────────────────────
Active task definitions are exposed as tools to agents with CanInvokeTasksAsTool.
Tool name pattern: task_invoke__{task-name}
Example: task registered as "summarise" → tool task_invoke__summarise
Parameters are mapped to JSON Schema from the definition's parameter list.
Invoking the tool creates and starts an instance automatically.

────────────────────────────────────────
SCRIPT LANGUAGE — QUICK REFERENCE
────────────────────────────────────────
One public class per script. Entry point: public async Task RunAsync(CancellationToken ct).
Public properties = parameters. Inner classes = data types.

Attributes:
  [Task("name")]               — required; registers the task name
  [Description("...")]        — optional; stored in definition + agent schema
  [RequiresProvider("...")]    — requires configured provider at preflight/runtime
  [RequiresModelCapability("...")] — requires at least one capable model
  [RequiresModel("...")]       — requires a specific model
  [RequiresModule("...")]      — requires enabled module
  [RecommendsModule("...")]    — warning only; non-blocking recommendation
  [RequiresPlatform(...)]       — limits supported host platforms
  [RequiresPermission("...")]  — requires caller permission/global flag
  [ToolCall("name")]           — marks a public method as an inline agent tool
  [AgentOutput("json|md|text")]— hints return-value format to the calling agent
  [Output]                     — marks one inner class as the structured output type
  [Schedule("cron")]           — self-register cron schedule
  [OnEvent("Type")]            — self-register event trigger
  [OnFileChanged(path)]         — self-register file watcher trigger
  [OnWebhook("/route")]        — self-register webhook trigger
  [OnTrigger("SourceName")]    — bind to a custom module-provided trigger source
  [OsShortcut("Label")]        — install desktop/app launcher shortcut
  [ConcurrencyPolicy(...)]      — control running-instance collision behavior

Parameter-level requirement attributes:
  [ModelId]                     — marks parameter as model reference
  [RequiresCapability("...")]  — requires that referenced model capability

Allowed constructs:
  var x = expr;  |  TypeName x = expr;     variable declaration
  x = expr;                                assignment
  if (cond) { } else if (cond) { } else { }
  foreach (var item in collection) { }
  while (cond) { }
  await expr;
  return;  |  return expr;

Built-in step methods (all async, all propagate ct):
  await Chat(agent, message)                   — full response from agent
  await ChatStream(agent, message)             — stream response to SSE channel
  await ChatToThread(agent, threadId, message) — chat into a named thread
  await FindModel(nameOrId)                    — resolve a model
  await FindProvider(nameOrId)                 — resolve a provider
  await FindAgent(nameOrId)                    — resolve an agent
  await CreateAgent(name, modelId)             — create agent at runtime
  await CreateThread(channelId, name)          — create thread at runtime
  await Emit(value)                            — push output to SSE; updates outputSnapshotJson
  await ParseResponse<T>(text)                 — parse agent text into typed T
  await HttpGet(url)                           — HTTP GET → string body
  await HttpPost(url, body)                    — HTTP POST with JSON body string
  await HttpPut(url, body)                     — HTTP PUT with JSON body string
  await HttpDelete(url)                        — HTTP DELETE
  await Delay(ms)                              — pause for ms milliseconds
  await WaitUntilStopped()                     — block until cancelled (daemon pattern)
  await Log(message)                           — write to instance logs and SSE

Allowed parameter/variable types:
  string, int, long, float, double, decimal, bool, Guid, DateTime, DateTimeOffset
  nullable variants (e.g. string?, int?)
  List<T>, IList<T>, IEnumerable<T>, ICollection<T> where T is a primitive or data type
  task-defined inner classes

────────────────────────────────────────
DIAGNOSTIC CODES
────────────────────────────────────────
Validation (run on register/update):
  TASK101  parameter has invalid type
  TASK102  data type property has invalid type
  TASK103  more than one [Output] class declared
  TASK104  variable declared more than once in scope
  TASK105  variable declaration uses invalid type
  TASK106  foreach missing iteration variable
  TASK107  foreach missing source expression
  TASK108  ParseResponse<T> references unknown type

Compilation (run on instance start):
  TASK201  required parameter not supplied
  TASK202  supplied parameter value cannot convert to declared type
  TASK203  declared default value cannot convert to declared type

Requirements / trigger diagnostics include TASK4xx codes for invalid requirement
and trigger declarations.

────────────────────────────────────────
PREFLIGHT, SCHEDULING, TRIGGERS
────────────────────────────────────────
Preflight:
  task preflight <taskId> [--param key=value ...]

Scheduling:
  task schedule list
  task schedule get <jobId>
  task schedule create <taskId> --cron <expr> [--timezone <tz>] [--name <n>]
  task schedule update <jobId> --cron <expr> [--timezone <tz>]
  task schedule pause <jobId>
  task schedule resume <jobId>
  task schedule delete <jobId>
  task schedule preview <expr> [--timezone <tz>] [--count N]

Triggers and shortcuts:
  task trigger-sources
  task triggers enable <taskId>
  task triggers disable <taskId>
  task shortcuts install <taskId>
  task shortcuts remove <taskId>

Module-owned trigger mapping:
  Computer Use module (sharpclaw_computer_use):
    [OnWindowFocused], [OnWindowBlurred], [OnHotkey], [OnSystemIdle], [OnSystemActive],
    [OnScreenLocked], [OnScreenUnlocked], [OnDeviceConnected], [OnDeviceDisconnected],
    [OnProcessStarted], [OnProcessStopped], [OsShortcut]
  Database Access module (sharpclaw_database_access):
    [OnQueryReturnsRows]

When those modules are disabled, task registration still succeeds, but preflight emits a
warning-level RecommendsModule finding and the trigger source is absent from
task trigger-sources / GET /tasks/trigger-sources.

────────────────────────────────────────
QUICK PATTERNS
────────────────────────────────────────
One-shot:   set parameterValues at launch; instance completes and exits.
Daemon:     call await WaitUntilStopped() at end of RunAsync; stop via POST .../stop.
Structured output: mark one inner class [Output]; call await Emit(value) at end.
Agent-invokable: ensure isActive=true and agent role has CanInvokeTasksAsTool.
