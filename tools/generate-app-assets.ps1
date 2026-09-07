# Generates the shared three-provider brand mark used by the tray executable
# and the MSIX logos. The ICO stores PNG frames at the standard shell sizes.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$trayAssets = Join-Path $root "src\BuildnBits.Usage.Tray\Assets"
$packageImages = Join-Path $root "src\BuildnBits.Usage.Package\Images"
New-Item -ItemType Directory -Force -Path $trayAssets, $packageImages | Out-Null

function New-BrandBitmap([int] $size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = [System.Drawing.Color]::FromArgb(255, 18, 21, 26)
    $brush = [System.Drawing.SolidBrush]::new($background)
    $radius = [float]($size * 0.22)
    $graphics.FillRectangle($brush, [System.Drawing.RectangleF]::new($radius, 0, $size - 2 * $radius, $size))
    $graphics.FillRectangle($brush, [System.Drawing.RectangleF]::new(0, $radius, $size, $size - 2 * $radius))
    $graphics.FillEllipse($brush, [System.Drawing.RectangleF]::new(0, 0, 2 * $radius, 2 * $radius))
    $graphics.FillEllipse($brush, [System.Drawing.RectangleF]::new($size - 2 * $radius, 0, 2 * $radius, 2 * $radius))
    $graphics.FillEllipse($brush, [System.Drawing.RectangleF]::new(0, $size - 2 * $radius, 2 * $radius, 2 * $radius))
    $graphics.FillEllipse($brush, [System.Drawing.RectangleF]::new($size - 2 * $radius, $size - 2 * $radius, 2 * $radius, 2 * $radius))
    $brush.Dispose()

    $marks = @(
        @{ Color = [System.Drawing.Color]::FromArgb(255, 16, 163, 127); X1 = 0.28; Y1 = 0.70; X2 = 0.40; Y2 = 0.30 },
        @{ Color = [System.Drawing.Color]::FromArgb(255, 200, 80, 40); X1 = 0.46; Y1 = 0.70; X2 = 0.58; Y2 = 0.30 },
        @{ Color = [System.Drawing.Color]::FromArgb(255, 66, 133, 244); X1 = 0.64; Y1 = 0.70; X2 = 0.76; Y2 = 0.30 }
    )
    $width = [float]($size * 0.13)
    foreach ($mark in $marks) {
        $pen = [System.Drawing.Pen]::new($mark.Color, $width)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine(
            $pen,
            [float]($size * $mark.X1),
            [float]($size * $mark.Y1),
            [float]($size * $mark.X2),
            [float]($size * $mark.Y2))
        $pen.Dispose()
    }

    $graphics.Dispose()
    return $bitmap
}

function Save-BrandPng([int] $size, [string] $path) {
    $bitmap = New-BrandBitmap $size
    try {
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function New-BrandIco([string] $path) {
    $sizes = @(16, 24, 32, 48, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = New-BrandBitmap $size
        $memory = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,([pscustomobject]@{
                Size = $size
                Data = $memory.ToArray()
            })
        }
        finally {
            $memory.Dispose()
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Data.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Data.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Data)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

New-BrandIco (Join-Path $trayAssets "app.ico")
Save-BrandPng 50 (Join-Path $packageImages "StoreLogo.png")
Save-BrandPng 44 (Join-Path $packageImages "Square44x44Logo.png")
Save-BrandPng 150 (Join-Path $packageImages "Square150x150Logo.png")

Write-Host "Generated $trayAssets\app.ico and MSIX provider logos."
