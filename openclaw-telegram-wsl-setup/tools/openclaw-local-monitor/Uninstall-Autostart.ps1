$startup = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startup "OpenClaw Monitor.lnk"
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath
    Write-Host "Removed autostart shortcut:"
    Write-Host $shortcutPath
} else {
    Write-Host "Autostart shortcut was not present."
}
