$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "OpenClaw Tavily API Key Setup"

Write-Host ""
Write-Host "OpenClaw Tavily API Key Setup" -ForegroundColor Cyan
Write-Host "Optional: save a Tavily API key for OpenClaw web_search."
Write-Host "The key will not be shown while typing. Do NOT paste it into chat."
Write-Host "This will not restart the gateway unless you choose to."
Write-Host ""

$secure = Read-Host "Tavily API key" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$plain = $null

try {
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  if ([string]::IsNullOrWhiteSpace($plain)) {
    throw "Empty Tavily API key."
  }

  $scriptPath = Join-Path $PSScriptRoot "save-openclaw-tavily-key.sh"
  if (!(Test-Path -LiteralPath $scriptPath)) {
    throw "Missing helper script: $scriptPath"
  }

  $wslScript = "/home/$((wsl -d Ubuntu -- bash -lc 'id -un').Trim())/save-openclaw-tavily-key.sh"
  Get-Content -LiteralPath $scriptPath -Raw | wsl -d Ubuntu -- bash -lc "cat > '$wslScript' && chmod 700 '$wslScript'"
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to copy helper script into WSL."
  }

  $plain | wsl -d Ubuntu -- bash -lc "'$wslScript'"
  if ($LASTEXITCODE -ne 0) {
    throw "OpenClaw Tavily setup failed."
  }

  Write-Host ""
  Write-Host "Tavily key saved and OpenClaw web_search config updated." -ForegroundColor Green
  $restart = Read-Host "Restart OpenClaw gateway now? [y/N]"
  if ($restart -match '^(y|yes)$') {
    wsl -d Ubuntu -- bash -lc "systemctl --user restart openclaw-gateway.service && sleep 8 && openclaw gateway probe"
    if ($LASTEXITCODE -ne 0) {
      throw "Gateway restart/probe failed. Leave this window open and tell Codex."
    }
    Write-Host "Gateway restarted and probe succeeded." -ForegroundColor Green
  } else {
    Write-Host "Skipped restart. The next gateway restart or reboot will apply Tavily." -ForegroundColor Yellow
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
