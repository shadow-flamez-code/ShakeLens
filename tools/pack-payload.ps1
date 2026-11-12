# Packs the published application into a single container that the
# installer unpacks.  Format:
#
#   "SLP1"            magic
#   int32             number of files
#   per file:
#     int32           length of the name (UTF-8)
#     bytes           name (UTF-8, relative, backslashes)
#     int64           size of the uncompressed file
#     int32           size of the compressed block
#     bytes           raw deflate stream
#
param(
    [string]$Source = "$PSScriptRoot\..\publish",
    [string]$Out = "$PSScriptRoot\..\build\payload.bin"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

$source = (Resolve-Path $Source).Path
$files = Get-ChildItem $source -File | Sort-Object Name

$temp = [System.IO.Path]::GetTempFileName()
$outStream = [System.IO.File]::Create($temp)
$deflate = New-Object System.IO.Compression.DeflateStream($outStream, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$buffer = New-Object byte[] 65536

function Write-Bytes($stream, [byte[]]$bytes) { $stream.Write($bytes, 0, $bytes.Length) }
function Write-Int32($stream, [int]$value) {
    $b = [BitConverter]::GetBytes($value)
    Write-Bytes $stream $b
}
function Write-Int64($stream, [long]$value) {
    $b = [BitConverter]::GetBytes($value)
    Write-Bytes $stream $b
}

Write-Bytes $deflate ([System.Text.Encoding]::ASCII.GetBytes("SLP1"))
Write-Int32 $deflate $files.Count

foreach ($file in $files) {
    $name = [System.Text.Encoding]::UTF8.GetBytes($file.Name)
    Write-Int32 $deflate $name.Length
    Write-Bytes $deflate $name

    $raw = [System.IO.File]::ReadAllBytes($file.FullName)
    Write-Int64 $deflate $raw.Length

    $memory = New-Object System.IO.MemoryStream
    $compressor = New-Object System.IO.Compression.DeflateStream($memory, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $compressor.Write($raw, 0, $raw.Length)
    $compressor.Dispose()
    $compressed = $memory.ToArray()
    $memory.Dispose()

    Write-Int32 $deflate $compressed.Length
    Write-Bytes $deflate $compressed
}

$deflate.Dispose()
$outStream.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
Move-Item $temp $Out -Force

$mb = [Math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Output "packed $($files.Count) files -> $Out ($mb MB)"
