$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolSource = Join-Path $scriptDir "openclaw-doubao-asr"
if (-not (Test-Path -LiteralPath $toolSource)) {
  throw "Missing tool source: $toolSource"
}

$linuxHome = (wsl -d Ubuntu -- bash -lc 'printf "%s" "$HOME"')
if (-not $linuxHome) {
  throw "Could not resolve WSL home directory."
}

$targetDir = "\\wsl.localhost\Ubuntu$linuxHome\.local\bin"
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$target = Join-Path $targetDir "openclaw-doubao-asr"
Copy-Item -LiteralPath $toolSource -Destination $target -Force
wsl -d Ubuntu -- bash -lc 'chmod +x ~/.local/bin/openclaw-doubao-asr'

$configureScript = @'
set -euo pipefail
env_file="$HOME/.openclaw/secrets/volcengine.env"
mkdir -p "$HOME/.openclaw/secrets"
chmod 700 "$HOME/.openclaw" "$HOME/.openclaw/secrets"
touch "$env_file"
chmod 600 "$env_file"
upsert() {
  local key="$1"
  local value="$2"
  if grep -q "^${key}=" "$env_file"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$env_file"
  else
    printf '%s=%s\n' "$key" "$value" >> "$env_file"
  fi
}
upsert VOLCENGINE_ASR_RESOURCE_ID volc.bigasr.auc_turbo
upsert VOLCENGINE_ASR_ENDPOINT https://openspeech.bytedance.com/api/v3/auc/bigmodel/recognize/flash
upsert VOLCENGINE_ASR_MODEL_NAME bigmodel
upsert VOLCENGINE_STANDARD_RESOURCE_ID volc.seedasr.auc
upsert VOLCENGINE_STANDARD_SUBMIT_ENDPOINT https://openspeech.bytedance.com/api/v3/auc/bigmodel/submit
upsert VOLCENGINE_STANDARD_QUERY_ENDPOINT https://openspeech.bytedance.com/api/v3/auc/bigmodel/query
upsert VOLCENGINE_STANDARD_MODEL_NAME bigmodel
'@

$configureTarget = Join-Path $targetDir "configure-openclaw-doubao-asr.tmp.sh"
$configureBytes = [System.Text.Encoding]::UTF8.GetBytes(($configureScript -replace "`r`n", "`n"))
[System.IO.File]::WriteAllBytes($configureTarget, $configureBytes)
wsl -d Ubuntu -- bash -lc 'bash ~/.local/bin/configure-openclaw-doubao-asr.tmp.sh && rm -f ~/.local/bin/configure-openclaw-doubao-asr.tmp.sh'

wsl -d Ubuntu -- bash -lc '~/.local/bin/openclaw-doubao-asr --self-check'
