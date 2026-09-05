[CmdletBinding()]
param([switch]$PlanOnly, [switch]$VerifyRollback, [string]$DeliveryDirectory = 'EV-2223')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($DeliveryDirectory -notmatch '^EV-\d{4}$') { throw 'Expected a project ticket delivery directory.' }
$receiptRoot = Join-Path $projectRoot ('artifacts\' + $DeliveryDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $receiptRoot 'delivery-manifest.json') -Raw | ConvertFrom-Json
$payloads = @(
    @{Relative='Egoist.Voice.dll';Hash=$manifest.applicationSha256},
    @{Relative='Egoist.Voice.exe';Hash=$manifest.executableSha256},
    @{Relative='assets\EgoistVoice.ico';Hash=$manifest.iconSha256}
)
foreach ($payload in $payloads) {
    $payload.Source = Join-Path $receiptRoot ('full-candidate\' + $payload.Relative)
    if ((Get-FileHash -LiteralPath $payload.Source).Hash -ne $payload.Hash) { throw 'Reviewed application payload hash mismatch.' }
}
$sourceModel = Join-Path $receiptRoot 'qwen-download\Qwen3-4B-Q4_K_M.gguf'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Egoist Voice'
$installedExe = Join-Path $installRoot 'Egoist.Voice.exe'
$installedDll = Join-Path $installRoot 'Egoist.Voice.dll'
$dataRoot = Join-Path $env:LOCALAPPDATA 'EgoistVoice'
$modelRoot = Join-Path $dataRoot 'TextModels'
$installedModel = Join-Path $modelRoot 'Qwen3-4B-Q4_K_M.gguf'
# A failed attempt may already have installed the authorized model. Reuse only verified bytes.
if (!(Test-Path -LiteralPath $sourceModel) -and (Test-Path -LiteralPath $installedModel)) { $sourceModel = $installedModel }
$settingsPath = Join-Path $dataRoot 'dictation.json'
$runtime = Join-Path $env:LOCALAPPDATA 'Egoist\TranslationEngine\v1\runtime\llama-b10219-vulkan-win-x64-vc143\llama-server.exe'
if (!(Test-Path -LiteralPath $installedExe) -or !(Test-Path -LiteralPath $runtime)) { throw 'The expected existing installation/runtime is missing.' }
foreach ($entry in @(
    @{Path=$sourceModel;Hash='7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5'},
    @{Path=$PSCommandPath;Hash=$manifest.deliveryScriptSha256}
)) {
    if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Hash) { throw 'Reviewed payload hash mismatch.' }
}
if ((Get-Item -LiteralPath $sourceModel).Length -ne 2497280256) { throw 'Model length mismatch.' }
if ((Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash -ne $manifest.runtimeSha256) { throw 'Existing runtime changed since verification.' }
if ((Test-Path -LiteralPath $installedModel) -and (Get-FileHash -LiteralPath $installedModel -Algorithm SHA256).Hash -ne $manifest.modelSha256) { throw 'An unrecognized model already exists; preserving it.' }
foreach ($directory in @($installRoot,$dataRoot,$modelRoot,(Split-Path -Parent $sourceModel))) {
    if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse install/data path refused.' }
}
if ($PlanOnly) {
    [pscustomobject]@{passed=$true;action='Update reviewed DLL, UTF-8 apphost and icon; reuse pinned Qwen and enable local formatting';payloads=@($payloads.Relative);modelBytes=2497280256;installerExecuted=$false;autostartRegistryChanged=$false} | ConvertTo-Json
    exit 0
}

if ($VerifyRollback) {
    # Everything below the transaction boundary uses isolated task-created fixture paths.
    # No user process is stopped or started. Failure is injected after real DLL/settings writes.
    $fixtureRoot = Join-Path $receiptRoot ('rollback-probe-' + [Guid]::NewGuid().ToString('N'))
    $installRoot = Join-Path $fixtureRoot 'Programs'
    $dataRoot = Join-Path $fixtureRoot 'Data'
    $modelRoot = Join-Path $dataRoot 'TextModels'
    $installedDll = Join-Path $installRoot 'Egoist.Voice.dll'
    $installedExe = Join-Path $installRoot 'Egoist.Voice.exe'
    $installedModel = Join-Path $modelRoot 'Qwen3-4B-Q4_K_M.gguf'
    $settingsPath = Join-Path $dataRoot 'dictation.json'
    $sourceModel = Join-Path $fixtureRoot 'fixture-model.bin'
    New-Item -ItemType Directory -Path $installRoot,$dataRoot | Out-Null
    foreach ($payload in $payloads) {
        $fixtureFile = Join-Path $installRoot $payload.Relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $fixtureFile) -Force | Out-Null
        [IO.File]::WriteAllText($fixtureFile, ('Previous fixture bytes: ' + $payload.Relative))
    }
    [IO.File]::WriteAllText($settingsPath, '{"theme":"Dark","formatWithQwen":false,"fixtureSetting":"preserve"}')
    [IO.File]::WriteAllText($sourceModel, 'Synthetic model payload for transaction-only fault injection')
    $fixtureModelHash = (Get-FileHash -LiteralPath $sourceModel).Hash
}
$backupRoot = Join-Path $dataRoot ('Backups\' + $DeliveryDirectory + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
New-Item -ItemType Directory -Path $backupRoot | Out-Null
foreach ($payload in $payloads) {
    $payload.Target = Join-Path $installRoot $payload.Relative
    $payload.Backup = Join-Path $backupRoot $payload.Relative
    $payload.OldHash = (Get-FileHash -LiteralPath $payload.Target).Hash
    New-Item -ItemType Directory -Path (Split-Path -Parent $payload.Backup) -Force | Out-Null
    Copy-Item -LiteralPath $payload.Target -Destination $payload.Backup
}
$hadSettings = Test-Path -LiteralPath $settingsPath
if ($hadSettings) { Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $backupRoot 'dictation.json') }
$oldSettingsHash = if ($hadSettings) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
$oldHash = (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash
$previousProcesses = @(if (!$VerifyRollback) { Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe } })
$wasRunning = $previousProcesses.Count -gt 0
if ($wasRunning) {
    # A production take must finish before an in-place managed DLL update.
    $logPath = Join-Path $dataRoot 'Logs\app.log'
    $idleDeadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $lastMarker = if (Test-Path -LiteralPath $logPath) {
            Get-Content -LiteralPath $logPath -Tail 200 | Where-Object { $_ -match 'Audio capture started|Dictation timing:|StopAndTranscribe failed|No speech detected|Recording operation cancelled|Dictation cancelled' } | Select-Object -Last 1
        } else { $null }
        $busy = $lastMarker -match 'Audio capture started'
        if ($busy) { Start-Sleep -Milliseconds 250 }
    } while ($busy -and [DateTime]::UtcNow -lt $idleDeadline)
    if ($busy) { throw 'A dictation is active; no app/model/settings changes applied.' }
    $stop = Start-Process -FilePath $installedExe -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    $stopHandle = $stop.Handle
    if (!$stop.WaitForExit(30000) -or $stop.ExitCode -ne 0) { throw 'Normal application shutdown did not complete.' }
    # Releasing the singleton mutex precedes native teardown. Wait for the actual processes
    # before copying DLLs; otherwise normal shutdown can briefly leave the payload locked.
    foreach ($previous in $previousProcesses) {
        if (!$previous.WaitForExit(30000)) { throw 'The previous application still holds its files; update not applied.' }
    }
}
$newProcess = $null
try {
    # The source is task-created staging. Verify both resolved endpoints before its one-way install move.
    $resolvedSource = [IO.Path]::GetFullPath($sourceModel)
    $resolvedTarget = [IO.Path]::GetFullPath($installedModel)
    if (!(Test-Path -LiteralPath $installedModel) -and
        (!$resolvedSource.StartsWith($receiptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        !$resolvedTarget.StartsWith($modelRoot + '\', [StringComparison]::OrdinalIgnoreCase))) { throw 'Unexpected model install boundary.' }
    New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
    if (!(Test-Path -LiteralPath $installedModel)) { Move-Item -LiteralPath $resolvedSource -Destination $resolvedTarget }
    foreach ($payload in $payloads) {
        Copy-Item -LiteralPath $payload.Source -Destination $payload.Target
        if ((Get-FileHash -LiteralPath $payload.Target).Hash -ne $payload.Hash) { throw 'Installed application payload hash mismatch.' }
    }
    $settings = if ($hadSettings) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    foreach ($setting in @{startLocalQwen=$true;formatWithQwen=$true;textModelEndpoint='http://127.0.0.1:47823/v1';textModelId='egoist-qwen3-4b';formatBudgetSeconds=2}.GetEnumerator()) {
        $settings | Add-Member -MemberType NoteProperty -Name $setting.Key -Value $setting.Value -Force
    }
    $temporary = Join-Path $dataRoot ('dictation-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($temporary, ($settings | ConvertTo-Json -Depth 20))
    if ($hadSettings) { [IO.File]::Replace($temporary, $settingsPath, (Join-Path $backupRoot 'dictation.replaced.json')) }
    else { [IO.File]::Move($temporary, $settingsPath) }
    if ($VerifyRollback) { throw 'Injected failure after application and settings replacement.' }
    $newProcess = Start-Process -FilePath $installedExe -ArgumentList '--background' -WindowStyle Hidden -PassThru
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(35)
    $ready = $false
    do {
        if ($newProcess.HasExited) { throw 'Updated application exited during startup.' }
        $logPath = Join-Path $dataRoot 'Logs\app.log'
        if (Test-Path -LiteralPath $logPath) {
            $ready = [bool](Get-Content -LiteralPath $logPath -Tail 100 | Where-Object { $_ -match ('\[' + $newProcess.Id + '\] Local Qwen ready;') })
        }
        if (!$ready) { Start-Sleep -Milliseconds 250 }
    } while (!$ready -and [DateTime]::UtcNow -lt $readyDeadline)
    if (!$ready) { throw 'Qwen did not become ready within the startup budget.' }
    [pscustomobject]@{passed=$true;appPid=$newProcess.Id;applicationSha256=$manifest.applicationSha256;previousApplicationSha256=$oldHash;payloads=@($payloads | ForEach-Object { @{path=$_.Relative;sha256=$_.Hash;previousSha256=$_.OldHash} });modelSha256=$manifest.modelSha256;backupRoot=$backupRoot;localQwenReady=$ready;installerExecuted=$false} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $receiptRoot 'workstation-upgrade.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $receiptRoot 'workstation-upgrade.json') -Raw
} catch {
    $failure = $_
    if ($newProcess -and !$newProcess.HasExited) { $newProcess.Kill(); $newProcess.WaitForExit() }
    foreach ($payload in $payloads) {
        Copy-Item -LiteralPath $payload.Backup -Destination $payload.Target
        if ((Get-FileHash -LiteralPath $payload.Target).Hash -ne $payload.OldHash) { throw 'Rollback application payload hash mismatch.' }
    }
    if ($hadSettings) { Copy-Item -LiteralPath (Join-Path $backupRoot 'dictation.json') -Destination $settingsPath }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath }
    if ((Get-FileHash -LiteralPath $installedDll).Hash -ne $oldHash) { throw 'Rollback application hash mismatch.' }
    if ($hadSettings -and (Get-FileHash -LiteralPath $settingsPath).Hash -ne $oldSettingsHash) { throw 'Rollback settings hash mismatch.' }
    if ($wasRunning) {
        $restoredProcess = Start-Process -FilePath $installedExe -ArgumentList '--background' -WindowStyle Hidden -PassThru
        if ($restoredProcess.WaitForExit(2000)) { throw 'Previous application did not remain running after rollback.' }
    }
    if ($VerifyRollback) {
        $modelRetained = (Get-FileHash -LiteralPath $installedModel).Hash -eq $fixtureModelHash
        $sourceMoved = !(Test-Path -LiteralPath $sourceModel)
        $retrySource = if (Test-Path -LiteralPath $sourceModel) { $sourceModel } else { $installedModel }
        $retryModelVerified = (Get-FileHash -LiteralPath $retrySource).Hash -eq $fixtureModelHash
        $expectedFailure = $failure.Exception.Message -eq 'Injected failure after application and settings replacement.'
        $passed = $modelRetained -and $sourceMoved -and $retryModelVerified -and $expectedFailure
        [ordered]@{passed=$passed;mode='Isolated failure after actual DLL, EXE, icon and settings replacement';applicationRestored=$true;payloadsRestored=@($payloads.Relative);settingsRestored=$true;modelRetained=$modelRetained;stagingModelMoved=$sourceMoved;retryModelVerified=$retryModelVerified;userProcessesTouched=$false;fixtureRoot=$fixtureRoot;deliveryScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $receiptRoot 'qa\rollback-check.json') -Encoding utf8
        if (!$passed) { throw ('Rollback fixture failed: ' + $failure.Exception.Message) }
        Get-Content -LiteralPath (Join-Path $receiptRoot 'qa\rollback-check.json') -Raw
        exit 0
    }
    throw $failure
}
