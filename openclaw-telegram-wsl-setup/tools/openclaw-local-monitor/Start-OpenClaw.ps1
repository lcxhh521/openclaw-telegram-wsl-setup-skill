$ErrorActionPreference = "SilentlyContinue"

$Distro = "Ubuntu"
$OpenClawUrl = "http://127.0.0.1:18789/chat?session=main"
$Wsl = Join-Path $env:WINDIR "System32\wsl.exe"

function Get-OpenClawDashboardUrl {
    param([string]$WslPath, [string]$WslDistro)

    $nodeScript = @'
import path from "node:path";
import { pathToFileURL } from "node:url";

const dist = process.env.OPENCLAW_DIST_DIR;
const mod = async (name) => import(pathToFileURL(path.join(dist, name)).href);
const { t: resolveGatewayAuthToken } = await mod("auth-token-resolution-D33ItfMH.js");
const { i: getRuntimeConfig } = await mod("io-CFdEhZuM.js");
const { u: resolveGatewayPort } = await mod("paths-B2cMK-wd.js");
const { t: resolveControlUiLinks } = await mod("control-ui-links-B5bsAmWy.js");

const cfg = getRuntimeConfig();
const resolvedToken = await resolveGatewayAuthToken({ cfg, env: process.env, envFallback: "always" });
const token = resolvedToken.token ?? "";
const bind = cfg.gateway?.bind ?? "loopback";
const links = resolveControlUiLinks({
  port: resolveGatewayPort(cfg),
  bind: bind === "lan" ? "loopback" : bind,
  customBindHost: cfg.gateway?.customBindHost,
  basePath: cfg.gateway?.controlUi?.basePath,
  tlsEnabled: cfg.gateway?.tls?.enabled === true
});

const url = token && !resolvedToken.secretRefConfigured
  ? `${links.httpUrl}#token=${encodeURIComponent(token)}`
  : links.httpUrl;
console.log(url);
'@

    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($nodeScript))
    $linuxJs = "/tmp/openclaw-dashboard-url-" + [Guid]::NewGuid().ToString("N") + ".mjs"
    $quotedLinuxJs = "'" + $linuxJs + "'"
    $quotedEncoded = "'" + $encoded + "'"
    $bash = 'tmp=' + $quotedLinuxJs + '; printf %s ' + $quotedEncoded + ' | base64 -d > "\$tmp"; root="$(npm root -g 2>/dev/null)/openclaw/dist"; [ -d "\$root" ] || root="\$HOME/.local/lib/node_modules/openclaw/dist"; OPENCLAW_DIST_DIR="\$root" node "\$tmp"; status=\$?; rm -f "\$tmp"; exit \$status'
    $url = & $WslPath -d $WslDistro -- bash -lc $bash 2>$null | Select-Object -Last 1
    if ($url -match '^https?://') { return $url.Trim() }
    return $null
}

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

$DashboardUrl = Get-OpenClawDashboardUrl -WslPath $Wsl -WslDistro $Distro
if ($DashboardUrl) {
    Start-Process $DashboardUrl
} else {
    Start-Process $OpenClawUrl
}
