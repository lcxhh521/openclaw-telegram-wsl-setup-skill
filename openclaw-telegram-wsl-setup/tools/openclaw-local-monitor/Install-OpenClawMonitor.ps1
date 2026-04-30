$ErrorActionPreference = "Stop"

$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDir = Join-Path $env:LOCALAPPDATA "OpenClawMonitor"

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$files = @(
    "OpenClawMonitor.cs",
    "OpenClawMonitor.ico",
    "OpenClawMonitorIcon.png",
    "Build-OpenClawMonitor.ps1",
    "Install-Autostart.ps1",
    "Uninstall-Autostart.ps1"
)

foreach ($file in $files) {
    Copy-Item -Force -LiteralPath (Join-Path $sourceDir $file) -Destination $installDir
}

& (Join-Path $installDir "Build-OpenClawMonitor.ps1")
& (Join-Path $installDir "Install-Autostart.ps1")

$exe = Join-Path $installDir "OpenClawMonitor.exe"
Start-Process -FilePath $exe

Write-Host "OpenClaw Monitor installed and started:"
Write-Host $exe
