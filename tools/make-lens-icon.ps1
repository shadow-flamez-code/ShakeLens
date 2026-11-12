# Draws the Google Lens logo and writes it as a multi size .ico
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "Assets\ShakeLens.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-LensBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $cx = 110.0 * $s
    $cy = 107.0 * $s
    $ro = 87.0 * $s          # outer radius of the ring
    $ri = 48.0 * $s          # inner radius of the ring
    $rm = ($ro + $ri) / 2    # middle of the stroke

    $red = [System.Drawing.Color]::FromArgb(255, 234, 67, 53)
    $yellow = [System.Drawing.Color]::FromArgb(255, 251, 188, 5)
    $green = [System.Drawing.Color]::FromArgb(255, 52, 168, 83)
    $blue = [System.Drawing.Color]::FromArgb(255, 66, 133, 244)

    # a filled ring segment between two screen angles (0 = 3 o'clock, clockwise)
    function New-ArcPoints([double]$from, [double]$to, [int]$steps) {
        $points = New-Object System.Collections.Generic.List[System.Drawing.PointF]
        for ($i = 0; $i -le $steps; $i++) {
            $t = ($from + ($to - $from) * $i / $steps) * [Math]::PI / 180.0
            $points.Add((New-Object System.Drawing.PointF (($cx + $ro * [Math]::Cos($t))), (($cy + $ro * [Math]::Sin($t)))))
        }
        for ($i = $steps; $i -ge 0; $i--) {
            $t = ($from + ($to - $from) * $i / $steps) * [Math]::PI / 180.0
            $points.Add((New-Object System.Drawing.PointF (($cx + $ri * [Math]::Cos($t))), (($cy + $ri * [Math]::Sin($t)))))
        }
        return $points.ToArray()
    }

    # red: 3 -> 12 o'clock, yellow: 12 -> 9, green: 9 -> 6, blue: 6 -> 3
    $segments = @(
        @{ color = $red;    from = 0;   to = -90 },
        @{ color = $yellow; from = -90; to = -180 },
        @{ color = $green;  from = 180; to = 90 },
        @{ color = $blue;   from = 90;  to = 0 }
    )
    foreach ($segment in $segments) {
        $brush = New-Object System.Drawing.SolidBrush $segment.color
        $g.FillPolygon($brush, (New-ArcPoints $segment.from $segment.to 40))
        $brush.Dispose()
    }

    # the handle grows out of the blue segment towards the lower right
    $angle = 45 * [Math]::PI / 180.0
    $x1 = $cx + [Math]::Cos($angle) * $rm
    $y1 = $cy + [Math]::Sin($angle) * $rm
    $x2 = $cx + [Math]::Cos($angle) * 179.0 * $s
    $y2 = $cy + [Math]::Sin($angle) * 179.0 * $s
    $pen = New-Object System.Drawing.Pen $blue, (($ro - $ri))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, $x1, $y1, $x2, $y2)
    $pen.Dispose()

    $g.Dispose()
    return $bmp
}

function Get-BgraBytes([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    return $bytes
}

# build the .ico with classic bitmap entries
$sizes = @(16, 32, 48, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = New-LensBitmap $size
    $bgra = Get-BgraBytes $bmp

    # bottom up rows
    $xor = New-Object byte[] ($bgra.Length)
    $stride = $size * 4
    for ($y = 0; $y -lt $size; $y++) {
        [Array]::Copy($bgra, ($size - 1 - $y) * $stride, $xor, $y * $stride, $stride)
    }

    # 1 bit mask, rows padded to 4 bytes
    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $mask = New-Object byte[] ($maskStride * $size)

    $header = New-Object byte[] 40
    [Array]::Copy([BitConverter]::GetBytes([int]40), 0, $header, 0, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]$size), 0, $header, 4, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]($size * 2)), 0, $header, 8, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]1), 0, $header, 12, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]32), 0, $header, 14, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]($xor.Length + $mask.Length)), 0, $header, 20, 4)

    $blob = New-Object byte[] (40 + $xor.Length + $mask.Length)
    [Array]::Copy($header, 0, $blob, 0, 40)
    [Array]::Copy($xor, 0, $blob, 40, $xor.Length)
    [Array]::Copy($mask, 0, $blob, 40 + $xor.Length, $mask.Length)

    $images += [pscustomobject]@{ Size = $size; Data = $blob }
    $bmp.Dispose()
}

# 256 px entries have to be stored as PNG, the classic bitmap format is not
# accepted for that size any more
$big = New-LensBitmap 256
$memory = New-Object System.IO.MemoryStream
$big.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
$images += [pscustomobject]@{ Size = 256; Data = $memory.ToArray(); Png = $true }
$memory.Dispose()

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $writer.Write([byte]($image.Size -band 0xFF))
    $writer.Write([byte]($image.Size -band 0xFF))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$image.Data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $image.Data.Length
}
foreach ($image in $images) { $writer.Write($image.Data) }
$writer.Flush()

[System.IO.File]::WriteAllBytes($Out, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()

Write-Output "wrote $Out ($((Get-Item $Out).Length) bytes) with $($images.Count) sizes"

# a preview for the eye
$preview = New-LensBitmap 256
$preview.Save((Join-Path $env:TEMP "lens_icon_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Output "preview: $env:TEMP\lens_icon_preview.png"
