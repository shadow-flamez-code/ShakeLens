# Extracts the 32 px frames of the Azure Chrome Aura animated cursor
# and turns them into the assets used by ShakeLens.
param(
    [string]$Ani = "$env:TEMP\aura_cursor\Azure Chrome Aura Cursor Set - Extra Azure Chrome _32-48-64.ani",
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "Assets\Cursor")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
Get-ChildItem $OutDir -Filter "frame_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force

$b = [System.IO.File]::ReadAllBytes($Ani)

function Get-Chunk([byte[]]$data, [int]$offset, [int]$end, [string]$id) {
    while ($offset + 8 -le $end) {
        $cid = [System.Text.Encoding]::ASCII.GetString($data, $offset, 4)
        $size = [BitConverter]::ToInt32($data, $offset + 4)
        if ($cid -eq $id) { return @{ Data = $offset + 8; Size = $size } }
        $offset += 8 + $size + ($size % 2)
    }
    return $null
}

# the frame rate
$rateChunk = Get-Chunk $b 12 $b.Length "rate"
$frameDelay = 83
if ($rateChunk) {
    # the rate chunk counts jiffies (1/60 s)
    $delay = [BitConverter]::ToInt32($b, $rateChunk.Data)
    if ($delay -gt 0) { $frameDelay = [int][Math]::Round($delay * 1000.0 / 60.0) }
}

# the frames
$frames = @()
$offset = 12
while ($offset + 8 -le $b.Length) {
    $id = [System.Text.Encoding]::ASCII.GetString($b, $offset, 4)
    $size = [BitConverter]::ToInt32($b, $offset + 4)
    if ($id -eq "LIST" -and [System.Text.Encoding]::ASCII.GetString($b, $offset + 8, 4) -eq "fram") {
        $listEnd = $offset + 8 + $size
        $o = $offset + 12
        while ($o + 8 -le $listEnd) {
            $cid = [System.Text.Encoding]::ASCII.GetString($b, $o, 4)
            $csz = [BitConverter]::ToInt32($b, $o + 4)
            if ($cid -eq "icon") {
                $cd = $o + 8
                $count = [BitConverter]::ToUInt16($b, $cd + 4)
                for ($e = 0; $e -lt $count; $e++) {
                    $eo = $cd + 6 + $e * 16
                    $w = $b[$eo]; $h = $b[$eo + 1]
                    # this writer stores the hotspot where the plane count would be
                    $hx = [BitConverter]::ToUInt16($b, $eo + 4)
                    $hy = [BitConverter]::ToUInt16($b, $eo + 6)
                    $bytes = [BitConverter]::ToUInt32($b, $eo + 8)
                    $imgOff = [BitConverter]::ToUInt32($b, $eo + 12)
                    if ($w -ne 32 -or $h -ne 32) { continue }
                    $png = New-Object byte[] $bytes
                    [Array]::Copy($b, $cd + $imgOff, $png, 0, $bytes)
                    $frames += [pscustomobject]@{ Png = $png; HotspotX = $hx; HotspotY = $hy }
                }
            }
            $o += 8 + $csz + ($csz % 2)
        }
        break
    }
    $offset += 8 + $size + ($size % 2)
}

Write-Output "frames: $($frames.Count)  delay: $frameDelay ms"

$manifest = @()
for ($i = 0; $i -lt $frames.Count; $i++) {
    $path = Join-Path $OutDir ("frame_{0:D2}.png" -f $i)
    [System.IO.File]::WriteAllBytes($path, $frames[$i].Png)
    $manifest += [pscustomobject]@{
        file    = "frame_{0:D2}.png" -f $i
        delay   = $frameDelay
        hotspot = @($frames[$i].HotspotX, $frames[$i].HotspotY)
    }
}

$manifest | ConvertTo-Json -Depth 4 |
    Set-Content -Path (Join-Path $OutDir "frames.json") -Encoding UTF8

Write-Output "hotspot of frame 0: $($frames[0].HotspotX),$($frames[0].HotspotY)"
Write-Output "written to $OutDir"
