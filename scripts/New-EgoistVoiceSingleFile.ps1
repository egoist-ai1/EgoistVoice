param(
    [Parameter(Mandatory = $true)]
    [string]$StubPath,
    [Parameter(Mandatory = $true)]
    [string[]]$PayloadFiles,
    [Parameter(Mandatory = $true)]
    [string]$LaunchFileName,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$magic = [System.Text.Encoding]::ASCII.GetBytes("EGOISTVOICEPKG01")
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Write-Utf8String([System.IO.BinaryWriter]$Writer, [string]$Value) {
    $bytes = $utf8.GetBytes($Value)
    $Writer.Write([int]$bytes.Length)
    $Writer.Write($bytes)
}

function Copy-HashedFile([string]$Source, [System.IO.FileStream]$Destination) {
    $sourceStream = [System.IO.File]::Open($Source, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $buffer = New-Object byte[] (1024 * 1024)
        while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $Destination.Write($buffer, 0, $read)
            [void]$sha.TransformBlock($buffer, 0, $read, $null, 0)
        }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return $sha.Hash
    } finally {
        $sha.Dispose()
        $sourceStream.Dispose()
    }
}

$stub = [System.IO.Path]::GetFullPath($StubPath)
$output = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $stub -PathType Leaf)) {
    throw "Bootstrap stub is missing: $stub"
}
$payload = @($PayloadFiles | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
if ($payload.Count -lt 2) {
    throw "At least an inner EXE and one BIN slice are required."
}
$names = @{}
foreach ($file in $payload) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Embedded payload file is missing: $file"
    }
    $name = [System.IO.Path]::GetFileName($file)
    if ($names.ContainsKey($name.ToLowerInvariant())) {
        throw "Duplicate embedded payload name: $name"
    }
    if ([System.IO.Path]::GetExtension($name).ToLowerInvariant() -notin @(".exe", ".bin")) {
        throw "Unexpected embedded payload type: $name"
    }
    $names[$name.ToLowerInvariant()] = $true
}
if (-not $names.ContainsKey($LaunchFileName.ToLowerInvariant()) -or
    [System.IO.Path]::GetExtension($LaunchFileName) -ne ".exe") {
    throw "Launch file is not the embedded inner EXE: $LaunchFileName"
}

$outputDirectory = Split-Path -Parent $output
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryOutput = Join-Path $outputDirectory ("." + [System.IO.Path]::GetFileName($output) + "." + [guid]::NewGuid().ToString("N") + ".part")
$entries = @()
$stream = [System.IO.File]::Open($temporaryOutput, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    [byte[]]$stubBytes = [System.IO.File]::ReadAllBytes($stub)
    $stream.Write($stubBytes, 0, $stubBytes.Length)
    foreach ($file in $payload) {
        $info = Get-Item -LiteralPath $file
        $offset = $stream.Position
        $hash = Copy-HashedFile $file $stream
        $entries += [pscustomobject]@{
            Name = $info.Name
            Offset = [long]$offset
            Length = [long]$info.Length
            Sha256 = [byte[]]$hash
        }
    }

    $manifestMemory = New-Object System.IO.MemoryStream
    $manifestWriter = New-Object System.IO.BinaryWriter($manifestMemory, $utf8, $true)
    try {
        $manifestWriter.Write([int]1)
        Write-Utf8String $manifestWriter $LaunchFileName
        $manifestWriter.Write([int]$entries.Count)
        foreach ($entry in $entries) {
            Write-Utf8String $manifestWriter $entry.Name
            $manifestWriter.Write([long]$entry.Offset)
            $manifestWriter.Write([long]$entry.Length)
            $manifestWriter.Write([byte[]]$entry.Sha256)
        }
        $manifestWriter.Flush()
        $manifestBytes = $manifestMemory.ToArray()
    } finally {
        $manifestWriter.Dispose()
        $manifestMemory.Dispose()
    }

    $manifestOffset = $stream.Position
    $stream.Write($manifestBytes, 0, $manifestBytes.Length)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $manifestHash = $sha.ComputeHash($manifestBytes) } finally { $sha.Dispose() }
    $footer = New-Object System.IO.BinaryWriter($stream, $utf8, $true)
    try {
        $footer.Write($magic)
        $footer.Write([long]$manifestOffset)
        $footer.Write([int]$manifestBytes.Length)
        $footer.Write([byte[]]$manifestHash)
        $footer.Flush()
        $stream.Flush($true)
    } finally {
        $footer.Dispose()
    }
} catch {
    $stream.Dispose()
    Remove-Item -LiteralPath $temporaryOutput -Force -ErrorAction SilentlyContinue
    throw
} finally {
    $stream.Dispose()
}

Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
[pscustomobject]@{
    OutputPath = $output
    ManifestSha256 = ([System.BitConverter]::ToString($manifestHash).Replace("-", "").ToLowerInvariant())
    EmbeddedPayloadBytes = [long](($entries | Measure-Object Length -Sum).Sum)
    Entries = $entries
}
