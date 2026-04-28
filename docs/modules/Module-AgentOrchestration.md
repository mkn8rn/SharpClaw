# SharpClaw Module: Agent Orchestration

> **Module ID:** `sharpclaw_agent_orchestration`
> **Display Name:** Agent Orchestration
> **Version:** 1.0.0
> **Tool Prefix:** `ao`
> **Platforms:** All
> **Exports:** none
> **Requires:** none

---

## How to Enable

| Setting | Value |
|---------|-------|
| **.env key** | `Modules:sharpclaw_agent_orchestration` |
| **Default** | ❌ Disabled |
| **Prerequisites** | None |
| **Platform** | All |

To enable, add to your core `.env` (`Infrastructure/Environment/.env`) Modules section:

```jsonc
"sharpclaw_agent_orchestration": "true"
```

To disable, set to `"false"` or remove the key (missing = disabled).

**Runtime toggle** (no restart required):

```
module disable sharpclaw_agent_orchestration
module enable sharpclaw_agent_orchestration
```

See [Module Enablement Guide](Module-Enablement-Guide.md) for full details.

---

## Overview

The Agent Orchestration module provides agent lifecycle management
(create sub-agents, manage agents), task editing, skill access, and
custom chat header editing. All tools flow through the standard job
pipeline.

Tools are dispatched via the SharpClaw module system
(`AgentActionType = ModuleAction`). Tool names are prefixed with `ao_`
when sent to the model — for example, `manage_agent` becomes
`ao_manage_agent`.

---

## Table of Contents

- [Tools](#tools)
  - [ao_create_sub_agent](#ao_create_sub_agent)
  - [ao_manage_agent](#ao_manage_agent)
  - [ao_edit_task](#ao_edit_task)
  - [ao_access_skill](#ao_access_skill)
  - [ao_edit_agent_header](#ao_edit_agent_header)
  - [ao_edit_channel_header](#ao_edit_channel_header)
- [Module-owned CLI resources](#module-owned-cli-resources)
- [Resource Dependencies](#resource-dependencies)
- [Role Permissions](#role-permissions)
- [Module Manifest](#module-manifest)

---

## Tools

### ao_create_sub_agent

Create a sub-agent with permissions ≤ the creator's.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Agent name |
| `modelId` | string (GUID) | yes | Model GUID |
| `systemPrompt` | string | no | System prompt |

**Permission:** Global — requires `canCreateSubAgents` flag.

---

### ao_manage_agent

Update an agent's name, systemPrompt, or modelId.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `resource_id` | string (GUID) | yes | Target agent GUID |
| `name` | string | no | New name |
| `systemPrompt` | string | no | New system prompt |
| `modelId` | string (GUID) | no | New model GUID |

**Permission:** Per-resource — requires `agentAccesses` grant.

**Aliases:** `manage_agent`

---

### ao_edit_task

Edit an Agent Orchestration task's name, repeat interval, or max retries.
This tool targets the module-owned `AoTask` resource, not the Core host task
scheduler's `task schedule` rows.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `resource_id` | string (GUID) | yes | Target task GUID |
| `name` | string | no | New name |
| `repeatIntervalMinutes` | integer | no | Minutes between repeats (0 = remove) |
| `maxRetries` | integer | no | Max retries |

**Permission:** Per-resource — requires an `AoTask` resource grant.

**Aliases:** `edit_task`

Create testable target rows with the module CLI:

```text
resource aotask add <name> [--next-run <timestamp>] [--repeat-minutes <n>] [--max-retries <n>]
```

---

### ao_access_skill

Retrieve an Agent Orchestration skill's instruction text. This tool targets the
module-owned `AoSkill` resource.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `resource_id` | string (GUID) | yes | Target skill GUID |

**Permission:** Per-resource — requires an `AoSkill` resource grant.

**Aliases:** `access_skill`

Create testable target rows with the module CLI:

```text
resource aoskill add <name> --text <skillText> [--description <description>]
```

---

### ao_edit_agent_header

Set or clear the custom chat header for an agent.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `resource_id` | string (GUID) | yes | Target agent GUID |
| `header` | string | no | Template string (empty string to clear) |

**Permission:** Per-resource — requires `agentHeaderAccesses` grant.

**Aliases:** `edit_agent_header`

---

### ao_edit_channel_header

Set or clear the custom chat header for a channel.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `resource_id` | string (GUID) | yes | Target channel GUID |
| `header` | string | no | Template string (empty string to clear) |

**Permission:** Per-resource — requires `channelHeaderAccesses` grant.

**Aliases:** `edit_channel_header`

---

## Module-owned CLI resources

Agent Orchestration owns the resources used by `edit_task` and `access_skill`.
Use these commands under the Core CLI's module-resource dispatch surface:

### AoTask

`AoTask` rows live in `AgentOrchestrationDbContext.ScheduledJobs`. They are
separate from Core host scheduled jobs managed by `task schedule ...`.

```text
resource aotask add <name> [--next-run <timestamp>] [--repeat-minutes <n>] [--max-retries <n>]
resource aotask get <id>
resource aotask list
resource aotask update <id> [--name <name>] [--repeat-minutes <n>] [--max-retries <n>]
resource aotask delete <id>
```

Alias: `resource aot ...`

Use `resource aotask add` to create a target resource before testing
`job submit <channelId> edit_task <aotaskId> ...`.

### AoSkill

`AoSkill` rows live in `AgentOrchestrationDbContext.Skills`.

```text
resource aoskill add <name> --text <skillText> [--description <description>]
resource aoskill get <id>
resource aoskill list
resource aoskill update <id> [--name <name>] [--description <description>] [--text <skillText>]
resource aoskill delete <id>
```

Alias: `resource aos ...`

Use `resource aoskill add` to create a target resource before testing
`job submit <channelId> access_skill <aoskillId> ...`.

---

## Resource Dependencies

| Resource Type | Used by |
|---------------|---------|
| Agents | `ao_manage_agent`, `ao_edit_agent_header` |
| `AoTask` | `ao_edit_task` |
| `AoSkill` | `ao_access_skill` |
| Channels | `ao_edit_channel_header` |

---

## Role Permissions

### Global flags

| Flag | Tools |
|------|-------|
| `canCreateSubAgents` | `ao_create_sub_agent` |
| `canEditAgentHeader` | `ao_edit_agent_header` |
| `canEditChannelHeader` | `ao_edit_channel_header` |

### Per-resource arrays

| Array | Resource Type | Tools |
|-------|---------------|-------|
| `agentAccesses` | Agents | `ao_manage_agent` |
| `AoTask` grants | Agent Orchestration tasks | `ao_edit_task` |
| `AoSkill` grants | Agent Orchestration skills | `ao_access_skill` |
| `agentHeaderAccesses` | Agents | `ao_edit_agent_header` |
| `channelHeaderAccesses` | Channels | `ao_edit_channel_header` |

---

## Module Manifest

```json
{
  "id": "sharpclaw_agent_orchestration",
  "displayName": "Agent Orchestration",
  "version": "1.0.0",
  "toolPrefix": "ao",
  "entryAssembly": "SharpClaw.Modules.AgentOrchestration.dll",
  "minHostVersion": "1.0.0",
  "license": "AGPL-3.0",
  "platforms": null,
  "executionTimeoutSeconds": 30,
  "exports": [],
  "requires": []
}
```
