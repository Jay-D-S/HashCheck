#Requires -Version 5.1
<#
.SYNOPSIS
    Simulates bit rot by flipping one byte in a file without changing timestamps or attributes.

.PARAMETER Path
    Path to the file to corrupt.

.PARAMETER Offset
    Byte offset to corrupt. Defaults to the middle of the file.
    Accepts decimal or hex (e.g. 0x1A4).

.EXAMPLE
    .\Corrupt-Byte.ps1 "D:\Photos\IMG_001.jpg"

.EXAMPLE
    .\Corrupt-Byte.ps1 "D:\Photos\IMG_001.jpg" -Offset 0x100
#>
param(
    [Parameter(Mandatory)]
    [string]$Path,

    [Parameter()]
    [long]$Offset = -1
)

$ErrorActionPreference = 'Stop'

$resolved = Resolve-Path $Path
$filePath  = $resolved.ProviderPath

if (-not (Test-Path $filePath -PathType Leaf)) {
    Write-Error "Not a file: $filePath"
    exit 1
}

$item = Get-Item -LiteralPath $filePath

# Snapshot every timestamp before touching anything
$created    = $item.CreationTimeUtc
$modified   = $item.LastWriteTimeUtc
$accessed   = $item.LastAccessTimeUtc
$attributes = $item.Attributes

$size = $item.Length
if ($size -eq 0) {
    Write-Error "File is empty — nothing to corrupt."
    exit 1
}

# Default to the middle of the file
if ($Offset -lt 0) { $Offset = [long]($size / 2) }

if ($Offset -ge $size) {
    Write-Error "Offset $Offset is beyond end of file (size $size)."
    exit 1
}

# Open with FileShare.None to ensure exclusive access, then flip the byte
$stream = [System.IO.File]::Open($filePath, 'Open', 'ReadWrite', 'None')
try {
    [void]$stream.Seek($Offset, 'Begin')
    $original = $stream.ReadByte()
    $flipped  = $original -bxor 0xFF
    [void]$stream.Seek($Offset, 'Begin')
    $stream.WriteByte([byte]$flipped)
    $stream.Flush()
}
finally {
    $stream.Dispose()
}

# Restore all timestamps exactly as they were
$item.CreationTimeUtc   = $created
$item.LastWriteTimeUtc  = $modified
$item.LastAccessTimeUtc = $accessed

# Restore attributes (read-only, hidden, etc.) in case WriteByte changed anything
$item.Attributes = $attributes

Write-Host ""
Write-Host "Corrupted: $filePath"
Write-Host "  Offset : $Offset (0x$($Offset.ToString('X')))"
Write-Host "  Before : 0x$($original.ToString('X2'))"
Write-Host "  After  : 0x$($flipped.ToString('X2'))"
Write-Host "  Size   : $size bytes (unchanged)"
Write-Host "  Mtime  : $($item.LastWriteTime) (restored)"
Write-Host ""
Write-Host "Run HashCheck validation to detect this as 'Corrupted' (hash differs, size+mtime unchanged)."
