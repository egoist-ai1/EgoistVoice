param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"
$package = [System.IO.Path]::GetFullPath($PackagePath)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$verifier = Join-Path $PSScriptRoot "Test-EgoistVoiceSingleFile.ps1"

if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "Full Offline package is missing: $package"
}
if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
    throw "Single-file verifier is missing: $verifier"
}

$verified = & $verifier -Path $package -PassThru
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Copy-ExactSegment(
    [System.IO.FileStream]$Source,
    [long]$Offset,
    [long]$Length,
    [string]$Destination,
    [string]$ExpectedHash
) {
    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        $existing = Get-Item -LiteralPath $Destination
        if ($existing.Length -eq $Length -and
            (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant() -eq $ExpectedHash) {
            return
        }
    }

    $temporary = "$Destination.$([guid]::NewGuid().ToString('N')).part"
    $Source.Position = $Offset
    $target = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $buffer = New-Object byte[] (1024 * 1024)
        $remaining = $Length
        while ($remaining -gt 0) {
            $wanted = [int][Math]::Min([long]$buffer.Length, $remaining)
            $read = $Source.Read($buffer, 0, $wanted)
            if ($read -le 0) { throw "Embedded payload ended early." }
            $target.Write($buffer, 0, $read)
            [void]$sha.TransformBlock($buffer, 0, $read, $null, 0)
            $remaining -= $read
        }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        $target.Flush($true)
        $actualHash = [System.BitConverter]::ToString($sha.Hash).Replace('-', '').ToLowerInvariant()
        if ($actualHash -ne $ExpectedHash) {
            throw "Extracted payload checksum mismatch: $Destination"
        }
    }
    catch {
        $target.Dispose()
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        $sha.Dispose()
        $target.Dispose()
    }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

$stream = [System.IO.File]::Open($package, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    foreach ($entry in @($verified.Entries)) {
        $destination = Join-Path $output ([string]$entry.Name)
        Copy-ExactSegment `
            -Source $stream `
            -Offset ([long]$entry.Offset) `
            -Length ([long]$entry.Length) `
            -Destination $destination `
            -ExpectedHash ([string]$entry.Sha256)
    }
}
finally {
    $stream.Dispose()
}

$result = [pscustomobject]@{
    SourcePackage = $package
    SourcePackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    LaunchFile = [string]$verified.LaunchFile
    ManifestSha256 = [string]$verified.ManifestSha256
    OutputDirectory = $output
    Files = @(
        foreach ($entry in @($verified.Entries)) {
            $file = Join-Path $output ([string]$entry.Name)
            [pscustomobject]@{
                Name = [string]$entry.Name
                Path = $file
                Length = [long]$entry.Length
                Sha256 = [string]$entry.Sha256
            }
        }
    )
}

if ($PassThru) { $result } else { $result.Files | Select-Object Path, Length, Sha256 }
