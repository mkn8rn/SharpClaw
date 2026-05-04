SharpClaw Module: HTTP — Agent Skill Reference

Module ID: sharpclaw_http
Display Name: HTTP
Tool Prefix: http
Version: 1.0.0
Platforms: all
Exports: none
Requires: none

────────────────────────────────────────
ENABLING
────────────────────────────────────────
.env key: Modules:sharpclaw_http
Default: disabled in base .env, enabled in .dev.env
Prerequisites: none
Platform: all

To enable, add to your core .env (Infrastructure/Environment/.env)
Modules section:
  "sharpclaw_http": "true"

To disable, set to "false" or remove the key (missing = disabled).

Runtime toggle (no restart required):
  module disable sharpclaw_http
  module enable sharpclaw_http

See docs/modules/Module-Enablement-Guide.md for full details.

────────────────────────────────────────
OVERVIEW
────────────────────────────────────────
Task-pipeline-only module. No LLM-callable tools.

Owns:
  - http_request task step (HttpGet/HttpPost/HttpPut/HttpDelete).
  - HttpParserExtension for the task script grammar.
  - Network trigger sources: Webhook, HostReachable, HostUnreachable,
    NetworkChanged.

────────────────────────────────────────
TASK-SCRIPT STEPS
────────────────────────────────────────
HttpGet     — HTTP GET request, captures status/headers/body.
HttpPost    — HTTP POST request with optional body / content type.
HttpPut     — HTTP PUT request with optional body / content type.
HttpDelete  — HTTP DELETE request.

All compile to the http_request executor (HttpTaskStepExecutor).

────────────────────────────────────────
TRIGGERS
────────────────────────────────────────
Webhook         — WebhookTriggerSource (also IWebhookTriggerHost)
HostReachable   — HostProbeTriggerSource
HostUnreachable — HostProbeTriggerSource
NetworkChanged  — NetworkTriggerSource

If this module is disabled, tasks using these triggers are flagged by
task preflight and removed from task trigger-sources.

────────────────────────────────────────
TOOLS
────────────────────────────────────────
None. ExecuteToolAsync(...) throws InvalidOperationException.

────────────────────────────────────────
CLI
────────────────────────────────────────
No dedicated commands. Use the standard task command surface.
