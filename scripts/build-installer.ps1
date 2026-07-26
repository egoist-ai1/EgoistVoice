param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\release"))
$staging = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "installer-staging"))
$modelStaging = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "model-staging"))
$modelSourceRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "EgoistVoice\Models"))
$installer = Join-Path $releaseRoot "EgoistVoice-Setup-$Version-win-x64.exe"
$checksum = "$installer.sha256"
$cudaCache = Join-Path $env:TEMP "egoist-voice-cuda13"
$cudaBin = Join-Path $cudaCache "nvidia\cu13\bin\x86_64"
$solution = Join-Path $projectRoot "Egoist.Voice.sln"
$project = Join-Path $projectRoot "Egoist.Voice.csproj"
$installerScriptSource = Join-Path $projectRoot "installer\EgoistVoice.iss"
$installerScript = Join-Path $projectRoot "installer\EgoistVoice.generated.iss"

if (-not $staging.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $staging"
}
if (-not $modelStaging.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe model staging path: $modelStaging"
}

New-Item -ItemType Directory -Force $releaseRoot | Out-Null
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
if (Test-Path -LiteralPath $modelStaging) {
    Remove-Item -LiteralPath $modelStaging -Recurse -Force
}

dotnet test $solution -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# The target is Windows x64 only. Removing foreign native runtimes and unused
# satellite resources reduces install size and surface area without changing
# the CUDA -> Vulkan -> CPU fallback chain.
$unusedPaths = @(
    "runtimes\browser",
    "runtimes\cuda\linux-x64",
    "runtimes\vulkan\linux-x64",
    "runtimes\win-arm64",
    "runtimes\win-x86",
    "runtimes\linux-arm64",
    "runtimes\linux-musl-x64",
    "runtimes\linux-x64",
    "runtimes\osx-arm64",
    "runtimes\osx-x64",
    "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "tr", "zh-Hans", "zh-Hant"
)
foreach ($relativePath in $unusedPaths) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $staging $relativePath))
    if (-not $target.StartsWith($staging + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe cleanup path: $target"
    }
    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath (Join-Path $staging "Egoist.Voice.pdb") -Force -ErrorAction SilentlyContinue

$requiredCudaDlls = @("cublas64_13.dll", "cublasLt64_13.dll", "cudart64_13.dll")
$cudaCacheComplete = $true
foreach ($dll in $requiredCudaDlls) {
    if (-not (Test-Path -LiteralPath (Join-Path $cudaBin $dll))) { $cudaCacheComplete = $false }
}

if (-not $cudaCacheComplete) {
    # --upgrade is required, not optional. Without it pip sees the target directory from a previous
    # run, prints "Target directory already exists" and skips extraction entirely — leaving a cache
    # that looks present but has no DLLs in it, and a build that fails on the copy below. This is
    # exactly what a second release build used to hit.
    python -m pip install `
        --disable-pip-version-check `
        --no-warn-script-location `
        --upgrade `
        --target $cudaCache `
        "nvidia-cublas==13.1.0.3" `
        "nvidia-cuda-runtime==13.1.80"
    if ($LASTEXITCODE -ne 0) { throw "CUDA runtime download failed with exit code $LASTEXITCODE" }

    foreach ($dll in $requiredCudaDlls) {
        $path = Join-Path $cudaBin $dll
        if (-not (Test-Path -LiteralPath $path)) {
            throw "CUDA runtime is incomplete after download: $path is missing. Удалите $cudaCache и повторите."
        }
    }
}

Copy-Item -LiteralPath `
    (Join-Path $cudaBin "cublas64_13.dll"), `
    (Join-Path $cudaBin "cublasLt64_13.dll"), `
    (Join-Path $cudaBin "cudart64_13.dll") `
    -Destination $staging `
    -Force

# The distributable is intentionally offline-capable. Validate every speech
# model against the application catalog before copying it into the installer;
# a stale or corrupt local cache must never be shipped to another machine.
$bundledModels = @(
    @{ Id = "gigaam-v3-e2e-rnnt-int8-v1"; File = "gigaam_v3_e2e_rnnt_encoder_int8.onnx"; Size = 318995997L; Sha256 = "2cac62d0c270bd128f898f2be1a2d34780d524a6e9483888ebac7b00f97410f1" },
    @{ Id = "gigaam-v3-e2e-rnnt-decoder-v1"; File = "gigaam_v3_e2e_rnnt_decoder.onnx"; Size = 4600058L; Sha256 = "781971998e6a355d6a714f6932a30eab295e7ba0d14fd7e0f78c83b87e811860" },
    @{ Id = "gigaam-v3-e2e-rnnt-joiner-v1"; File = "gigaam_v3_e2e_rnnt_joint.onnx"; Size = 2712896L; Sha256 = "602ff7017a93311aad34df1437c8d7f49911353c13d6eae7a6ee7b041339465c" },
    @{ Id = "gigaam-v3-e2e-rnnt-tokens-v1"; File = "gigaam_v3_e2e_rnnt_tokens.txt"; Size = 13353L; Sha256 = "7ddf22514c42c531358182c81446a8159771e9921019f09ae743ea622d40221d" },
    @{ Id = "whisper-large-v3-turbo-q5_0-v1"; File = "ggml-large-v3-turbo-q5_0.bin"; Size = 574041195L; Sha256 = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2" }
)
foreach ($model in $bundledModels) {
    $sourceDirectory = Join-Path $modelSourceRoot ("Speech\" + $model.Id)
    $source = Join-Path $sourceDirectory $model.File
    $marker = "$source.verified.json"
    if (-not (Test-Path -LiteralPath $source) -or -not (Test-Path -LiteralPath $marker)) {
        throw "Verified bundled model is missing: $source"
    }
    if ((Get-Item -LiteralPath $source).Length -ne $model.Size) {
        throw "Bundled model has the wrong size: $source"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    if ($actualHash -ne $model.Sha256) {
        throw "Bundled model checksum mismatch: $source"
    }
    $markerData = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($markerData.id -ne $model.Id -or $markerData.sizeBytes -ne $model.Size -or $markerData.sha256 -ne $model.Sha256) {
        throw "Bundled model verification marker is invalid: $marker"
    }
    $destinationDirectory = Join-Path $modelStaging ("Speech\" + $model.Id)
    New-Item -ItemType Directory -Force $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $source, $marker -Destination $destinationDirectory -Force
}

dotnet tool restore --tool-manifest (Join-Path $projectRoot ".config\dotnet-tools.json")
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE" }

$env:DOTNET_ROLL_FORWARD = "Major"
Remove-Item -LiteralPath $installer, $checksum -Force -ErrorAction SilentlyContinue
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$utf8WithBom = [System.Text.UTF8Encoding]::new($true)
$installerScriptText = [System.IO.File]::ReadAllText($installerScriptSource, $utf8Strict)
$installerAssets = @(
    "installer-microphone-52.bmp",
    "installer-text-26.bmp",
    "installer-privacy-26.bmp"
)
foreach ($assetName in $installerAssets) {
    $assetPath = Join-Path $projectRoot "assets\$assetName"
    if (-not (Test-Path -LiteralPath $assetPath) -or (Get-Item -LiteralPath $assetPath).Length -lt 100) {
        throw "Branded installer asset is missing or invalid: $assetPath"
    }
}
$requiredInstallerFragments = @(
    '[Languages]',
    'Name: "russian"',
    'ShowLanguageDialog=no',
    'DisableDirPage=yes',
    'DisableReadyPage=yes',
    'procedure CreateBrandShell',
    'function CreateSurfaceLabel',
    'procedure CurInstallProgressChanged',
    'WizardForm.BorderStyle := bsNone',
    'procedure CancelButtonClick',
    'SetPrimaryButton(',
    'installer-microphone-52.bmp',
    'installer-text-26.bmp',
    'installer-privacy-26.bmp'
)
foreach ($requiredFragment in $requiredInstallerFragments) {
    if (-not $installerScriptText.Contains($requiredFragment)) {
        throw "Inno Setup source is missing required fragment: $requiredFragment"
    }
}
$styleLabelMatch = [regex]::Match(
    $installerScriptText,
    'procedure\s+StyleLabel\b.*?begin(?<body>.*?)end;',
    [System.Text.RegularExpressions.RegexOptions]::Singleline
)
if (-not $styleLabelMatch.Success) {
    throw "Inno Setup source is missing StyleLabel"
}
$styleLabelBody = $styleLabelMatch.Groups['body'].Value
$autoSizeIndex = $styleLabelBody.IndexOf('LabelControl.AutoSize := False;')
$fontIndex = $styleLabelBody.IndexOf('LabelControl.Font.Name')
if ($autoSizeIndex -lt 0 -or $fontIndex -lt 0 -or $autoSizeIndex -gt $fontIndex) {
    throw "StyleLabel must disable AutoSize before changing the font to prevent runtime caption clipping"
}
$surfaceLabelMatch = [regex]::Match(
    $installerScriptText,
    'function\s+CreateSurfaceLabel\b.*?begin(?<body>.*?)end;',
    [System.Text.RegularExpressions.RegexOptions]::Singleline
)
if (-not $surfaceLabelMatch.Success -or
    -not $surfaceLabelMatch.Groups['body'].Value.Contains('Result.AutoSize := False;')) {
    throw "CreateSurfaceLabel must disable AutoSize before captions are assigned"
}
if ([regex]::Matches($installerScriptText, ':=\s*CreateSurfaceLabel;').Count -lt 10) {
    throw "All fixed-width installer surface labels must be created through CreateSurfaceLabel"
}
$forbiddenInstallerFragments = @(
    '[Tasks]',
    'ShowLanguageDialog=yes',
    'DisableDirPage=no',
    'DisableReadyPage=no'
)
foreach ($forbiddenFragment in $forbiddenInstallerFragments) {
    if ($installerScriptText.Contains($forbiddenFragment)) {
        throw "Inno Setup source contains a forbidden interactive fragment: $forbiddenFragment"
    }
}
[System.IO.File]::WriteAllText($installerScript, $installerScriptText, $utf8WithBom)
$scriptBytes = [System.IO.File]::ReadAllBytes($installerScript)
if ($scriptBytes.Length -lt 3 -or $scriptBytes[0] -ne 0xEF -or $scriptBytes[1] -ne 0xBB -or $scriptBytes[2] -ne 0xBF) {
    throw "Generated Inno Setup script is missing the UTF-8 BOM"
}
Push-Location $projectRoot
try {
    dotnet tool run iscc -- `
        "/Qp" `
        "/DSourceDir=$staging" `
        "/DModelSourceDir=$modelStaging" `
        "/DOutputDir=$releaseRoot" `
        "/DMyAppVersion=$Version" `
        $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $installerScript -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not created: $installer"
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value "$hash  $(Split-Path -Leaf $installer)" -Encoding ascii

Get-ChildItem -LiteralPath $releaseRoot -File |
    Where-Object {
        $_.Name -like "EgoistVoice-Setup-*-win-x64.exe*" -and
        $_.FullName -ne $installer -and
        $_.FullName -ne $checksum
    } |
    Remove-Item -Force

Remove-Item -LiteralPath $staging -Recurse -Force
Remove-Item -LiteralPath $modelStaging -Recurse -Force
Get-Item -LiteralPath $installer, $checksum | Select-Object FullName, Length
