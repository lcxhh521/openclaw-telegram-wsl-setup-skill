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
    "Uninstall-Autostart.ps1",
    "Start-OpenClaw.ps1",
    "Start-OpenClaw.cmd"
)

foreach ($file in $files) {
    Copy-Item -Force -LiteralPath (Join-Path $sourceDir $file) -Destination $installDir
}

& (Join-Path $installDir "Build-OpenClawMonitor.ps1")
& (Join-Path $installDir "Install-Autostart.ps1")

$exe = Join-Path $installDir "OpenClawMonitor.exe"
$startScript = Join-Path $installDir "Start-OpenClaw.ps1"
$powershell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
$icon = Join-Path $installDir "OpenClawMonitor.ico"
$desktop = [Environment]::GetFolderPath("Desktop")
$programs = [Environment]::GetFolderPath("Programs")
$shell = New-Object -ComObject WScript.Shell

foreach ($shortcutPath in @(
    (Join-Path $desktop "OpenClaw Monitor.lnk"),
    (Join-Path $programs "OpenClaw Monitor.lnk")
)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $installDir
    if (Test-Path -LiteralPath $icon) { $shortcut.IconLocation = $icon }
    $shortcut.Description = "OpenClaw local monitor panel"
    $shortcut.Save()
}

foreach ($shortcutPath in @(
    (Join-Path $desktop "OpenClaw 启动.lnk"),
    (Join-Path $programs "OpenClaw 启动.lnk")
)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $powershell
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$startScript`""
    $shortcut.WorkingDirectory = $installDir
    if (Test-Path -LiteralPath $icon) { $shortcut.IconLocation = $icon }
    $shortcut.Description = "Start OpenClaw gateway and open the local page"
    $shortcut.Save()
}

Start-Process -FilePath $exe

Write-Host "OpenClaw Monitor installed and started:"
Write-Host $exe
