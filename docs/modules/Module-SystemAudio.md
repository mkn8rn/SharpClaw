# SharpClaw Module: System Audio

> **Module ID:** `sharpclaw_systemaudio`
> **Display Name:** System Audio
> **Version:** 1.0.0
> **Tool Prefix:** `sa`
> **Platforms:** Windows only (WASAPI audio capture)
> **Exports:** `system_audio_capture` (`IAudioCaptureProvider`)
> **Requires:** none

---

## How to Enable

| Setting | Value |
|---------|-------|
| **.env key** | `Modules:sharpclaw_systemaudio` |
| **Default** | ❌ Disabled (base `.env`) — ✅ Enabled in `.dev.env` |
| **Prerequisites** | None |
| **Platform** | Windows only (WASAPI audio capture) |

To enable, add to your core `.env` (`Infrastructure/Environment/.env`)
Modules section:

```jsonc
"sharpclaw_systemaudio": "true"
```

To disable, set to `"false"` or remove the key (missing = disabled).

**Runtime toggle** (no restart required):

```
module disable sharpclaw_systemaudio
module enable sharpclaw_systemaudio
```

See [Module-Enablement-Guide.md](Module-Enablement-Guide.md) for full
details.

> **Important:** Required by the Transcription module. Disabling
> System Audio cascade-disables `sharpclaw_transcription` because the
> `system_audio_capture` contract becomes unavailable.

---

## Overview

The System Audio module owns input audio device CRUD, the WASAPI
capture pipeline, and the `InputAudio` resource type. It exports the
`system_audio_capture` contract that the Transcription module consumes
for live audio capture, and exposes a CLI surface for managing input
audio devices.

The module has **no LLM-callable tools**; agents interact with audio
through the Transcription module (which provides the
`tr_transcribe_*` tools), and System Audio simply supplies the capture
provider and the resource catalogue behind those tools.

---

## Exported Contracts

| Contract | Service | Description |
|----------|---------|-------------|
| `system_audio_capture` | `IAudioCaptureProvider` | WASAPI-backed audio capture provider used for live transcription and any other audio-aware module. |

The default implementation is `WasapiAudioCaptureProvider`, used in
combination with the singleton `SharedAudioCaptureManager` so multiple
consumers targeting the same device share a single capture session and
ring buffer.

---

## Resource Type

| Resource | Type code | Permission |
|----------|-----------|------------|
| `InputAudio` (`InputAudioDB`) | `TrAudio` | `AccessInputAudioAsync` |

`InputAudio` resources represent input audio devices. They participate
in the standard SharpClaw resource permission system: agents need a
`TrAudio` grant on a specific input audio device to use it (or the
`AllResources` wildcard).

---

## CLI Commands

Resource-type CLI commands are exposed under `resource inputaudio`
(alias: `ia`):

| Command | Description |
|---------|-------------|
| `resource inputaudio add <name> [identifier] [description]` | Register a new input audio device. |
| `resource inputaudio get <id>`                              | Show one input audio device. |
| `resource inputaudio list`                                  | List all input audio devices. |
| `resource inputaudio update <id> [name] [identifier]`       | Update an input audio device. |
| `resource inputaudio delete <id>`                           | Delete an input audio device. |
| `resource inputaudio sync`                                  | Import system input audio devices via WASAPI enumeration. |

---

## Seed Data

`SeedDataAsync(...)` registers a default `InputAudio` device on first
install so a fresh deployment has a usable target for transcription
without requiring a CLI roundtrip.

---

## Tool Definitions

`GetToolDefinitions()` returns an empty list. The module is
contract-and-resource-only; there are no `sa_*` tools.

---

## Module Manifest

```text
Id           = sharpclaw_systemaudio
DisplayName  = System Audio
ToolPrefix   = sa
Exported contracts = system_audio_capture (IAudioCaptureProvider)
Required contracts = none
Resource types     = InputAudio (TrAudio)
```
