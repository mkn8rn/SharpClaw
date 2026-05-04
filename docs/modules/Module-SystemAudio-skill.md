SharpClaw Module: System Audio — Agent Skill Reference

Module ID: sharpclaw_systemaudio
Display Name: System Audio
Tool Prefix: sa
Version: 1.0.0
Platforms: Windows only (WASAPI audio capture)
Exports: system_audio_capture (IAudioCaptureProvider)
Requires: none

────────────────────────────────────────
ENABLING
────────────────────────────────────────
.env key: Modules:sharpclaw_systemaudio
Default: disabled in base .env, enabled in .dev.env
Prerequisites: none
Platform: Windows only (WASAPI)

To enable, add to your core .env (Infrastructure/Environment/.env)
Modules section:
  "sharpclaw_systemaudio": "true"

To disable, set to "false" or remove the key (missing = disabled).

Runtime toggle (no restart required):
  module disable sharpclaw_systemaudio
  module enable sharpclaw_systemaudio

See docs/modules/Module-Enablement-Guide.md for full details.

Important: Required by sharpclaw_transcription. Disabling System Audio
cascade-disables Transcription because system_audio_capture becomes
unavailable.

────────────────────────────────────────
OVERVIEW
────────────────────────────────────────
Owns input audio device CRUD, WASAPI capture, and the InputAudio
resource. Exports system_audio_capture (IAudioCaptureProvider).

Has no LLM-callable tools. Agents use audio through Transcription
(tr_transcribe_*); System Audio provides the capture provider and
resource catalogue behind those tools.

────────────────────────────────────────
EXPORTED CONTRACTS
────────────────────────────────────────
system_audio_capture (IAudioCaptureProvider)
  Default impl: WasapiAudioCaptureProvider, multiplexed via
  SharedAudioCaptureManager so multiple consumers targeting the same
  device share a single capture session + ring buffer.

────────────────────────────────────────
RESOURCE TYPE
────────────────────────────────────────
InputAudio (InputAudioDB)
  Type code:  TrAudio
  Permission: AccessInputAudioAsync
  Grant:      per-device (or AllResources wildcard).

────────────────────────────────────────
CLI
────────────────────────────────────────
resource inputaudio add <name> [identifier] [description]
resource inputaudio get <id>
resource inputaudio list
resource inputaudio update <id> [name] [identifier]
resource inputaudio delete <id>
resource inputaudio sync            Import system input audio devices

Aliases: ia

────────────────────────────────────────
SEED DATA
────────────────────────────────────────
SeedDataAsync registers a default InputAudio device on first install.

────────────────────────────────────────
TOOLS
────────────────────────────────────────
None. The module is contract-and-resource-only.
