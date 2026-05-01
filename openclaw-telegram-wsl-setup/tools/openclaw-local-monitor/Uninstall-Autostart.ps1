$startup = [Environment]::GetFolderPath("Startup")
$removed = $false
foreach ($shortcut in Get-ChildItem -LiteralPath $startup -Filter "OpenClaw*.lnk" -ErrorAction SilentlyContinue) {
    Remove-Item -LiteralPath $shortcut.FullName
    Write-Host "Removed autostart shortcut:"
    Write-Host $shortcut.FullName
    $removed = $true
}
if (-not $removed) {
    Write-Host "Autostart shortcut was not present."
}
