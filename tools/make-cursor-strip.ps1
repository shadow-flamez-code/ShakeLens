# Builds a strip of the cursor animation frames for the readme.
param(
    [string]$Dir = "$PSScriptRoot\..\Assets\Cursor",
    [string]$Out = "$PSScriptRoot\..\docs\cursor.png",
    [int]$Scale = 4
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$frames = Get-ChildItem $Dir -Filter "frame_*.png" | Sort-Object Name
$pick = @()
for ($i = 0; $i -lt $frames.Count; $i += 5) { $pick += $frames[$i] }

$size = 32 * $Scale
$gap = 10
$width = ($pick.Count * $size) + (($pick.Count - 1) * $gap) + 40
$height = $size + 40

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(0x1E, 0x23, 0x2E))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$x = 20
foreach ($file in $pick) {
    $img = [System.Drawing.Image]::FromFile($file.FullName)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle $x, 20, $size, $size))
    $img.Dispose()
    $x += $size + $gap
}

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("wrote {0} ({1} frames)" -f $Out, $pick.Count)
