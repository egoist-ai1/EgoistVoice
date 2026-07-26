param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$releaseRoot = Join-Path $projectRoot "artifacts\release"
$staging = Join-Path $releaseRoot "staging"
$archive = Join-Path $releaseRoot "EgoistVoice-$Version-win-x64.zip"
$checksum = "$archive.sha256"
$cudaCache = Join-Path $env:TEMP "egoist-voice-cuda13"
$cudaBin = Join-Path $cudaCache "nvidia\cu13\bin\x86_64"

if (-not ([System.IO.Path]::GetFullPath($staging)).StartsWith($releaseRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $staging"
}

New-Item -ItemType Directory -Force $releaseRoot | Out-Null
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

dotnet test (Join-Path $projectRoot "Egoist.Voice.sln") -c Release
dotnet publish (Join-Path $projectRoot "Egoist.Voice.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $staging

if (-not (Test-Path -LiteralPath (Join-Path $cudaBin "cublasLt64_13.dll"))) {
    python -m pip install `
        --disable-pip-version-check `
        --no-warn-script-location `
        --target $cudaCache `
        "nvidia-cublas==13.1.0.3" `
        "nvidia-cuda-runtime==13.1.80"
}

Copy-Item -LiteralPath `
    (Join-Path $cudaBin "cublas64_13.dll"), `
    (Join-Path $cudaBin "cublasLt64_13.dll"), `
    (Join-Path $cudaBin "cudart64_13.dll") `
    -Destination $staging `
    -Force

Remove-Item -LiteralPath $archive, $checksum -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding ascii

Remove-Item -LiteralPath $staging -Recurse -Force
Get-Item -LiteralPath $archive, $checksum | Select-Object FullName, Length
