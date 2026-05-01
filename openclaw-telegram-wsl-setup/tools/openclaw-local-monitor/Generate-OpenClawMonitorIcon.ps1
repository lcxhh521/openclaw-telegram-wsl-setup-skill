$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pngPath = Join-Path $scriptDir "OpenClawMonitorIcon.png"
$icoPath = Join-Path $scriptDir "OpenClawMonitor.ico"

Add-Type -AssemblyName System.Drawing

function New-Canvas {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Draw-CuteOpenClawIcon {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Size
    )

    $s = [float]$Size / 256.0
    function S([float]$v) { return [int][Math]::Round($v * $s) }

    $red = [System.Drawing.Color]::FromArgb(240, 52, 58)
    $redDark = [System.Drawing.Color]::FromArgb(197, 36, 44)
    $redLight = [System.Drawing.Color]::FromArgb(255, 92, 93)
    $eye = [System.Drawing.Color]::FromArgb(19, 214, 221)
    $ink = [System.Drawing.Color]::FromArgb(13, 22, 31)

    $redBrush = New-Object System.Drawing.SolidBrush $red
    $darkBrush = New-Object System.Drawing.SolidBrush $redDark
    $eyeBrush = New-Object System.Drawing.SolidBrush $eye
    $inkBrush = New-Object System.Drawing.SolidBrush $ink
    $pen = New-Object System.Drawing.Pen $redLight, (S 7)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    try {
        # Antennae
        $Graphics.DrawBezier($pen, (S 93), (S 58), (S 76), (S 28), (S 58), (S 41), (S 52), (S 44))
        $Graphics.DrawBezier($pen, (S 163), (S 58), (S 180), (S 28), (S 198), (S 41), (S 204), (S 44))

        # Side claws / cheeks
        $Graphics.FillEllipse($redBrush, (S 28), (S 111), (S 45), (S 52))
        $Graphics.FillEllipse($redBrush, (S 183), (S 111), (S 45), (S 52))
        $Graphics.FillEllipse($redBrush, (S 48), (S 48), (S 160), (S 160))

        # Tiny lower legs
        $Graphics.FillRectangle($darkBrush, (S 92), (S 198), (S 18), (S 34))
        $Graphics.FillRectangle($darkBrush, (S 146), (S 198), (S 18), (S 34))
        $Graphics.FillEllipse($darkBrush, (S 92), (S 222), (S 18), (S 11))
        $Graphics.FillEllipse($darkBrush, (S 146), (S 222), (S 18), (S 11))

        # Friendly eyes
        $Graphics.FillEllipse($inkBrush, (S 91), (S 95), (S 24), (S 24))
        $Graphics.FillEllipse($inkBrush, (S 141), (S 95), (S 24), (S 24))
        $Graphics.FillEllipse($eyeBrush, (S 97), (S 99), (S 11), (S 11))
        $Graphics.FillEllipse($eyeBrush, (S 147), (S 99), (S 11), (S 11))

    }
    finally {
        $redBrush.Dispose()
        $darkBrush.Dispose()
        $eyeBrush.Dispose()
        $inkBrush.Dispose()
        $pen.Dispose()
    }
}

function Save-PngBytes {
    param([int]$Size)
    $pair = New-Canvas -Size $Size
    $bmp = $pair[0]
    $g = $pair[1]
    try {
        Draw-CuteOpenClawIcon -Graphics $g -Size $Size
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

[System.IO.File]::WriteAllBytes($pngPath, (Save-PngBytes -Size 256))

$pair = New-Canvas -Size 256
$bmp = $pair[0]
$g = $pair[1]
try {
    Draw-CuteOpenClawIcon -Graphics $g -Size 256
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $fs = [System.IO.File]::Create($icoPath)
    try {
        $icon.Save($fs)
    }
    finally {
        $fs.Dispose()
        $icon.Dispose()
    }
}
finally {
    $g.Dispose()
    $bmp.Dispose()
}

Write-Host "Generated transparent OpenClaw icon:"
Write-Host $pngPath
Write-Host $icoPath
