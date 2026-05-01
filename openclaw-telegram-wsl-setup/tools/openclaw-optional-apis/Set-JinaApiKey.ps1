$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "OpenClaw Jina Embeddings API Key Setup"

Write-Host ""
Write-Host "OpenClaw Jina Embeddings API Key Setup" -ForegroundColor Cyan
Write-Host "Optional: save a Jina API key for OpenClaw memory_search embeddings."
Write-Host "The key will not be shown while typing. Do NOT paste it into chat."
Write-Host "This will not restart the gateway unless you choose to."
Write-Host ""

$openPage = Read-Host "Open Jina Embeddings page now? [Y/n]"
if ($openPage -notmatch '^(n|N)$') {
  Start-Process "https://jina.ai/en-US/embeddings/"
  Write-Host ""
  Write-Host "In the browser: sign in, create/copy your API key, then return here." -ForegroundColor Yellow
}

$secure = Read-Host "Jina API key" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$plain = $null

try {
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  if ([string]::IsNullOrWhiteSpace($plain)) {
    throw "Empty Jina API key."
  }

  $scriptPath = Join-Path $PSScriptRoot "save-openclaw-jina-key.sh"
  if (!(Test-Path -LiteralPath $scriptPath)) {
    throw "Missing helper script: $scriptPath"
  }

  $wslScript = "/home/$((wsl -d Ubuntu -- bash -lc 'id -un').Trim())/save-openclaw-jina-key.sh"
  Get-Content -LiteralPath $scriptPath -Raw | wsl -d Ubuntu -- bash -lc "cat > '$wslScript' && chmod 700 '$wslScript'"
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to copy helper script into WSL."
  }

  $plain | wsl -d Ubuntu -- bash -lc "'$wslScript'"
  if ($LASTEXITCODE -ne 0) {
    throw "OpenClaw Jina setup failed."
  }

  Write-Host ""
  Write-Host "Jina key saved and OpenClaw memorySearch config updated." -ForegroundColor Green
  $restart = Read-Host "Restart OpenClaw gateway now? [y/N]"
  if ($restart -match '^(y|yes)$') {
    wsl -d Ubuntu -- bash -lc "systemctl --user restart openclaw-gateway.service && sleep 8 && openclaw gateway probe"
    if ($LASTEXITCODE -ne 0) {
      throw "Gateway restart/probe failed. Leave this window open and tell Codex."
    }
    Write-Host "Gateway restarted and probe succeeded." -ForegroundColor Green
  } else {
    Write-Host "Skipped restart. The next gateway restart or reboot will apply Jina." -ForegroundColor Yellow
  }
}
catch {
  Write-Host ""
  Write-Host "Setup failed. Leave this window open and tell Codex." -ForegroundColor Red
  Write-Host $_.Exception.Message -ForegroundColor Red
  exit 1
}
finally {
  if ($bstr -ne [IntPtr]::Zero) {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
  }
  $plain = $null
}

Write-Host ""
Read-Host "Press Enter to close"
