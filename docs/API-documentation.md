# SharpClaw Application API Reference

> **Base URL:** `http://127.0.0.1:48923`
>
> **Authentication:** Every request must include an `X-Api-Key` header.
> The key is auto-generated per session and written to
> `%LOCALAPPDATA%/SharpClaw/.api-key`.

All request/response bodies are JSON. Enum fields are serialized as strings.
Timestamps are ISO 8601 (`DateTimeOffset`).

---

## Table of Contents

- [Enums](#enums)
- [Auth](#auth)
- [Providers](#providers)
- [Models](#models)
- [Agents](#agents)
- [Contexts](#contexts)
- [Conversations](#conversations)
- [Chat](#chat)
- [Agent Jobs](#agent-jobs)
- [Permission Resolution](#permission-resolution)

---

## Enums

### ProviderType

```
OpenAI, Anthropic, OpenRouter, GoogleVertexAI, GoogleGemini,
ZAI, VercelAIGateway, XAI, Groq, Cerebras, Mistral, GitHubCopilot, Custom
```

### AgentActionType

| Category | Values |
|----------|--------|
| Global flags | `CreateSubAgent`, `CreateContainer`, `RegisterInfoStore`, `EditAnyTask` |
| Per-resource | `UnsafeExecuteAsDangerousShell`, `ExecuteAsSafeShell`, `AccessLocalInfoStore`, `AccessExternalInfoStore`, `AccessWebsite`, `QuerySearchEngine`, `AccessContainer`, `ManageAgent`, `EditTask`, `AccessSkill` |
| Transcription | `TranscribeFromAudioDevice`, `TranscribeFromAudioStream`, `TranscribeFromAudioFile` |

### PermissionClearance

| Value | Int | Description |
|-------|-----|-------------|
| `Unset` | 0 | Falls back to the group-level default |
| `ApprovedBySameLevelUser` | 1 | Requires approval from a same-level user |
| `ApprovedByWhitelistedUser` | 2 | Requires approval from a whitelisted user |
| `ApprovedByPermittedAgent` | 3 | Requires approval from an agent with the same permission |
| `ApprovedByWhitelistedAgent` | 4 | Requires approval from a whitelisted agent |
| `Independent` | 5 | Agent can act without any external approval |

### AgentJobStatus

| Value | Int | Description |
|-------|-----|-------------|
| `Queued` | 0 | Created, permission check in progress |
| `Executing` | 1 | Permission granted, action running |
| `AwaitingApproval` | 2 | Requires approval before execution |
| `Completed` | 3 | Finished successfully |
| `Failed` | 4 | Action threw an error |
| `Denied` | 5 | Agent lacks the required permission |
| `Cancelled` | 6 | Cancelled by a user or agent |

---

## Auth

### POST /auth/register

Register a new user.

**Request:**

```json
{
  "username": "string",
  "password": "string"
}
```

**Response `200`:**

```json
{
  "id": "guid",
  "username": "string"
}
```

---

### POST /auth/login

**Request:**

```json
{
  "username": "string",
  "password": "string",
  "rememberMe": false
}
```

**Response `200`:**

```json
{
  "accessToken": "string",
  "accessTokenExpiresAt": "datetime",
  "refreshToken": "string | null",
  "refreshTokenExpiresAt": "datetime | null"
}
```

**Response `401`:** Invalid credentials.

---

### POST /auth/refresh

**Request:**

```json
{
  "refreshToken": "string"
}
```

**Response `200`:** Same shape as login response.

**Response `401`:** Invalid or expired refresh token.

---

### POST /auth/invalidate-access-tokens

**Request:**

```json
{
  "userIds": ["guid"]
}
```

**Response `204`:** No content.

---

### POST /auth/invalidate-refresh-tokens

**Request:**

```json
{
  "userIds": ["guid"]
}
```

**Response `204`:** No content.

---

## Providers

### POST /providers

Create a new provider.

**Request:**

```json
{
  "name": "string",
  "providerType": "OpenAI",
  "apiEndpoint": "string | null",
  "apiKey": "string | null"
}
```

`apiEndpoint` is required for `Custom` providers, ignored otherwise.

**Response `200`:** `ProviderResponse`

---

### GET /providers

List all providers.

**Response `200`:** `ProviderResponse[]`

---

### GET /providers/{id}

**Response `200`:** `ProviderResponse`
**Response `404`:** Not found.

---

### PUT /providers/{id}

**Request:**

```json
{
  "name": "string | null",
  "apiEndpoint": "string | null"
}
```

**Response `200`:** `ProviderResponse`
**Response `404`:** Not found.

---

### DELETE /providers/{id}

**Response `204`:** Deleted.
**Response `404`:** Not found.

---

### POST /providers/{id}/set-key

**Request:**

```json
{
  "apiKey": "string"
}
```

**Response `204`:** Key stored (encrypted at rest).

---

### POST /providers/{id}/sync-models

Syncs model list from the provider's API.

**Response `200`:** Synced model list.

---

### POST /providers/{id}/auth/device-code

Start an OAuth device code flow for providers that require it (e.g. GitHub Copilot).

**Response `200`:**

```json
{
  "userCode": "string",
  "verificationUri": "string",
  "expiresInSeconds": 0
}
```

---

### ProviderResponse

```json
{
  "id": "guid",
  "name": "string",
  "providerType": "OpenAI",
  "apiEndpoint": "string | null",
  "hasApiKey": true
}
```

---

## Models

### POST /models

**Request:**

```json
{
  "name": "string",
  "providerId": "guid"
}
```

**Response `200`:** `ModelResponse`

---

### GET /models

**Response `200`:** `ModelResponse[]`

---

### GET /models/{id}

**Response `200`:** `ModelResponse`
**Response `404`:** Not found.

---

### PUT /models/{id}

**Request:**

```json
{
  "name": "string | null"
}
```

**Response `200`:** `ModelResponse`
**Response `404`:** Not found.

---

### DELETE /models/{id}

**Response `204`:** Deleted.
**Response `404`:** Not found.

---

### ModelResponse

```json
{
  "id": "guid",
  "name": "string",
  "providerId": "guid",
  "providerName": "string"
}
```

---

## Agents

### POST /agents

**Request:**

```json
{
  "name": "string",
  "modelId": "guid",
  "systemPrompt": "string | null"
}
```

**Response `200`:** `AgentResponse`

---

### GET /agents

**Response `200`:** `AgentResponse[]`

---

### GET /agents/{id}

**Response `200`:** `AgentResponse`
**Response `404`:** Not found.

---

### PUT /agents/{id}

**Request:**

```json
{
  "name": "string | null",
  "modelId": "guid | null",
  "systemPrompt": "string | null"
}
```

**Response `200`:** `AgentResponse`
**Response `404`:** Not found.

---

### DELETE /agents/{id}

**Response `204`:** Deleted.
**Response `404`:** Not found.

---

### AgentResponse

```json
{
  "id": "guid",
  "name": "string",
  "systemPrompt": "string | null",
  "modelId": "guid",
  "modelName": "string",
  "providerName": "string"
}
```

---

## Contexts

A context is a named group of conversations and tasks that share a common
set of pre-authorised permission grants. See [Permission Resolution](#permission-resolution).

### POST /contexts

**Request:**

```json
{
  "agentId": "guid",
  "name": "string | null",
  "permissionGrants": [
    { "actionType": "UnsafeExecuteAsDangerousShell", "grantedClearance": "ApprovedBySameLevelUser" }
  ]
}
```

**Response `200`:** `ContextResponse`

---

### GET /contexts?agentId={guid}

List contexts. Optional `agentId` filter.

**Response `200`:** `ContextResponse[]`

---

### GET /contexts/{id}

**Response `200`:** `ContextResponse`
**Response `404`:** Not found.

---

### PUT /contexts/{id}

**Request:**

```json
{
  "name": "string | null"
}
```

**Response `200`:** `ContextResponse`
**Response `404`:** Not found.

---

### DELETE /contexts/{id}

Deletes the context. Conversations and tasks inside it are detached
(set to standalone), not deleted.

**Response `204`:** Deleted.
**Response `404`:** Not found.

---

### POST /contexts/{id}/grant

Add or update a permission grant on the context.

**Request:**

```json
{
  "actionType": "UnsafeExecuteAsDangerousShell",
  "grantedClearance": "ApprovedBySameLevelUser"
}
```

**Response `200`:** `ContextResponse` (updated, including all grants).
**Response `404`:** Not found.

---

### ContextResponse

```json
{
  "id": "guid",
  "name": "string",
  "agentId": "guid",
  "agentName": "string",
  "createdAt": "datetime",
  "updatedAt": "datetime",
  "permissionGrants": [
    {
      "id": "guid",
      "actionType": "UnsafeExecuteAsDangerousShell",
      "grantedClearance": "ApprovedBySameLevelUser"
    }
  ]
}
```

---

## Conversations

A conversation belongs to an agent and optionally to a context. Each
conversation has its own model (changeable mid-conversation), title,
chat history, and permission grant overrides.

### POST /conversations

**Request:**

```json
{
  "agentId": "guid",
  "title": "string | null",
  "modelId": "guid | null",
  "contextId": "guid | null",
  "permissionGrants": [
    { "actionType": "AccessWebsite", "grantedClearance": "Independent" }
  ]
}
```

- `modelId` defaults to the agent's current model if omitted.
- `contextId` is optional; when set the context's grants become defaults.

**Response `200`:** `ConversationResponse`

---

### GET /conversations?agentId={guid}

List conversations. Optional `agentId` filter.

**Response `200`:** `ConversationResponse[]`

---

### GET /conversations/{id}

**Response `200`:** `ConversationResponse`
**Response `404`:** Not found.

---

### PUT /conversations/{id}

**Request:**

```json
{
  "title": "string | null",
  "modelId": "guid | null",
  "contextId": "guid | null"
}
```

- Set `contextId` to a valid GUID to attach to a context.
- Set `contextId` to `"00000000-0000-0000-0000-000000000000"` to detach.

**Response `200`:** `ConversationResponse`
**Response `404`:** Not found.

---

### DELETE /conversations/{id}

**Response `204`:** Deleted.
**Response `404`:** Not found.

---

### POST /conversations/{id}/grant

Add or update a per-conversation permission override.

**Request:**

```json
{
  "actionType": "UnsafeExecuteAsDangerousShell",
  "grantedClearance": "Independent"
}
```

**Response `200`:** `ConversationResponse` (updated).
**Response `404`:** Not found.

---

### ConversationResponse

```json
{
  "id": "guid",
  "title": "string",
  "agentId": "guid",
  "agentName": "string",
  "modelId": "guid",
  "modelName": "string",
  "contextId": "guid | null",
  "contextName": "string | null",
  "createdAt": "datetime",
  "updatedAt": "datetime",
  "permissionGrants": [
    {
      "id": "guid",
      "actionType": "AccessWebsite",
      "grantedClearance": "Independent"
    }
  ],
  "effectivePermissions": [
    {
      "actionType": "UnsafeExecuteAsDangerousShell",
      "grantedClearance": "ApprovedBySameLevelUser",
      "source": "context"
    },
    {
      "actionType": "AccessWebsite",
      "grantedClearance": "Independent",
      "source": "conversation"
    }
  ]
}
```

---

## Chat

All chat operations are scoped to a conversation.

### POST /conversations/{conversationId}/chat

Send a message and receive the assistant's reply.

When the agent has permissions to perform actions (shell execution,
info store access, etc.), the assistant can autonomously submit jobs
during the conversation turn. Tool calls are executed via the same
`AgentJobService.SubmitAsync` pipeline — the agent's role permissions
**and** the channel/context permission set are both evaluated.

The channel's permission set is defined by the user and counts as
`ApprovedByWhitelistedUser`-level pre-authorisation. When the agent's
role clearance is ≤ 2 (`ApprovedBySameLevelUser` or
`ApprovedByWhitelistedUser`) and the channel or context has a matching
grant, the job executes immediately. Clearances ≥ 3
(`ApprovedByPermittedAgent`, `ApprovedByWhitelistedAgent`) always
require explicit per-job approval, at which point **the chat stops** —
no further tool calls are processed until the user approves or denies
the pending job(s) via `POST /agents/{agentId}/jobs/{jobId}/approve`.

**Request:**

```json
{
  "message": "string"
}
```

**Response `200`:**

```json
{
  "userMessage": {
    "role": "user",
    "content": "string",
    "timestamp": "datetime"
  },
  "assistantMessage": {
    "role": "assistant",
    "content": "string",
    "timestamp": "datetime"
  },
  "jobResults": [
    {
      "id": "guid",
      "agentId": "guid",
      "actionType": "ExecuteAsSafeShell",
      "resourceId": "guid | null",
      "status": "Completed | Denied | AwaitingApproval",
      "effectiveClearance": "Independent",
      "resultData": "string | null",
      "errorLog": "string | null"
    }
  ]
}
```

`jobResults` is `null` when no tool calls were made during the turn.

---

### GET /conversations/{conversationId}/chat

Retrieve chat history for a conversation (most recent 50 messages,
chronological order).

**Response `200`:**

```json
[
  {
    "role": "user",
    "content": "string",
    "timestamp": "datetime"
  },
  {
    "role": "assistant",
    "content": "string",
    "timestamp": "datetime"
  }
]
```

---

## Agent Jobs

Jobs represent permission-gated agent actions. When a job is submitted,
the permission system evaluates it immediately:

- **Approved** → executes inline, returns `Completed` or `Failed`.
- **Pending** → checks conversation/context for a user-granted
  permission. If found → executes. Otherwise → `AwaitingApproval`.
- **Denied** → returns `Denied`.

### POST /agents/{agentId}/jobs

Submit a new job.

**Request:**

```json
{
  "actionType": "ExecuteAsSafeShell",
  "resourceId": "guid | null",
  "callerUserId": "guid | null",
  "callerAgentId": "guid | null",
  "dangerousShellType": "Bash | PowerShell | CommandPrompt | Git | null",
  "safeShellType": "Mk8Shell | null"
}
```

`resourceId` is required for per-resource action types. Global action
types ignore it.

**Response `200`:** `AgentJobResponse`

---

### GET /agents/{agentId}/jobs

List all jobs for an agent.

**Response `200`:** `AgentJobResponse[]`

---

### GET /agents/{agentId}/jobs/{jobId}

**Response `200`:** `AgentJobResponse`
**Response `404`:** Not found.

---

### POST /agents/{agentId}/jobs/{jobId}/approve

Approve a job that is `AwaitingApproval`.

**Request:**

```json
{
  "approverUserId": "guid | null",
  "approverAgentId": "guid | null"
}
```

The approver's identity is re-evaluated against the clearance
requirement.

**Response `200`:** `AgentJobResponse`
**Response `404`:** Not found.

---

### POST /agents/{agentId}/jobs/{jobId}/cancel

Cancel a job.

**Response `200`:** `AgentJobResponse`
**Response `404`:** Not found.

---

### AgentJobResponse

```json
{
  "id": "guid",
  "agentId": "guid",
  "actionType": "ExecuteAsSafeShell",
  "resourceId": "guid | null",
  "status": "Completed",
  "effectiveClearance": "ApprovedBySameLevelUser",
  "resultData": "string | null",
  "errorLog": "string | null",
  "dangerousShellType": "Bash | null",
  "safeShellType": "Mk8Shell | null",
  "logs": [
    {
      "message": "string",
      "level": "Information",
      "timestamp": "datetime"
    }
  ],
  "createdAt": "datetime",
  "startedAt": "datetime | null",
  "completedAt": "datetime | null"
}
```

---

## Permission Resolution

When an agent action is submitted as a job, the permission system
evaluates it through two stages:

### Stage 1 — Agent capability check

The agent's **role permission set** is checked to determine whether the
agent has the grant at all and what clearance level it requires.

- `Independent` (5) → **approved immediately**, no further checks.
- Any other clearance level → proceeds to Stage 2.
- No matching grant → **denied**.

### Stage 2 — Channel / context pre-authorisation

Conversation pre-auth counts as `ApprovedByWhitelistedUser`-level
authority. The user who configured the channel or context permission
set is treated as a whitelisted user granting approval in advance.

This means pre-auth only satisfies agent clearances ≤ 2:

| Agent clearance | Pre-auth? | Outcome |
|----------------|-----------|---------|
| `ApprovedBySameLevelUser` (1) | ✓ | Executes if channel/context has grant |
| `ApprovedByWhitelistedUser` (2) | ✓ | Executes if channel/context has grant |
| `ApprovedByPermittedAgent` (3) | ✗ | Always `AwaitingApproval` |
| `ApprovedByWhitelistedAgent` (4) | ✗ | Always `AwaitingApproval` |

When the agent's clearance is ≤ 2, the system checks for a matching
grant in this order:

```
┌──────────────────────────────────────────────────┐
│ 1. Channel's own permission set                  │ ← checked first
│ 2. Parent context's permission set               │ ← fallback
│ 3. No match → AwaitingApproval                   │
└──────────────────────────────────────────────────┘
```

- If the channel PS has a matching grant (exact resource or wildcard)
  → **pre-authorised**, executes immediately.
- If the channel PS does not have it → check the context PS.
- If neither has it → `AwaitingApproval`. The user must approve via
  `POST /agents/{agentId}/jobs/{jobId}/approve`.

Only grant **existence** matters for pre-auth — the clearance value
on the channel/context grant itself is irrelevant.

The `effectivePermissions` array in `ConversationResponse` shows the
merged result with source attribution.

Standalone conversations (no context) rely solely on their own grants.
