$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "OpenClaw Doubao / Volcengine ASR Credentials" -ForegroundColor Cyan
Write-Host "Paste credentials from the Volcengine speech-service page."
Write-Host "Do NOT paste them into chat. Access Token will be hidden while typing."
Write-Host ""

$appKey = Read-Host "APP ID / App Key"
$accessSecure = Read-Host "Access Token / Access Key" -AsSecureString

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($accessSecure)
try {
  $accessKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
} finally {
  if ($bstr -ne [IntPtr]::Zero) {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
  }
}

if ([string]::IsNullOrWhiteSpace($appKey) -or [string]::IsNullOrWhiteSpace($accessKey)) {
  throw "APP ID/App Key and Access Token/Access Key are both required."
}

$linuxHome = (wsl -d Ubuntu -- bash -lc 'printf "%s" "$HOME"')
if (-not $linuxHome) {
  throw "Could not resolve WSL home directory."
}

$secretDir = "\\wsl.localhost\Ubuntu$linuxHome\.openclaw\secrets"
New-Item -ItemType Directory -Force -Path $secretDir | Out-Null
$envPath = Join-Path $secretDir "volcengine.env"
if (-not (Test-Path -LiteralPath $envPath)) {
  New-Item -ItemType File -Force -Path $envPath | Out-Null
}

$lines = Get-Content -LiteralPath $envPath -ErrorAction SilentlyContinue
$updates = [ordered]@{
  "VOLCENGINE_ASR_APP_KEY" = $appKey.Trim()
  "VOLCENGINE_ASR_ACCESS_KEY" = $accessKey
  "VOLCENGINE_STANDARD_APP_KEY" = $appKey.Trim()
  "VOLCENGINE_STANDARD_ACCESS_KEY" = $accessKey
}

$out = New-Object System.Collections.Generic.List[string]
$seen = @{}
foreach ($line in $lines) {
  $matched = $false
  foreach ($key in $updates.Keys) {
    if ($line -match "^$([regex]::Escape($key))=") {
      $out.Add("$key=$($updates[$key])")
      $seen[$key] = $true
      $matched = $true
      break
    }
  }
  if (-not $matched) {
    $out.Add($line)
  }
}
foreach ($key in $updates.Keys) {
  if (-not $seen.ContainsKey($key)) {
    $out.Add("$key=$($updates[$key])")
  }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($envPath, (($out -join "`n") + "`n"), $utf8NoBom)
wsl -d Ubuntu -- bash -lc 'chmod 700 ~/.openclaw ~/.openclaw/secrets; chmod 600 ~/.openclaw/secrets/volcengine.env; ~/.local/bin/openclaw-doubao-asr --self-check'

Write-Host ""
Write-Host "Done. You can close this window." -ForegroundColor Green
Write-Host "Press Enter to close:"
[void][Console]::ReadLine()
