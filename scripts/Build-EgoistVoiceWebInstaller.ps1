param(
    [string]$PackagePath = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\release"))
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $releaseRoot "EgoistVoice-Setup-2.2.0-win-x64.exe"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releaseRoot "github-v2.2.0-preview.1"
}
$package = [System.IO.Path]::GetFullPath($PackagePath)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$exporter = Join-Path $PSScriptRoot "Export-EgoistVoicePayload.ps1"
$source = Join-Path $projectRoot "installer\EgoistVoiceWebBootstrap.cs"
$manifestSource = Join-Path $projectRoot "installer\EgoistVoiceWebBootstrap.Manifest.cs"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$icon = Join-Path $projectRoot "assets\EgoistVoice.ico"
$webInstaller = Join-Path $output "EgoistVoice-Web-Setup-2.2.0.exe"
$assemblyInfo = Join-Path $output ".EgoistVoiceWebBootstrap.AssemblyInfo.cs"
$checksums = Join-Path $output "SHA256SUMS-2.2.0-preview.1.txt"
$receipt = Join-Path $output "EgoistVoice-2.2.0-preview.1.release.json"

$sourceRevision = (& git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw "Unable to resolve the release source revision."
}
$sourceTreeDirty = [bool](@(& git -C $projectRoot status --porcelain --untracked-files=all).Count)
$informationalVersion = "2.2.0-preview.1+$($sourceRevision.Substring(0, 12))"

foreach ($required in @($package, $exporter, $source, $manifestSource, $compiler, $icon)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Web installer input is missing: $required"
    }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$payload = & $exporter -PackagePath $package -OutputDirectory $output -PassThru

$assemblyInfoText = @"
using System.Reflection;
[assembly: AssemblyTitle("Egoist Voice Web Installer")]
[assembly: AssemblyDescription("Hash-verified online and offline installer for Egoist Voice")]
[assembly: AssemblyCompany("EGOIST")]
[assembly: AssemblyProduct("Egoist Voice")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]
[assembly: AssemblyInformationalVersion("$informationalVersion")]
"@
[System.IO.File]::WriteAllText($assemblyInfo, $assemblyInfoText, (New-Object System.Text.UTF8Encoding($false)))

try {
    & $compiler `
        /nologo `
        /target:winexe `
        /platform:x64 `
        /optimize+ `
        "/out:$webInstaller" `
        "/win32icon:$icon" `
        /reference:System.dll `
        /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll `
        $source `
        $manifestSource `
        $assemblyInfo
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $webInstaller -PathType Leaf)) {
        throw "Web bootstrap compilation failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $assemblyInfo -Force -ErrorAction SilentlyContinue
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($webInstaller)
if ($versionInfo.FileVersion -ne "2.2.0.0" -or
    $versionInfo.ProductVersion -ne $informationalVersion -or
    $versionInfo.ProductName -ne "Egoist Voice") {
    throw "Web bootstrap PE version identity is inconsistent."
}

$verification = Start-Process `
    -FilePath $webInstaller `
    -ArgumentList @("--verify-only", "--payload-dir", $output) `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($verification.ExitCode -ne 0) {
    throw "Web bootstrap offline verification failed with exit code $($verification.ExitCode)"
}

$deliveryFiles = @((Get-Item -LiteralPath $webInstaller)) + @($payload.Files | ForEach-Object { Get-Item -LiteralPath $_.Path })
$checksumLines = @(
    foreach ($file in $deliveryFiles) {
        "{0}  {1}" -f `
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), `
            $file.Name
    }
)
[System.IO.File]::WriteAllText($checksums, (($checksumLines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$receiptObject = [ordered]@{
    schemaVersion = 1
    applicationVersion = "2.2.0"
    releaseTag = "v2.2.0-preview.1"
    repository = "egoist-ai1/EgoistVoice"
    sourceRevision = $sourceRevision
    sourceTreeDirty = $sourceTreeDirty
    deliveryMode = "github-web-bootstrap-with-offline-payload-fallback"
    sourcePackage = [ordered]@{
        name = [System.IO.Path]::GetFileName($payload.SourcePackage)
        bytes = (Get-Item -LiteralPath $payload.SourcePackage).Length
        sha256 = $payload.SourcePackageSha256
        embeddedManifestSha256 = $payload.ManifestSha256
    }
    bootstrap = [ordered]@{
        name = [System.IO.Path]::GetFileName($webInstaller)
        bytes = (Get-Item -LiteralPath $webInstaller).Length
        sha256 = (Get-FileHash -LiteralPath $webInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
        fileVersion = [string]$versionInfo.FileVersion
        productVersion = [string]$versionInfo.ProductVersion
    }
    launchFile = $payload.LaunchFile
    payloadFiles = @(
        foreach ($file in @($payload.Files)) {
            [ordered]@{
                name = $file.Name
                bytes = $file.Length
                sha256 = $file.Sha256
            }
        }
    )
    behaviors = [ordered]@{
        localPayloadPreferred = $true
        networkSourcePinnedToReleaseTag = $true
        resumableDownloads = $true
        sha256BeforeLaunch = $true
        failedDownloadNeverLaunchesInstaller = $true
        successfulOnlineCacheCleanup = $true
    }
    signed = $false
    installerExecutedOnDevelopmentHost = $false
}
[System.IO.File]::WriteAllText(
    $receipt,
    (($receiptObject | ConvertTo-Json -Depth 6) + "`n"),
    (New-Object System.Text.UTF8Encoding($false)))

Get-Item -LiteralPath @($webInstaller, $checksums, $receipt) | Select-Object FullName, Length
$payload.Files | Select-Object Path, Length, Sha256
