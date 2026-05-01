$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $scriptDir "OpenClawMonitor.exe"
$icon = Join-Path $scriptDir "OpenClawMonitor.ico"
$startup = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startup "OpenClaw Control.lnk"

Get-ChildItem -LiteralPath $startup -Filter "OpenClaw*.lnk" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.Arguments = ""
$shortcut.WorkingDirectory = $scriptDir
$shortcut.Description = "OpenClaw local control center"
if (Test-Path -LiteralPath $icon) { $shortcut.IconLocation = "$icon,0" }
$shortcut.Save()

Write-Host "Installed autostart shortcut:"
Write-Host $shortcutPath
