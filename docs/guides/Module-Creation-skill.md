SharpClaw Module Creation — Agent Skill Reference

Full human-readable guide: guides/Module-Creation-Guide.md

────────────────────────────────────────
WHAT A MODULE IS
────────────────────────────────────────
A C# class implementing ISharpClawModule compiled into the solution.
Discovered automatically by ModuleLoader at startup.
Enabled/disabled by the Modules section in Infrastructure/Environment/.env.
Can be toggled at runtime with no restart: module enable/disable <id>

────────────────────────────────────────
REQUIRED INTERFACE MEMBERS
────────────────────────────────────────
string Id                        unique lowercase_underscore identifier
string DisplayName               human-readable name
string ToolPrefix                short prefix for tool names (must be unique)
void ConfigureServices(IServiceCollection)
IReadOnlyList<ModuleToolDefinition> GetToolDefinitions()
void MapEndpoints(IEndpointRouteBuilder)

Optional (have default no-op implementations):
  Task InitializeAsync(IServiceProvider, CancellationToken)
  Task ShutdownAsync()
  Task SeedDataAsync(IServiceProvider, CancellationToken)
  IReadOnlyList<ModuleInlineToolDefinition> GetInlineToolDefinitions()
  IReadOnlyList<ModuleContractExport> ExportedContracts
  IReadOnlyList<ModuleContractRequirement> RequiredContracts

────────────────────────────────────────
LIFECYCLE ORDER
────────────────────────────────────────
ConfigureServices → (container built) → InitializeAsync → SeedDataAsync
  → MapEndpoints → [runtime] → ShutdownAsync

InitializeAsync throws → module disabled, exported contracts poisoned,
  dependent modules cascade-fail.
SeedDataAsync runs once only (.seeded marker file guards repeat calls).

────────────────────────────────────────
TOOL REGISTRATION
────────────────────────────────────────
Job-pipeline tools (full job lifecycle, auditable):
  Return ModuleToolDefinition records from GetToolDefinitions().
  Handle in ExecuteToolAsync(toolName, arguments, agentId, services, ct).
  Tool exposed to model as: {prefix}_{name}
  Aliases: IReadOnlyList<string>? on the record for legacy names.

Inline tools (stateless, no job record, runs in chat loop):
  Return ModuleInlineToolDefinition records from GetInlineToolDefinitions().
  Handle in ExecuteInlineToolAsync(toolName, arguments, agentId, services, ct).

ModuleToolDefinition constructor:
  Name, Description, ParametersSchema (JsonElement), Permission, TimeoutSeconds?,
  Aliases?

ModuleToolPermission:
  IsPerResource (bool)
  Check: Func<Guid, Guid?, ActionCaller, CancellationToken, Task<AgentActionResult>>?
  DelegateTo: string?  (name of existing AgentActionService method; validated at startup)

Return values:
  ModuleToolResult.Success(message)
  ModuleToolResult.NotHandled()
  ModuleInlineToolResult.Success(message)
  ModuleInlineToolResult.NotHandled()

────────────────────────────────────────
CONTRACTS
────────────────────────────────────────
Export — register interface in DI + declare in ExportedContracts:
  new ModuleContractExport(contractName, typeof(IMyInterface), description?)
  contractName: lowercase_underscore, max 60 chars, unique across all modules

Require — declare in RequiredContracts:
  new ModuleContractRequirement(contractName, IsOptional: false)
  IsOptional: true → module loads, feature degrades if provider absent

Initialization order is sorted by contract dependency graph automatically.

────────────────────────────────────────
TASK TRIGGER OWNERSHIP
────────────────────────────────────────
Implement ITaskTriggerSourceProvider alongside ISharpClawModule.
Register: services.AddSingleton<ITaskTriggerSourceProvider, MySource>()
Members: SourceName (string), SupportedKinds (IReadOnlyList<TaskTriggerKind>)
         EnableTriggerAsync(TaskDefinition, ct), DisableTriggerAsync(taskId, ct)

Module must be enabled for owned trigger kinds to fire.
Check ownership: GET /tasks/trigger-sources

────────────────────────────────────────
CLI COMMANDS
────────────────────────────────────────
Implement ICliCommandProvider. Register in ConfigureServices.
Members: Verbs (IReadOnlyList<string>), HandleAsync(string[], ct) → CliResult

────────────────────────────────────────
ENABLING A NEW MODULE
────────────────────────────────────────
Add to Infrastructure/Environment/.env Modules section:
  "my_module": "true"

Runtime (no restart): module enable my_module
Verify: module get my_module  →  status should be "enabled"
If status is "failed", check application log under [Module:my_module] for exception.

────────────────────────────────────────
TROUBLESHOOTING
────────────────────────────────────────
Not in module list         → class not implementing interface or project not compiled
Status: failed             → InitializeAsync threw; check log [Module:{id}]
Tool not reaching handler  → permission check denied; verify ModuleToolPermission
Inline tool produces nothing → ExecuteInlineToolAsync returned NotHandled or threw
Contract not satisfied     → provider module disabled or failed; check module list
SeedDataAsync not running  → .seeded marker exists; delete it to force re-seed
Trigger never fires        → check EnableTriggerAsync was called; check OS permissions
