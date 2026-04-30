# OpenClaw Doubao ASR Tool

This is a small local WSL helper for Volcengine/Doubao recording-file ASR.

It is intentionally separate from OpenClaw's chat model routing:

- Ark/Doubao chat models can handle text fallback work.
- The recording-file ASR endpoint is a speech product and needs the `volc.bigasr.auc_turbo` resource.
- A general Volcengine API key is not enough if the ASR resource is not enabled for the account/project.

## Install

From Windows PowerShell:

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-doubao-asr
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-DoubaoAsrTool.ps1
```

The installer copies `openclaw-doubao-asr` to `~/.local/bin` inside Ubuntu and writes non-secret ASR defaults into:

```text
~/.openclaw/secrets/volcengine.env
```

It does not overwrite or print the API key.

## Usage

Check local configuration without uploading audio:

```bash
openclaw-doubao-asr --self-check
```

Transcribe a local file:

```bash
openclaw-doubao-asr /path/to/audio.wav --output result.json
openclaw-doubao-asr --text-only /path/to/audio.mp3
```

Running the transcription command uploads that audio file to Volcengine. Use it only after the user has approved that transfer.

## Defaults

```text
VOLCENGINE_ASR_RESOURCE_ID=volc.bigasr.auc_turbo
VOLCENGINE_ASR_ENDPOINT=https://openspeech.bytedance.com/api/v3/auc/bigmodel/recognize/flash
VOLCENGINE_ASR_MODEL_NAME=bigmodel
```

The key is read from one of:

```text
VOLCENGINE_ASR_API_KEY
VOLCANO_ENGINE_API_KEY
VOLCENGINE_API_KEY
DOUBAO_ASR_API_KEY
```

## Practical role

For OpenClaw workflows, treat this as the "audio to text" adapter:

1. Use Doubao ASR to transcribe local audio when Gemini native audio is unavailable.
2. Feed the transcript to Doubao text models or the current OpenClaw model for style analysis, taxonomy checks, or summary work.
3. Keep Gemini as the option for native audio understanding when that distinction matters.
