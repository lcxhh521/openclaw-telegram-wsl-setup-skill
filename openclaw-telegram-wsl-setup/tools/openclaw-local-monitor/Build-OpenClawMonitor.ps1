$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $scriptDir "OpenClawMonitor.cs"
$out = Join-Path $scriptDir "OpenClawMonitor.exe"
$icon = Join-Path $scriptDir "OpenClawMonitor.ico"

if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /win32icon:$icon `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    /out:$out `
    $src

Write-Host "Built $out"
