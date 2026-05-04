# SharpClaw Module: HTTP

> **Module ID:** `sharpclaw_http`
> **Display Name:** HTTP
> **Version:** 1.0.0
> **Tool Prefix:** `http`
> **Platforms:** All
> **Exports:** none
> **Requires:** none

---

## How to Enable

| Setting | Value |
|---------|-------|
| **.env key** | `Modules:sharpclaw_http` |
| **Default** | ❌ Disabled (base `.env`) — ✅ Enabled in `.dev.env` |
| **Prerequisites** | None |
| **Platform** | All |

To enable, add to your core `.env` (`Infrastructure/Environment/.env`)
Modules section:

```jsonc
"sharpclaw_http": "true"
```

To disable, set to `"false"` or remove the key (missing = disabled).

**Runtime toggle** (no restart required):

```
module disable sharpclaw_http
module enable sharpclaw_http
```

See [Module-Enablement-Guide.md](Module-Enablement-Guide.md) for full
details.

---

## Overview

The HTTP module owns SharpClaw's network-facing task primitives. It has
**no LLM-callable tools** — it contributes purely to the task pipeline
and the trigger host:

- A task-script step executor for `HttpGet`, `HttpPost`, `HttpPut`, and
  `HttpDelete` (registered as the `http_request` step).
- A parser extension (`HttpParserExtension`) that exposes the HTTP step
  primitives to the task script grammar.
- Three task trigger sources from the legacy NetworkTriggers module:
  - **WebhookTriggerSource** — HTTP webhook receivers, also implements
    `IWebhookTriggerHost` for routing.
  - **HostProbeTriggerSource** — `HostReachable` and `HostUnreachable`
    triggers (TCP / ICMP probes).
  - **NetworkTriggerSource** — `NetworkChanged` trigger (interface /
    address changes).

When this module is disabled, any task referencing an HTTP step or one
of the triggers above will be flagged by `task preflight` and excluded
from the active trigger source list returned by
`task trigger-sources`.

---

## Task-script Steps

| Step | Description |
|------|-------------|
| `HttpGet`    | Issue an HTTP GET request and capture status / headers / body. |
| `HttpPost`   | Issue an HTTP POST request with optional body and content type. |
| `HttpPut`    | Issue an HTTP PUT request with optional body and content type. |
| `HttpDelete` | Issue an HTTP DELETE request. |

All four compile into the same underlying `http_request` executor,
backed by `HttpTaskStepExecutor`.

---

## Triggers

| Trigger | Source | Notes |
|---------|--------|-------|
| `Webhook`         | `WebhookTriggerSource` | Routes inbound HTTP webhook calls to listeners; exposes `IWebhookTriggerHost`. |
| `HostReachable`   | `HostProbeTriggerSource` | Fires when a configured host becomes reachable. |
| `HostUnreachable` | `HostProbeTriggerSource` | Fires when a configured host stops being reachable. |
| `NetworkChanged`  | `NetworkTriggerSource`   | Fires on network interface / address changes. |

Trigger configuration is owned by the task definition; this module only
provides the live source implementations.

---

## CLI Commands

This module ships no dedicated CLI commands. Tasks that use the HTTP
step or its triggers are managed through the standard `task` command
surface.

---

## Tool Definitions

`GetToolDefinitions()` returns an empty list. Calling
`ExecuteToolAsync(...)` raises an `InvalidOperationException` —
attempts to dispatch a job-pipeline tool through this module are a
programming error.

---

## Module Manifest

```text
Id           = sharpclaw_http
DisplayName  = HTTP
ToolPrefix   = http
ParserExtension = HttpParserExtension.Instance
Exported contracts = none
Required contracts = none
```
