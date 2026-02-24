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
- [Channel contexts](#channel-contexts)
- [Channels](#channels)
- [Chat](#chat)
- [Agent Jobs](#agent-jobs)
- [Resources](#resources)
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

## Channel contexts

The API exposes a first-class resource called `channel-contexts` (route
group: `/channel-contexts`). These are the same "context" objects used
by the permission system to provide group-level permission grants for
channels and conversations.

### POST /channel-contexts

Create a new channel context.

Request body (example):

```json
{
  "agentId": "guid",
  "name": "string | null",
  "permissionGrants": [
    { "actionType": "UnsafeExecuteAsDangerousShell", "grantedClearance": "ApprovedBySameLevelUser" }
  ]
}
```

Response `200`: `ContextResponse`

### GET /channel-contexts?agentId={guid}

List channel contexts. Optional `agentId` filter.

Response `200`: `ContextResponse[]`

### GET /channel-contexts/{id}

Response `200`: `ContextResponse` or `404` when not found.

### PUT /channel-contexts/{id}

Update a context (e.g. rename).

Request body:

```json
{ "name": "string | null" }
```

Response `200`: `ContextResponse` or `404` when not found.

### DELETE /channel-contexts/{id}

Deletes the context. Conversations and channels inside it are detached
instead of deleted.

Response `204` or `404` when not found.

### POST /channel-contexts/{id}/grant

Add or update a permission grant on the context.

Request body:

```json
{
  "actionType": "UnsafeExecuteAsDangerousShell",
  "grantedClearance": "ApprovedBySameLevelUser"
}
```

Response `200`: `ContextResponse` (updated) or `404` when not found.

ContextResponse shape (example):

```json
{
  "id": "guid",
  "name": "string",
  "agentId": "guid",
  "agentName": "string",
  "createdAt": "datetime",
  "updatedAt": "datetime",
  "permissionGrants": [ ... ]
}
```

---

## Channels

Channels are the primary REST surface for sending messages and
containing chat history. The route group is `/channels`.

### POST /channels

Create a new channel.

Request (example):

```json
{
  "agentId": "guid",
  "title": "string | null",
  "modelId": "guid | null",
  "contextId": "guid | null",
  "permissionGrants": [ ... ]
}
```

Response `200`: `ChannelResponse`

### GET /channels?agentId={guid}

List channels. Optional `agentId` filter.

Response `200`: `ChannelResponse[]`

### GET /channels/{id}

Response `200`: `ChannelResponse` or `404` when not found.

### PUT /channels/{id}

Update channel properties.

Request (example):

```json
{ "title": "string | null", "modelId": "guid | null", "contextId": "guid | null" }
```

Response `200`: `ChannelResponse` or `404` when not found.

### DELETE /channels/{id}

Response `204` or `404` when not found.

---

## Chat (per-channel)

All chat operations are scoped to a channel id. Endpoints live under
`/channels/{id}/chat`.

### POST /channels/{id}/chat

Send a message and receive the assistant's reply. Body:

```json
{ "message": "string" }
```

Response `200` (example):

```json
{
  "userMessage": { "role": "user", "content": "string", "timestamp": "datetime" },
  "assistantMessage": { "role": "assistant", "content": "string", "timestamp": "datetime" },
  "jobResults": [ /* Agent job results, if any */ ]
}
```

When the assistant submits agent jobs during the turn the same
permission-resolution rules apply (see [Permission Resolution](#permission-resolution)).

### GET /channels/{id}/chat

Retrieve chat history for a channel (most recent messages, chronological order).

Response `200`: an array of message objects.

---

## Chat streaming (SSE)

The API exposes an SSE-based streaming chat endpoint which pauses for
inline approvals. Use the following endpoints under the channel route
group:

POST /channels/{id}/chat/stream

- Streams `ChatStreamEvent` items as server-sent events (`text/event-stream`).
- When a job requires approval the stream emits an `approval_required`
  event and waits for the client to POST to the companion approve
  endpoint.

POST /channels/{id}/chat/stream/approve/{jobId}

- Companion endpoint used by the client to resolve a pending approval
  emitted by a running SSE stream. Body:

```json
{ "approved": true }
```

Response `200` on success, or `404` when no pending approval exists.

---

## Transcription streaming

Two streaming transports are provided for live transcription segments.

WebSocket:

    /jobs/{jobId}/ws

- Connect with WebSocket; the server will send JSON text frames with
  transcription segment objects.

SSE:

    /jobs/{jobId}/stream

- Server-sent events with transcription segments in `data` frames.

Both endpoints return `404` if the job is not found or has no active
subscription.

---

## Resources

The API exposes multiple resource types under the `/resources` group.
Examples implemented in the handlers include containers and audio
devices.

### Containers

POST /resources/containers
GET  /resources/containers
GET  /resources/containers/{id}
PUT  /resources/containers/{id}
DELETE /resources/containers/{id}
POST /resources/containers/sync

Typical request/response bodies are `CreateContainerRequest`, `ContainerResponse`,
etc.

### Audio devices

POST /resources/audiodevices
GET  /resources/audiodevices
GET  /resources/audiodevices/{id}
PUT  /resources/audiodevices/{id}
DELETE /resources/audiodevices/{id}
POST /resources/audiodevices/sync

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

The channel/context pre-authorisation logic is used to determine whether a
pending job can execute immediately without per-job approval.

- Conversation/channel pre-auth counts as `ApprovedByWhitelistedUser`-level authority.
- This satisfies agent clearances ≤ 2 (`ApprovedBySameLevelUser`, `ApprovedByWhitelistedUser`).
- Clearances ≥ 3 (`ApprovedByPermittedAgent`, `ApprovedByWhitelistedAgent`) always require explicit per-job approval.

The resolver checks in order: channel's own permission set → parent context → fallback (AwaitingApproval).
