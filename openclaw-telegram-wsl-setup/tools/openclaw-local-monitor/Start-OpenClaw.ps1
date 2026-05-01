$ErrorActionPreference = "SilentlyContinue"

$Distro = "Ubuntu"
$OpenClawUrl = "http://127.0.0.1:18789/chat?session=main"
$Wsl = Join-Path $env:WINDIR "System32\wsl.exe"

if (-not (Test-Path -LiteralPath $Wsl)) {
    Start-Process $OpenClawUrl
    exit 0
}

$keepalive = & $Wsl -d $Distro -- bash -lc "pgrep -af 'openclaw-manual-keepalive' >/dev/null 2>&1 && echo yes || true" 2>$null
if ($keepalive -notmatch "yes") {
    $bash = "systemctl --user start openclaw-gateway.service >/dev/null 2>&1 || true; openclaw gateway probe >/dev/null 2>&1 || true; exec -a openclaw-manual-keepalive sleep infinity"
    Start-Process -FilePath $Wsl -ArgumentList @("-d", $Distro, "--", "bash", "-lc", $bash) -WindowStyle Hidden
}

for ($i = 0; $i -lt 12; $i++) {
    $probe = & $Wsl -d $Distro -- bash -lc "openclaw gateway probe >/dev/null 2>&1 && echo ok || true" 2>$null
    if ($probe -match "ok") { break }
    Start-Sleep -Seconds 1
}

Start-Process $OpenClawUrl
