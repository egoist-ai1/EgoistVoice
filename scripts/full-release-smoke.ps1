param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Join-Path $projectRoot "Egoist.Voice.csproj"
    $match = [regex]::Match((Get-Content -LiteralPath $csproj -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) { throw "Не удалось прочитать <Version> из $csproj." }
    $Version = $match.Groups[1].Value.Trim()
    Write-Output "версия из проекта: $Version"
}
$localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$programsRoot = [IO.Path]::GetFullPath((Join-Path $localRoot "Programs"))
$dataRoot = [IO.Path]::GetFullPath((Join-Path $localRoot "EgoistVoice"))
$dataBackup = [IO.Path]::GetFullPath((Join-Path $localRoot "EgoistVoice.release-$Version-backup"))
$appRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot "Egoist Voice"))
$appBackup = [IO.Path]::GetFullPath((Join-Path $programsRoot "Egoist Voice.release-$Version-backup"))
$testRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot "EgoistVoiceInstallerSmoke"))
$legacyTestRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\qa\installer-smoke-install"))
$misparsedTestRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot "Egoist"))
$installer = Join-Path $projectRoot "artifacts\release\EgoistVoice-Setup-$Version-win-x64.exe"
$finalLog = Join-Path $projectRoot "artifacts\qa\installer-clean-final-$Version.log"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

function Assert-ChildPath([string]$Path, [string]$Parent, [string]$Label) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe $Label path: $Path"
    }
}

function Stop-AppAt([string]$Directory) {
    $executable = Join-Path $Directory "Egoist.Voice.exe"
    if (Test-Path -LiteralPath $executable) {
        $process = Start-Process -FilePath $executable -ArgumentList "--shutdown" -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin 0, 2) {
            throw "Egoist Voice shutdown failed at $Directory with exit code $($process.ExitCode)"
        }
    }
}

Assert-ChildPath $dataRoot $localRoot "data"
Assert-ChildPath $dataBackup $localRoot "data backup"
Assert-ChildPath $appRoot $programsRoot "application"
Assert-ChildPath $appBackup $programsRoot "application backup"
Assert-ChildPath $testRoot $programsRoot "test install"
Assert-ChildPath $legacyTestRoot $projectRoot "legacy test install"
Assert-ChildPath $misparsedTestRoot $programsRoot "misparsed test install"

if (-not (Test-Path -LiteralPath $installer)) { throw "Installer not found: $installer" }
if (-not (Test-Path -LiteralPath $dataRoot)) { throw "Application data not found: $dataRoot" }
if (-not (Test-Path -LiteralPath $appRoot)) { throw "Installed application not found: $appRoot" }
if ((Test-Path -LiteralPath $dataBackup) -or (Test-Path -LiteralPath $appBackup)) {
    throw "A release-smoke backup already exists; refusing to overwrite it."
}
if (Test-Path -LiteralPath $legacyTestRoot) {
    Remove-Item -LiteralPath $legacyTestRoot -Recurse -Force
}
$misparsedExecutable = Join-Path $misparsedTestRoot "Egoist.Voice.exe"
if (Test-Path -LiteralPath $misparsedExecutable) {
    $misparsedProduct = (Get-Item -LiteralPath $misparsedExecutable).VersionInfo.ProductName
    if ($misparsedProduct -ne "Egoist Voice") {
        throw "Refusing to clean unexpected application at $misparsedTestRoot"
    }
    Stop-AppAt $misparsedTestRoot
    Remove-Item -LiteralPath $misparsedTestRoot -Recurse -Force
}

Stop-AppAt $appRoot

try {
    Move-Item -LiteralPath $dataRoot -Destination $dataBackup
    Move-Item -LiteralPath $appRoot -Destination $appBackup

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "test-installer.ps1") `
        -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "test-installer failed with exit code $LASTEXITCODE" }

    $setup = Start-Process -FilePath $installer -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/LANG=russian",
        "/LOG=$finalLog"
    ) -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Final install failed with exit code $($setup.ExitCode)" }

    if (Test-Path -LiteralPath $dataRoot) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
    Move-Item -LiteralPath $dataBackup -Destination $dataRoot

    $newExecutable = Join-Path $appRoot "Egoist.Voice.exe"
    $fileVersion = (Get-Item -LiteralPath $newExecutable).VersionInfo.FileVersion
    if ($fileVersion -ne "$Version.0") { throw "Unexpected final version: $fileVersion" }

    Remove-Item -LiteralPath $appBackup -Recurse -Force
    Start-Process -FilePath $newExecutable -ArgumentList "--background" -WindowStyle Hidden
    Start-Sleep -Seconds 3
    if (-not (Get-Process -Name "Egoist.Voice" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $newExecutable })) {
        throw "Final background application did not start."
    }

    [pscustomobject]@{
        FullInstallerSmoke = "PASS"
        Version = $fileVersion
        ModelsBytes = (Get-ChildItem (Join-Path $dataRoot "Models\Speech") -Recurse -File |
            Measure-Object Length -Sum).Sum
        Settings = [IO.File]::ReadAllText((Join-Path $dataRoot "settings.json"))
    } | Format-List
}
catch {
    Stop-AppAt $testRoot
    Stop-AppAt $appRoot

    if (Test-Path -LiteralPath $dataBackup) {
        if (Test-Path -LiteralPath $dataRoot) {
            Remove-Item -LiteralPath $dataRoot -Recurse -Force
        }
        Move-Item -LiteralPath $dataBackup -Destination $dataRoot
    }
    if (Test-Path -LiteralPath $appBackup) {
        if (Test-Path -LiteralPath $appRoot) {
            Remove-Item -LiteralPath $appRoot -Recurse -Force
        }
        Move-Item -LiteralPath $appBackup -Destination $appRoot
    }

    $restoredExecutable = Join-Path $appRoot "Egoist.Voice.exe"
    if (Test-Path -LiteralPath $restoredExecutable) {
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $runKey -Name "EgoistVoice" -Value "`"$restoredExecutable`" --background"
        Start-Process -FilePath $restoredExecutable -ArgumentList "--background" -WindowStyle Hidden
    }
    throw
}
