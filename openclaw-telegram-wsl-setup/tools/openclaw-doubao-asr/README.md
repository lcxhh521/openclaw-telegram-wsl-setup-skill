# OpenClaw Doubao ASR Tool

This is a small local WSL helper for Volcengine/Doubao recording-file ASR.

It is intentionally separate from OpenClaw's chat model routing:

- Ark/Doubao chat models can handle text fallback work.
- Flash ASR needs the `volc.bigasr.auc_turbo` resource.
- Standard ASR needs a standard recording-file resource, defaulting here to `volc.seedasr.auc`.
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
openclaw-doubao-asr --mode flash /path/to/audio.wav --output result.json
openclaw-doubao-asr --mode flash --text-only /path/to/audio.mp3
```

Submit a standard recording-file job from a public URL:

```bash
openclaw-doubao-asr --mode standard --url "https://example.com/audio.wav" --output standard-submit.json
openclaw-doubao-asr --mode standard --url "https://example.com/audio.wav" --wait --output standard-result.json
openclaw-doubao-asr --mode standard --query "<request-id>" --output standard-query.json
```

Flash mode uploads the local audio file to Volcengine.
Standard mode sends the audio URL to Volcengine; the URL must be reachable by Volcengine servers.
Use either mode only after the user has approved that transfer.

## Defaults

```text
VOLCENGINE_ASR_RESOURCE_ID=volc.bigasr.auc_turbo
VOLCENGINE_ASR_ENDPOINT=https://openspeech.bytedance.com/api/v3/auc/bigmodel/recognize/flash
VOLCENGINE_ASR_MODEL_NAME=bigmodel
VOLCENGINE_STANDARD_RESOURCE_ID=volc.seedasr.auc
VOLCENGINE_STANDARD_SUBMIT_ENDPOINT=https://openspeech.bytedance.com/api/v3/auc/bigmodel/submit
VOLCENGINE_STANDARD_QUERY_ENDPOINT=https://openspeech.bytedance.com/api/v3/auc/bigmodel/query
VOLCENGINE_STANDARD_MODEL_NAME=bigmodel
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
4. Use flash mode for short local clips, and standard mode for longer audio that already has a public object-storage URL.
