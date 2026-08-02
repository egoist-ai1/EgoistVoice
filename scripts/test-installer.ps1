param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Join-Path $projectRoot "Egoist.Voice.csproj"
    $match = [regex]::Match((Get-Content -LiteralPath $csproj -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) { throw "Не удалось прочитать <Version> из $csproj." }
    $Version = $match.Groups[1].Value.Trim()
    Write-Output "версия из проекта: $Version"
}
$qaRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\qa"))
$programsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $programsRoot "EgoistVoiceInstallerSmoke"))
$installer = Join-Path $projectRoot "artifacts\release\EgoistVoice-Setup-$Version-win-x64.exe"
$app = Join-Path $installRoot "Egoist.Voice.exe"
$uninstaller = Join-Path $installRoot "unins000.exe"
$preview = Join-Path $qaRoot "capsule-installed.png"
$backgroundPreview = Join-Path $qaRoot "capsule-background-installed.png"
$trayPreview = Join-Path $qaRoot "tray-custom-installed.png"
$shortcutPreview = Join-Path $qaRoot "custom-shortcut-installed.png"
$benchmark = Join-Path $qaRoot "benchmark-installed.txt"
$stressBenchmark = Join-Path $qaRoot "stress-installed.txt"
$microphone = Join-Path $qaRoot "microphone-installed.txt"
$modelSources = Join-Path $qaRoot "model-sources-installed.txt"
$audio = Join-Path $qaRoot "gigaam-example.wav"
$installLog = Join-Path $qaRoot "installer-smoke.log"
$upgradeLog = Join-Path $qaRoot "installer-upgrade-smoke.log"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "EgoistVoice"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Egoist Voice\Egoist Voice.lnk"
$liveApp = Join-Path $env:LOCALAPPDATA "Programs\Egoist Voice\Egoist.Voice.exe"
$dataRoot = Join-Path $env:LOCALAPPDATA "EgoistVoice"
$bundledModelsRoot = Join-Path $dataRoot "Models\Speech"
$speechSentinel = Join-Path $dataRoot "Models\Speech\gigaam-v3-e2e-rnnt-int8-v1\installer-speech-sentinel.bin"
$legacyModelSentinel = Join-Path $dataRoot "Models\Language\qwen3-8b-q4_k_m-v1\Qwen3-8B-Q4_K_M.gguf"
$legacyHistorySentinel = Join-Path $dataRoot "Data\history.bin"
$legacyLogSentinel = Join-Path $dataRoot "Logs\legacy-1.2.log"
$legacyTempSentinel = Join-Path $dataRoot "Temp\legacy-1.2.tmp"
$settingsSentinel = Join-Path $dataRoot "settings.json"
$activationSentinel = Join-Path $dataRoot "activation.json"
$legacyInstallSentinel = Join-Path $installRoot "LLamaSharp.dll"
$legacyRegistryKey = "HKCU:\Software\EgoistVoice"

if (-not $installRoot.StartsWith($programsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe test install path: $installRoot"
}
if (Test-Path -LiteralPath $liveApp) {
    throw "Refusing isolated installer smoke test while a live Egoist Voice install exists. It uses the same AppId; run this test on a clean Windows user or VM."
}
if (-not (Test-Path -LiteralPath $installer)) { throw "Installer not found: $installer" }
if (-not (Test-Path -LiteralPath $audio)) { throw "Benchmark fixture not found: $audio" }

$oldRunValue = $null
$hadOldRunValue = $false
try {
    $oldRunValue = Get-ItemPropertyValue -LiteralPath $runKey -Name $runValueName -ErrorAction Stop
    $hadOldRunValue = $true
}
catch [System.Management.Automation.ItemNotFoundException] { }
catch [System.Management.Automation.PSArgumentException] { }

function Invoke-Installer([string]$logPath) {
    $arguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/LANG=russian",
        "/DIR=`"$installRoot`"",
        "/LOG=$logPath"
    )
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode)" }
}

function Get-TestAppProcess {
    Get-Process -Name "Egoist.Voice" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -eq $app) }
}

try {
    if (Test-Path -LiteralPath $installRoot) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
    Remove-Item -LiteralPath $preview, $backgroundPreview, $trayPreview, $shortcutPreview, $benchmark, $stressBenchmark, $microphone, $modelSources, $installLog, $upgradeLog -Force -ErrorAction SilentlyContinue

    Invoke-Installer $installLog
    if (-not (Test-Path -LiteralPath $app)) { throw "Application executable was not installed" }
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw "Uninstaller was not installed" }
    if (-not (Test-Path -LiteralPath $startMenuShortcut)) { throw "Start menu shortcut was not created" }
    $bundledModelBytes = (Get-ChildItem -LiteralPath $bundledModelsRoot -Recurse -File | Measure-Object Length -Sum).Sum
    if ($bundledModelBytes -ne 900364167) { throw "Offline model payload is incomplete: $bundledModelBytes bytes" }

    $fileVersion = (Get-Item -LiteralPath $app).VersionInfo.FileVersion
    if ($fileVersion -ne "$Version.0") { throw "Unexpected installed file version: $fileVersion" }
    $runValue = Get-ItemPropertyValue -LiteralPath $runKey -Name $runValueName
    $expectedRunValue = '"' + $app + '" --background'
    if ($runValue -ne $expectedRunValue) { throw "Unexpected autostart value: $runValue" }

    $render = Start-Process -FilePath $app -ArgumentList @("--render-preview", ('"' + $preview + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($render.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preview)) { throw "Installed UI render smoke test failed" }

    $backgroundRender = Start-Process -FilePath $app -ArgumentList @("--background-render-preview", ('"' + $backgroundPreview + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($backgroundRender.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $backgroundPreview)) { throw "Installed background capsule smoke test failed" }

    $trayRender = Start-Process -FilePath $app -ArgumentList @("--render-tray-preview", ('"' + $trayPreview + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($trayRender.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $trayPreview)) { throw "Installed tray render smoke test failed" }

    $shortcutRender = Start-Process -FilePath $app -ArgumentList @("--render-shortcut-preview", ('"' + $shortcutPreview + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($shortcutRender.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $shortcutPreview)) { throw "Installed custom shortcut UI smoke test failed" }

    $sourceCheck = Start-Process -FilePath $app -ArgumentList @("--model-source-smoke", ('"' + $modelSources + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($sourceCheck.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $modelSources)) { throw "Installed model source smoke test failed" }
    if (-not (Get-Content -LiteralPath $modelSources -Raw).StartsWith("PASS")) { throw "Installed model sources are unavailable or inconsistent" }

    $bench = Start-Process -FilePath $app -ArgumentList @("--benchmark", ('"' + $audio + '"'), ('"' + $benchmark + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($bench.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $benchmark)) { throw "Installed ASR benchmark failed" }
    if ((Get-Content -LiteralPath $benchmark -Raw).StartsWith("ERROR:")) { throw "Installed ASR benchmark reported an error" }

    $stress = Start-Process -FilePath $app -ArgumentList @("--stress-benchmark", ('"' + $audio + '"'), ('"' + $stressBenchmark + '"'), "20") -WindowStyle Hidden -Wait -PassThru
    if ($stress.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $stressBenchmark)) { throw "Installed ASR stress benchmark failed" }
    if (-not (Get-Content -LiteralPath $stressBenchmark -Raw).StartsWith("PASS")) { throw "Installed ASR stress benchmark reported an error" }

    $mic = Start-Process -FilePath $app -ArgumentList @("--microphone-smoke", ('"' + $microphone + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($mic.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $microphone)) { throw "Installed microphone smoke test failed" }
    if ((Get-Content -LiteralPath $microphone -Raw).StartsWith("ERROR:")) { throw "Installed microphone smoke test reported an error" }

    Start-Process -FilePath $app -ArgumentList "--background" -WindowStyle Hidden
    Start-Sleep -Seconds 4
    if (-not (Get-TestAppProcess)) { throw "Background application did not stay running" }

    $shutdown = Start-Process -FilePath $app -ArgumentList "--shutdown" -WindowStyle Hidden -Wait -PassThru
    if ($shutdown.ExitCode -notin 0, 2) { throw "Custom hotkey smoke shutdown failed" }
    Set-Content -LiteralPath $activationSentinel -Value '{"Binding":4,"CustomShortcut":{"Modifiers":6,"VirtualKey":135}}' -Encoding utf8
    $logPath = Join-Path $dataRoot "Logs\app.log"
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $app -ArgumentList "--background" -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if (-not (Get-TestAppProcess)) { throw "Application did not start with a custom hotkey" }
    if (-not (Select-String -LiteralPath $logPath -SimpleMatch "Registering hotkey Ctrl + Shift + F24" -Quiet)) {
        throw "Installed custom hotkey was not registered"
    }

    New-Item -ItemType Directory -Force `
        (Split-Path $speechSentinel), `
        (Split-Path $legacyModelSentinel), `
        (Split-Path $legacyHistorySentinel), `
        (Split-Path $legacyLogSentinel), `
        (Split-Path $legacyTempSentinel) | Out-Null
    Set-Content -LiteralPath $speechSentinel -Value "preserve-whisper-on-upgrade" -Encoding ascii
    Set-Content -LiteralPath $legacyModelSentinel -Value "remove-qwen-on-upgrade" -Encoding ascii
    Set-Content -LiteralPath $legacyHistorySentinel -Value "remove-history-on-upgrade" -Encoding ascii
    Set-Content -LiteralPath $legacyLogSentinel -Value "remove-log-on-upgrade" -Encoding ascii
    Set-Content -LiteralPath $legacyTempSentinel -Value "remove-temp-on-upgrade" -Encoding ascii
    Set-Content -LiteralPath $settingsSentinel -Value '{"left":120,"top":80}' -Encoding utf8
    Set-Content -LiteralPath $activationSentinel -Value '{"binding":1}' -Encoding utf8
    Set-Content -LiteralPath $legacyInstallSentinel -Value "remove-old-binary-on-upgrade" -Encoding ascii
    New-Item -Path $legacyRegistryKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $legacyRegistryKey -Name "QwenModel" -Value $legacyModelSentinel

    Invoke-Installer $upgradeLog
    Start-Sleep -Milliseconds 500
    if (Get-TestAppProcess) { throw "Upgrade did not stop the running application" }
    if (-not (Test-Path -LiteralPath $speechSentinel)) { throw "Upgrade removed the active GigaAM model" }
    if (-not (Test-Path -LiteralPath $settingsSentinel)) { throw "Upgrade removed user settings" }
    if (-not (Test-Path -LiteralPath $activationSentinel)) { throw "Upgrade removed activation settings" }
    if (Test-Path -LiteralPath $legacyModelSentinel) { throw "Upgrade left the Qwen model behind" }
    if (Test-Path -LiteralPath $legacyHistorySentinel) { throw "Upgrade left transcript history behind" }
    if (Test-Path -LiteralPath $legacyLogSentinel) { throw "Upgrade left legacy logs behind" }
    if (Test-Path -LiteralPath $legacyTempSentinel) { throw "Upgrade left legacy temporary files behind" }
    if (Test-Path -LiteralPath $legacyInstallSentinel) { throw "Upgrade left an old application binary behind" }
    if (Test-Path -LiteralPath $legacyRegistryKey) { throw "Upgrade left the legacy registry key behind" }

    Start-Process -FilePath $app -ArgumentList "--background" -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if (-not (Get-TestAppProcess)) { throw "Application did not start after upgrade" }

    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -WindowStyle Hidden -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) { throw "Uninstaller failed with exit code $($uninstall.ExitCode)" }
    Start-Sleep -Seconds 1
    if (Get-TestAppProcess) { throw "Uninstall did not stop the background application" }
    if (Test-Path -LiteralPath $installRoot) { throw "Uninstall left the installation directory behind" }
    if (Test-Path -LiteralPath $startMenuShortcut) { throw "Uninstall left the Start menu shortcut behind" }
    if (Get-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue) { throw "Uninstall left the autostart registry value behind" }
    if (Test-Path -LiteralPath $speechSentinel) { throw "Uninstall left models behind" }
    if (Test-Path -LiteralPath $settingsSentinel) { throw "Uninstall left settings behind" }
    if (Test-Path -LiteralPath $activationSentinel) { throw "Uninstall left activation settings behind" }

    [pscustomobject]@{
        Installer = $installer
        Version = $fileVersion
        Install = "PASS"
        Preview = "PASS"
        BackgroundPreview = "PASS"
        TrayPreview = "PASS"
        CustomShortcut = "PASS"
        ModelSources = "PASS"
        OfflineModels = "PASS"
        ASR = "PASS"
        StressASR = "PASS"
        Microphone = "PASS"
        Background = "PASS"
        Upgrade = "PASS"
        Autostart = "PASS"
        Uninstall = "PASS"
    } | Format-List
}
finally {
    if (Test-Path -LiteralPath $uninstaller) {
        Start-Process -FilePath $uninstaller -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -WindowStyle Hidden -Wait
    }
    if ($hadOldRunValue) {
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $runKey -Name $runValueName -Value $oldRunValue
    }
    else {
        Remove-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }
}
