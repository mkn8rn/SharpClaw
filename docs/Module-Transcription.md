# SharpClaw Module: Transcription

> **Module ID:** `sharpclaw_transcription`
> **Display Name:** Transcription
> **Version:** 1.0.0
> **Tool Prefix:** `tr`
> **Platforms:** Windows only
> **Exports:** `transcription_stt` (`ITranscriptionApiClient`), `transcription_audio_capture` (`IAudioCaptureProvider`)
> **Requires:** none

---

## Overview

The Transcription module provides live audio transcription, input audio
device management, and STT (speech-to-text) provider integration. Audio
is captured via WASAPI (Windows only), normalised to mono 16 kHz 16-bit
PCM, and sent to transcription providers (OpenAI Whisper, Groq, or
local Whisper.net).

Tools are dispatched via the SharpClaw module system
(`AgentActionType = ModuleAction`). Tool names are prefixed with `tr_`
when sent to the model. However, transcription jobs use the core
`IsTranscriptionAction` path in `AgentJobService` — they are
intercepted before dispatch reaches the module and routed to
`ILiveTranscriptionOrchestrator.StartTranscriptionAsync`.

---

## Table of Contents

- [Enums](#enums)
- [Tools](#tools)
  - [tr_transcribe_audio_device](#tr_transcribe_audio_device)
  - [tr_transcribe_audio_stream](#tr_transcribe_audio_stream)
  - [tr_transcribe_audio_file](#tr_transcribe_audio_file)
- [Transcription Modes](#transcription-modes)
- [Deduplication Pipeline](#deduplication-pipeline)
- [Language Enforcement](#language-enforcement)
- [Streaming Transports](#streaming-transports)
- [CLI Commands](#cli-commands)
- [Resource Dependencies](#resource-dependencies)
- [Role Permissions](#role-permissions)
- [Exported Contracts](#exported-contracts)
- [Module Manifest](#module-manifest)

---

## Enums

### TranscriptionMode

| Value | Int | Description |
|-------|-----|-------------|
| `SlidingWindow` | 0 | Two-pass sliding window (default). Segments are emitted provisionally, then finalized or retracted after commit delay. |
| `StrictWindow` | 2 | Non-overlapping sequential windows. Each window transcribed exactly once. Minimal token cost; perceived latency equals window length. |

---

## Tools

### tr_transcribe_audio_device

Start live transcription from an input audio device.

**Parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `targetId` | string (GUID) | yes | Input audio device resource GUID |
| `transcriptionModelId` | string (GUID) | no | Override model (defaults from channel → context) |
| `language` | string | no | BCP-47 language hint (e.g. `"en"`, `"de"`, `"ja"`) |
| `transcriptionMode` | string | no | `"SlidingWindow"` (default) or `"StrictWindow"` |
| `windowSeconds` | integer | no | Audio window per inference tick (5–15, default 10) |
| `stepSeconds` | integer | no | Step between ticks, SlidingWindow only (1–window, default 2) |

**Permission:** Per-resource — requires `audioDeviceAccesses` grant.

**Returns:** Transcription segments (streamed via WebSocket or SSE).

---

### tr_transcribe_audio_stream

Start live transcription from an audio stream.

**Parameters:** Same as `tr_transcribe_audio_device`.

**Permission:** Per-resource — requires `audioDeviceAccesses` grant.

---

### tr_transcribe_audio_file

Transcribe an audio file.

**Parameters:** Same as `tr_transcribe_audio_device`.

**Permission:** Per-resource — requires `audioDeviceAccesses` grant.

---

## Transcription Modes

### SlidingWindow (default)

Two-pass pipeline. Segments go through a lifecycle:

1. **Provisional** — emitted within one inference tick (~2 s).
   `isProvisional: true`.
2. **Finalized** — confirmed after commit delay (2 s). Same `id`,
   updated text/confidence, `isProvisional: false`.
3. **Retracted** — if not confirmed within 2× commit delay (likely
   hallucination), tombstone event with empty text.

### StrictWindow

Non-overlapping sequential windows (default 10 s). Each window
transcribed exactly once — one API call per window. All segments are
final on first emission (`isProvisional` always `false`).

### Pipeline Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `WindowSeconds` | 10 | Default audio window per inference tick |
| `InferenceIntervalSeconds` | 2 | Default step between ticks |
| `BufferCapacitySeconds` | 15 | Ring buffer size / max window clamp |
| `CommitDelaySeconds` | 2.0 | Delay before provisional → finalized |
| `MaxPromptChars` | 500 | Rolling prompt buffer size for Whisper |
| `SampleRate` | 16 000 | Audio sample rate (mono PCM) |

---

## Deduplication Pipeline

When the STT API returns the full window as a single text blob without
per-word timestamps (e.g. `gpt-4o-transcribe`):

1. **Text diff** — compares current response against previous; uses
   containment check (strict for < 10 words, 10% fuzzy for longer) and
   suffix-prefix overlap to extract new content.
2. **Context tracking** — `previousWindowText` updated carefully to
   prevent context loss.
3. **Sentence splitting** — multiple sentences split at boundaries with
   proportional timestamp distribution.
4. **Fragment merge** — short residuals (≤ 2 words, lowercase) merged
   into most recent provisional segment.
5. **Emitted-text guard** — HashSet tracks all emitted texts; duplicates
   skipped (catches Whisper hallucination replay).
6. **Timestamp guard** — segments with `absEnd ≤ lastSeenEnd` skipped.

---

## Language Enforcement

When `language` is set:

- Whisper prompt is seeded with a natural phrase from an embedded
  `transcription-language-seeds.json` covering 99 Whisper-supported
  languages.
- If the API response language tag doesn't match, the chunk is retried
  up to 4 times with escalating reinforcement (single seed → triple →
  instruction preamble → max saturation block).
- Final result is accepted even on mismatch — no audio is silently
  dropped.

---

## Streaming Transports

### WebSocket

```
/jobs/{jobId}/ws
```

Server sends JSON text frames with transcription segment objects.

### SSE

```
/jobs/{jobId}/stream
```

Server-sent events with segments in `data` frames.

Both return `404` if the job is not found or has no active subscription.

---

## CLI Commands

The module registers an `inputaudio` resource command (alias: `ia`):

```
resource inputaudio add <name> [identifier] [description]
resource inputaudio get <id>
resource inputaudio list
resource inputaudio update <id> [name] [identifier]
resource inputaudio delete <id>
resource inputaudio sync            Import system input audio devices
```

---

## Resource Dependencies

| Resource Type | Used by |
|---------------|---------|
| Input Audio Devices | All transcription tools |

Input audio devices can be synced via
`POST /resources/audiodevices/sync` or `resource inputaudio sync`.

A default input audio device ("Default", identifier `"default"`) is
seeded on module startup.

---

## Role Permissions

### Per-resource arrays

| Array | Resource Type | Tools |
|-------|---------------|-------|
| `audioDeviceAccesses` | InputAudioDevices | All transcription tools |

---

## Exported Contracts

| Contract Name | Interface | Description |
|---------------|-----------|-------------|
| `transcription_stt` | `ITranscriptionApiClient` | Speech-to-text transcription via provider APIs |
| `transcription_audio_capture` | `IAudioCaptureProvider` | Audio capture from input devices |

---

## Audio / Concurrency Contract

- One job = one task.
- Single consumer `Channel<(byte[], int)>`.
- No `Task.Run` per chunk.
- Linked CTS propagates errors.
- Consumer awaited before rethrow.
- OpenAI client uses 3 retries with exponential backoff on 429.

---

## Module Manifest

```json
{
  "id": "sharpclaw_transcription",
  "displayName": "Transcription",
  "version": "1.0.0",
  "toolPrefix": "tr",
  "entryAssembly": "SharpClaw.Modules.Transcription",
  "minHostVersion": "1.0.0",
  "platforms": ["windows"],
  "executionTimeoutSeconds": 120,
  "exports": [
    {
      "contractName": "transcription_stt",
      "serviceType": "SharpClaw.Modules.Transcription.Clients.ITranscriptionApiClient"
    },
    {
      "contractName": "transcription_audio_capture",
      "serviceType": "SharpClaw.Modules.Transcription.Clients.IAudioCaptureProvider"
    }
  ],
  "requires": []
}
```
