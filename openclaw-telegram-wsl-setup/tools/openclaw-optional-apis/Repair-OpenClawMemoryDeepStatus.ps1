param(
  [string]$Distro = "Ubuntu"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repairScript = Join-Path $scriptDir "repair-openclaw-memory-deep-status.py"
if (-not (Test-Path -LiteralPath $repairScript)) {
  throw "Missing repair script: $repairScript"
}

function ConvertTo-WslPath {
  param([Parameter(Mandatory = $true)][string]$WindowsPath)
  $full = (Resolve-Path -LiteralPath $WindowsPath).Path
  if ($full -match '^([A-Za-z]):\\(.*)$') {
    $drive = $Matches[1].ToLowerInvariant()
    $rest = $Matches[2] -replace '\\', '/'
    return "/mnt/$drive/$rest"
  }
  throw "Only local drive paths are supported: $full"
}

$wslPath = ConvertTo-WslPath $repairScript
Write-Host "Repairing OpenClaw memory deep-status probe in WSL distro: $Distro" -ForegroundColor Cyan
wsl -d $Distro -- python3 "$wslPath"
if ($LASTEXITCODE -ne 0) {
  throw "Repair failed with exit code $LASTEXITCODE"
}

Write-Host "Done. Re-run: openclaw memory status --deep --json" -ForegroundColor Green
