param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"
$file = [System.IO.Path]::GetFullPath($Path)
$magic = "EGOISTVOICEPKG01"
$footerSize = 60
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Read-Exact([System.IO.Stream]$Stream, [int]$Count) {
    $result = New-Object byte[] $Count
    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($result, $offset, $Count - $offset)
        if ($read -le 0) { throw "Unexpected end of embedded installer." }
        $offset += $read
    }
    return $result
}

function Read-Utf8String([System.IO.BinaryReader]$Reader) {
    $length = $Reader.ReadInt32()
    if ($length -le 0 -or $length -gt 512) { throw "Invalid embedded manifest string length." }
    return $utf8.GetString((Read-Exact $Reader.BaseStream $length))
}

function Get-SegmentHash([System.IO.FileStream]$Stream, [long]$Offset, [long]$Length) {
    $Stream.Position = $Offset
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $buffer = New-Object byte[] (1024 * 1024)
        $remaining = $Length
        while ($remaining -gt 0) {
            $wanted = [int][Math]::Min([long]$buffer.Length, $remaining)
            $read = $Stream.Read($buffer, 0, $wanted)
            if ($read -le 0) { throw "Embedded payload is truncated." }
            [void]$sha.TransformBlock($buffer, 0, $read, $null, 0)
            $remaining -= $read
        }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return [System.BitConverter]::ToString($sha.Hash).Replace("-", "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Installer is missing: $file" }
$stream = [System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    if ($stream.Length -lt $footerSize) { throw "Embedded installer footer is missing." }
    $stream.Position = $stream.Length - $footerSize
    $reader = New-Object System.IO.BinaryReader($stream, $utf8, $true)
    $actualMagic = [System.Text.Encoding]::ASCII.GetString((Read-Exact $stream 16))
    $manifestOffset = $reader.ReadInt64()
    $manifestLength = $reader.ReadInt32()
    $manifestHash = [System.BitConverter]::ToString((Read-Exact $stream 32)).Replace("-", "").ToLowerInvariant()
    $footerOffset = $stream.Length - $footerSize
    if ($actualMagic -ne $magic -or $manifestOffset -le 0 -or $manifestLength -le 0 -or
        $manifestOffset -gt ($footerOffset - $manifestLength)) {
        throw "Embedded installer footer is invalid."
    }
    $stream.Position = $manifestOffset
    $manifestBytes = Read-Exact $stream $manifestLength
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $actualManifestHash = [System.BitConverter]::ToString($sha.ComputeHash($manifestBytes)).Replace("-", "").ToLowerInvariant() } finally { $sha.Dispose() }
    if ($actualManifestHash -ne $manifestHash) { throw "Embedded manifest checksum mismatch." }

    $memory = New-Object System.IO.MemoryStream(,$manifestBytes)
    $manifestReader = New-Object System.IO.BinaryReader($memory, $utf8)
    try {
        if ($manifestReader.ReadInt32() -ne 1) { throw "Unsupported embedded manifest version." }
        $launchFile = Read-Utf8String $manifestReader
        $count = $manifestReader.ReadInt32()
        if ($count -lt 2 -or $count -gt 32) { throw "Invalid embedded file count." }
        $entries = @()
        $names = @{}
        for ($index = 0; $index -lt $count; $index++) {
            $name = Read-Utf8String $manifestReader
            $offset = $manifestReader.ReadInt64()
            $length = $manifestReader.ReadInt64()
            $expectedHash = [System.BitConverter]::ToString((Read-Exact $manifestReader.BaseStream 32)).Replace("-", "").ToLowerInvariant()
            if ([System.IO.Path]::GetFileName($name) -ne $name -or $names.ContainsKey($name.ToLowerInvariant()) -or
                $offset -lt 0 -or $length -le 0 -or $offset -gt ($manifestOffset - $length)) {
                throw "Unsafe embedded file entry: $name"
            }
            $names[$name.ToLowerInvariant()] = $true
            $entries += [pscustomobject]@{ Name = $name; Offset = [long]$offset; Length = [long]$length; Sha256 = $expectedHash }
        }
        if ($memory.Position -ne $memory.Length -or -not $names.ContainsKey($launchFile.ToLowerInvariant())) {
            throw "Embedded manifest identity is invalid."
        }
    } finally {
        $manifestReader.Dispose()
        $memory.Dispose()
    }

    $previousEnd = 0L
    foreach ($entry in @($entries | Sort-Object Offset)) {
        if ($entry.Offset -lt $previousEnd) { throw "Embedded payload entries overlap." }
        $previousEnd = $entry.Offset + $entry.Length
        $actualHash = Get-SegmentHash $stream $entry.Offset $entry.Length
        if ($actualHash -ne $entry.Sha256) { throw "Embedded payload checksum mismatch: $($entry.Name)" }
    }

    $result = [pscustomobject]@{
        Path = $file
        LaunchFile = $launchFile
        ManifestSha256 = $manifestHash
        EmbeddedPayloadBytes = [long](($entries | Measure-Object Length -Sum).Sum)
        Entries = $entries
    }
    if ($PassThru) { $result } else { Write-Output "single-file installer verified: $file" }
} finally {
    $stream.Dispose()
}
