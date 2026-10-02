#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification='The script-level ShouldProcess guards the whole transaction.')]
param(
    [Parameter(Mandatory, ParameterSetName='Upgrade')][string]$StagingDirectory,
    [Parameter(Mandatory, ParameterSetName='Upgrade')][string]$ManifestPath,
    [Parameter(Mandatory, ParameterSetName='Upgrade')][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ManifestSha256,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Egoist Voice Compact'),
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [Parameter(ParameterSetName='Upgrade')][switch]$PlanOnly,
    [Parameter(Mandatory, ParameterSetName='RollbackCheck')][switch]$VerifyRollback,
    [Parameter(ParameterSetName='RollbackCheck')][ValidateSet('2.3.0','2.4.0')][string]$RollbackFixtureVersion = '2.4.0',
    [Parameter(ParameterSetName='RollbackCheck')][ValidateSet('Missing','Disabled','Enabled')][string]$RollbackSpeechPunctuation = 'Missing',
    [Parameter(Mandatory, ParameterSetName='Recover')][string]$RecoverTransaction,
    [ValidateRange(5,120)][int]$ReadyTimeoutSeconds = 45
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = [Text.UTF8Encoding]::new($false)
function Get-FullPath([string]$Path) {
    if (!$Path -or ![IO.Path]::IsPathFullyQualified($Path)) { throw 'An absolute literal path is required.' }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($resolved -eq [IO.Path]::GetPathRoot($resolved).TrimEnd('\', '/')) { throw 'A drive root is not an application/work directory.' }
    return $resolved
}
function Test-Under([string]$Child, [string]$Parent) {
    return $Child.StartsWith($Parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoReparse([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Reparse path refused.'
        }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($parent) { $parent.FullName } else { $null }
    }
}
function Get-PayloadPath([string]$Root, [string]$Relative) {
    if (!$Relative -or $Relative -match '(^|[\\/])(\.\.?|Data)([\\/]|$)|[:;"{}\r\n]' -or [IO.Path]::IsPathRooted($Relative)) {
        throw 'Unsafe/private payload path.'
    }
    $result = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (!(Test-Under $result $Root)) { throw 'Payload escaped its exact root.' }
    Assert-NoReparse $result
    return $result
}
function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8)
}
function Get-ExpectedModelId([string]$Version) {
    $primary = @('gigaam-v3-rnnt-int8-v1','gigaam-v3-rnnt-decoder-v1','gigaam-v3-rnnt-joiner-v1','gigaam-v3-rnnt-tokens-v1')
    if ([version]($Version -split '-', 2)[0] -ge [version]'2.4.0') {
        return $primary + @('gigaam-v3-e2e-rnnt-int8-v1','gigaam-v3-e2e-rnnt-decoder-v1','gigaam-v3-e2e-rnnt-joiner-v1','gigaam-v3-e2e-rnnt-tokens-v1')
    }
    return $primary
}
function Get-ReadinessState([string[]]$Lines, [int]$ProcessId, [DateTime]$StartedUtc, [bool]$RequireFormatter) {
    $asrReady = $false
    $formatterReady = $false
    $processStartedUtc = $StartedUtc.ToUniversalTime()
    foreach ($line in $Lines) {
        $asrMatch = $line -match ('\[' + $ProcessId + '\] Russian ASR ready: engine=GigaAM v3 RNNT$')
        $formatterMatch = $line -match ('\[' + $ProcessId + '\] Russian formatter ready: engine=GigaAM v3 E2E RNNT$')
        if (!$asrMatch -and !$formatterMatch) { continue }
        [DateTimeOffset]$stamp = [DateTimeOffset]::MinValue
        if (![DateTimeOffset]::TryParse(($line -split ' ', 2)[0], [ref]$stamp) -or $stamp.UtcDateTime -lt $processStartedUtc) { continue }
        if ($asrMatch) { $asrReady = $true }
        if ($formatterMatch) { $formatterReady = $true }
    }
    return [pscustomobject]@{AsrReady=$asrReady;FormatterReady=$formatterReady;Ready=($asrReady -and (!$RequireFormatter -or $formatterReady))}
}
function Read-VerifiedPayload([string]$Stage, [string]$Manifest, [string]$ExpectedHash) {
    Assert-NoReparse $Stage
    Assert-NoReparse $Manifest
    if (!(Test-Path -LiteralPath $Stage -PathType Container) -or (Get-Hash $Manifest) -ne $ExpectedHash.ToLowerInvariant()) {
        throw 'Reviewed manifest hash mismatch or staging is missing.'
    }
    $catalog = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or $catalog.version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9.]+)?$' -or
        $catalog.sourceRevision -notmatch '^[0-9a-f]{40}$' -or $catalog.sourceDirty) { throw 'A clean versioned source-bound manifest is required.' }
    $files = @($catalog.files)
    $actual = @(Get-ChildItem -LiteralPath $Stage -Recurse -Force -File)
    foreach ($entry in @(Get-Item -LiteralPath $Stage) + @(Get-ChildItem -LiteralPath $Stage -Recurse -Force)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse staging entry refused.' }
    }
    if ($files.Count -ne $catalog.fileCount -or $files.Count -ne $actual.Count) { throw 'Staging file count differs from the explicit manifest.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $files) {
        $source = Get-PayloadPath $Stage $file.path
        $canonical = [IO.Path]::GetRelativePath($Stage, $source).Replace('\','/')
        if (!$seen.Add($canonical) -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $file.bytes -lt 0) { throw 'Invalid/duplicate manifest entry.' }
        if (!(Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -ne $file.bytes -or (Get-Hash $source) -ne $file.sha256) {
            throw ('Staging payload integrity failure: ' + $file.path)
        }
    }
    foreach ($required in @('Egoist.Voice.exe', 'Egoist.Voice.dll', 'coreclr.dll', 'egoist-voice.portable', 'compact-models.json')) {
        if (!$seen.Contains($required)) { throw 'Required self-contained Compact payload is missing.' }
    }
    $models = @(Get-Content -LiteralPath (Join-Path $Stage 'compact-models.json') -Raw | ConvertFrom-Json)
    $expectedModelIds = @(Get-ExpectedModelId -Version $catalog.version)
    if ($models.Count -ne $expectedModelIds.Count) { throw 'Version requires four primary RNNT assets, and from 2.4 also four formatting RNNT assets.' }
    $modelIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($model in $models) {
        if ($model.Id -notmatch '^gigaam-[a-z0-9-]+$' -or !$modelIds.Add([string]$model.Id) -or $model.FileName -notmatch '^[a-zA-Z0-9_.-]+$' -or
            $model.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $model.SizeBytes -le 0) { throw 'Invalid primary model catalog.' }
    }
    foreach ($expectedId in $expectedModelIds) {
        if (!$modelIds.Contains($expectedId)) {
            if ($expectedId -match '-e2e-') { throw 'Required formatting RNNT model asset is missing.' }
            throw 'Required primary RNNT model asset is missing.'
        }
    }
    foreach ($model in $models) {
        $relative = 'Models/Speech/' + $model.Id + '/' + $model.FileName
        $modelEntries = @($files | Where-Object { $_.path -ieq $relative })
        if ($modelEntries.Count -ne 1 -or $modelEntries[0].bytes -ne $model.SizeBytes -or $modelEntries[0].sha256 -ne $model.Sha256) {
            throw 'Primary model catalog differs from the reviewed payload.'
        }
    }
    $catalog | Add-Member -MemberType NoteProperty -Name modelAssetCount -Value $models.Count -Force
    return $catalog
}
function Assert-CompactInstallation([string]$Root) {
    Assert-NoReparse $Root
    foreach ($relative in @('Egoist.Voice.exe', 'Egoist.Voice.dll', 'egoist-voice.portable')) {
        if (!(Test-Path -LiteralPath (Get-PayloadPath $Root $relative) -PathType Leaf)) { throw 'Expected existing Compact installation is missing.' }
    }
    Assert-NoReparse (Join-Path $Root 'Data\dictation.json')
}
function Get-MatchingProcess([string]$Root) {
    if (!('Egoist.UpgradeProcessIdentity' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace Egoist {
    public static class UpgradeProcessIdentity {
        [DllImport("kernel32.dll", SetLastError=true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", SetLastError=true)]
        private static extern bool CloseHandle(IntPtr handle);
        public static bool TryGetPath(int processId, out string path, out int error) {
            path=null; error=0;
            IntPtr process=OpenProcess(0x1000, false, processId);
            if (process==IntPtr.Zero) { error=Marshal.GetLastWin32Error(); return false; }
            try {
                var buffer=new StringBuilder(32768); int size=buffer.Capacity;
                if (!QueryFullProcessImageName(process, 0, buffer, ref size)) { error=Marshal.GetLastWin32Error(); return false; }
                path=buffer.ToString(); return true;
            } finally { CloseHandle(process); }
        }
    }
}
'@
    }
    $executable = Join-Path $Root 'Egoist.Voice.exe'
    foreach ($candidate in @(Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue)) {
        [string]$imagePath = $null
        [int]$queryError = 0
        if (![Egoist.UpgradeProcessIdentity]::TryGetPath($candidate.Id, [ref]$imagePath, [ref]$queryError)) {
            $candidate.Refresh()
            if ($candidate.HasExited) { continue }
            throw ('Cannot establish exact Voice process identity; Win32=' + $queryError + '. No process is stopped.')
        }
        if ([IO.Path]::GetFullPath($imagePath) -ieq $executable) { $candidate }
    }
}
function Stop-MatchingApplication([string]$Root, [object[]]$Processes) {
    if (!$Processes.Count) { return }
    $log = Join-Path $Root 'Data\Logs\app.log'
    $idleDeadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $busy = $false
        foreach ($existing in $Processes) {
            if ($existing.HasExited) { continue }
            $last = if (Test-Path -LiteralPath $log) {
                Get-Content -LiteralPath $log -Tail 400 | Where-Object {
                    $_ -match ('\[' + $existing.Id + '\]') -and
                    $_ -match 'Audio capture started|StopAndTranscribe requested|Dictation timing:|StartRecording failed|StopAndTranscribe failed|No speech detected|Recording operation cancelled|Dictation cancelled|Transcription complete: characters=0\b|Startup complete|Russian ASR ready:|Russian formatter ready:'
                } | Select-Object -Last 1
            } else { $null }
            # Fail closed when no technical marker can establish idle for this exact process.
            if (!$last -or $last -match 'Audio capture started|StopAndTranscribe requested') { $busy = $true }
        }
        if ($busy) { Start-Sleep -Milliseconds 250 }
    } while ($busy -and [DateTime]::UtcNow -lt $idleDeadline)
    if ($busy) { throw 'The exact Voice process is busy or its idle state is unknown; no payload changes applied.' }
    $stillMatching = @(Get-MatchingProcess $Root)
    if (!$stillMatching.Count) { return }
    if (@($stillMatching | Where-Object { $_.Id -notin @($Processes.Id) }).Count) { throw 'The selected application process changed during its idle check; no shutdown sent.' }
    $shutdown = Start-Process -FilePath (Join-Path $Root 'Egoist.Voice.exe') -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    $null = $shutdown.Handle
    if (!$shutdown.WaitForExit(30000) -or $shutdown.ExitCode -ne 0) { throw 'Graceful Voice shutdown did not complete.' }
    foreach ($existing in $Processes) {
        if (!$existing.WaitForExit(30000)) { throw 'The previous exact Voice process still holds its payload.' }
    }
}
function Restore-Transaction([object]$Journal, [string]$TransactionRoot) {
    Assert-NoReparse $TransactionRoot
    foreach ($file in @($Journal.files)) {
        $target = Get-PayloadPath $Journal.installRoot $file.path
        if ($file.existed) {
            $backup = Get-PayloadPath (Join-Path $TransactionRoot 'previous') $file.path
            if ((Get-Hash $backup) -ne $file.previousSha256) { throw 'Rollback backup integrity failure.' }
        }
    }
    $settingsPath = Join-Path $Journal.installRoot 'Data\dictation.json'
    Assert-NoReparse $settingsPath
    $previousSettings = Join-Path $TransactionRoot 'settings.previous.json'
    if ($Journal.settingsExisted -and (Get-Hash $previousSettings) -ne $Journal.previousSettingsSha256) { throw 'Rollback settings integrity failure.' }
    foreach ($file in @($Journal.files)) {
        $target = Get-PayloadPath $Journal.installRoot $file.path
        if ($file.existed) {
            Copy-Item -LiteralPath (Get-PayloadPath (Join-Path $TransactionRoot 'previous') $file.path) -Destination $target -Force
            if ((Get-Hash $target) -ne $file.previousSha256) { throw 'Rollback application integrity failure.' }
        } elseif (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Force
        }
    }
    if ($Journal.settingsExisted) {
        Copy-Item -LiteralPath $previousSettings -Destination $settingsPath -Force
        if ((Get-Hash $settingsPath) -ne $Journal.previousSettingsSha256) { throw 'Rollback settings verification failed.' }
    } elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
    $Journal.state = 'rolled-back'
    Write-Json (Join-Path $TransactionRoot 'transaction.json') $Journal
}
function Invoke-UpgradeTransaction([string]$Stage, [object]$Catalog, [string]$Root, [string]$Work, [string]$Receipts, [bool]$Fixture = $false, [int]$ReadySeconds = 45) {
    Assert-CompactInstallation $Root
    foreach ($file in @($Catalog.files)) { $null = Get-PayloadPath $Root $file.path }
    $settingsPath = Join-Path $Root 'Data\dictation.json'
    $settingsExisted = Test-Path -LiteralPath $settingsPath
    $settings = if ($settingsExisted) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    if ($settings -isnot [pscustomobject]) { throw 'Existing settings must be a JSON object; preserved without changes.' }
    $speechProperty = $settings.PSObject.Properties['formatSpeechPunctuation']
    if ($speechProperty -and $speechProperty.Value -isnot [bool]) { throw 'Existing formatSpeechPunctuation must be a JSON boolean; preserved without changes.' }
    $speechPunctuationEnabled = if ($speechProperty) { [bool]$speechProperty.Value } else { $true }
    $requireFormatter = $Catalog.modelAssetCount -eq 8 -and $speechPunctuationEnabled

    New-Item -ItemType Directory -Path $Work, $Receipts -Force | Out-Null
    $transaction = Join-Path $Work ('compact-upgrade-' + [Guid]::NewGuid().ToString('N'))
    $previous = Join-Path $transaction 'previous'
    New-Item -ItemType Directory -Path $previous | Out-Null
    $records = @($Catalog.files | ForEach-Object {
        $target = Get-PayloadPath $Root $_.path
        $existed = Test-Path -LiteralPath $target
        $previousHash = if ($existed) { Get-Hash $target } else { $null }
        if ($existed) {
            $backup = Get-PayloadPath $previous $_.path
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $backup
            if ((Get-Hash $backup) -ne $previousHash) { throw 'Transaction snapshot integrity failure.' }
        }
        [pscustomobject]@{path=$_.path;sha256=$_.sha256;existed=$existed;previousSha256=$previousHash}
    })
    if ($settingsExisted) { Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $transaction 'settings.previous.json') }
    [object[]]$processes = @(if (!$Fixture) { Get-MatchingProcess $Root })
    $journal = [pscustomobject][ordered]@{schemaVersion=1;state='prepared';version=$Catalog.version;sourceRevision=$Catalog.sourceRevision;installRoot=$Root;wasRunning=($processes.Count -gt 0);files=$records;settingsExisted=$settingsExisted;previousSettingsSha256=$(if($settingsExisted){Get-Hash $settingsPath}else{$null});generatedAt=[DateTime]::UtcNow.ToString('o')}
    $journalPath = Join-Path $transaction 'transaction.json'
    Write-Json $journalPath $journal
    $newProcess = $null
    $ready = $false
    $asrReady = $false
    $formatterReady = $false
    try {
        if (!$Fixture) { Stop-MatchingApplication $Root $processes }
        $journal.state = 'applying'
        Write-Json $journalPath $journal
        foreach ($file in @($Catalog.files)) {
            $target = Get-PayloadPath $Root $file.path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Get-PayloadPath $Stage $file.path) -Destination $target -Force
            if ((Get-Hash $target) -ne $file.sha256) { throw 'Installed payload verification failed.' }
        }
        foreach ($setting in @{preserveSpokenWords=$true;formatWithQwen=$false;startLocalQwen=$false;mixedLanguageMode=$false;directGigaamFastMode=$true}.GetEnumerator()) {
            $settings | Add-Member -MemberType NoteProperty -Name $setting.Key -Value $setting.Value -Force
        }
        $settings | Add-Member -MemberType NoteProperty -Name formatSpeechPunctuation -Value $speechPunctuationEnabled -Force
        New-Item -ItemType Directory -Path (Split-Path -Parent $settingsPath) -Force | Out-Null
        $newSettings = Join-Path $transaction 'settings.new.json'
        Write-Json $newSettings $settings
        if ($settingsExisted) { [IO.File]::Replace($newSettings, $settingsPath, (Join-Path $transaction 'settings.replaced.json')) }
        else { [IO.File]::Move($newSettings, $settingsPath) }
        if ($Fixture) {
            $observedSettings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            $fixtureProfileApplied = $observedSettings.preserveSpokenWords -and !$observedSettings.formatWithQwen -and
                !$observedSettings.startLocalQwen -and !$observedSettings.mixedLanguageMode -and $observedSettings.directGigaamFastMode -and $observedSettings.formatSpeechPunctuation -eq $speechPunctuationEnabled
            $fixtureUserChoicesPreserved = $observedSettings.theme -eq 'Dark' -and $observedSettings.captureDeviceId -eq 'fixture-device' -and
                !$observedSettings.saveRecentRecordings -and $observedSettings.custom -eq 'preserve'
            $fixtureSpeechPreferencePreserved = $observedSettings.formatSpeechPunctuation -eq $speechPunctuationEnabled
            throw 'Injected failure after all real payload and settings replacements.'
        }
        if ($journal.wasRunning) {
            $newProcess = Start-Process -FilePath (Join-Path $Root 'Egoist.Voice.exe') -ArgumentList '--background' -WindowStyle Hidden -PassThru
            $deadline = [DateTime]::UtcNow.AddSeconds($ReadySeconds)
            $log = Join-Path $Root 'Data\Logs\app.log'
            do {
                if ($newProcess.HasExited) { throw 'Updated Voice exited during startup.' }
                if (Test-Path -LiteralPath $log) {
                    $readiness = Get-ReadinessState -Lines @(Get-Content -LiteralPath $log -Tail 150) -ProcessId $newProcess.Id -StartedUtc $newProcess.StartTime.ToUniversalTime() -RequireFormatter $requireFormatter
                    $asrReady = $readiness.AsrReady
                    $formatterReady = $readiness.FormatterReady
                    $ready = $readiness.Ready
                }
                if (!$ready) { Start-Sleep -Milliseconds 250 }
            } while (!$ready -and [DateTime]::UtcNow -lt $deadline)
            if (!$ready) { throw 'Required Russian ASR and enabled audio formatter readiness were not confirmed within the startup budget.' }
        }
        $journal.state = 'committed'
        Write-Json $journalPath $journal
        $receipt = [ordered]@{passed=$true;version=$Catalog.version;sourceRevision=$Catalog.sourceRevision;manifestSha256=$ManifestSha256.ToLowerInvariant();fileCount=$records.Count;modelAssetCount=$Catalog.modelAssetCount;settingsProfile=$(if($Catalog.modelAssetCount -eq 8){'russian-quality-rnnt'}else{'literal-russian-rnnt'});speechPunctuationEnabled=$speechPunctuationEnabled;formatterRequired=$requireFormatter;formatterReady=$formatterReady;userChoicesPreserved=$true;asrReady=$asrReady;wasRunning=$journal.wasRunning;appPid=$(if($newProcess){$newProcess.Id}else{$null});transactionRoot=$transaction;installerExecuted=$false;generatedAt=[DateTime]::UtcNow.ToString('o')}
        Write-Json (Join-Path $Receipts 'compact-workstation-upgrade.json') $receipt
        return $receipt
    } catch {
        $failure = $_
        if ($newProcess -and !$newProcess.HasExited) { $newProcess.Kill(); $newProcess.WaitForExit(30000) | Out-Null }
        if ($journal.state -eq 'applying') { Restore-Transaction $journal $transaction }
        if (!$Fixture -and $journal.wasRunning -and !(@(Get-MatchingProcess $Root)).Count) {
            $restored = Start-Process -FilePath (Join-Path $Root 'Egoist.Voice.exe') -ArgumentList '--background' -WindowStyle Hidden -PassThru
            if ($restored.WaitForExit(2000)) { throw 'Previous application exited after rollback.' }
        }
        if ($Fixture -and $failure.Exception.Message -eq 'Injected failure after all real payload and settings replacements.' -and $journal.state -eq 'rolled-back') {
            return [ordered]@{passed=$true;rollbackVerified=$true;transactionRoot=$transaction;applicationRestored=$true;settingsRestored=$true;russianProfileApplied=$fixtureProfileApplied;userChoicesPreserved=$fixtureUserChoicesPreserved;modelAssetCount=$Catalog.modelAssetCount;speechPunctuationEnabled=$speechPunctuationEnabled;speechPreferencePreserved=$fixtureSpeechPreferencePreserved;formatterRequired=$requireFormatter;userProcessesTouched=$false;installerExecuted=$false}
        }
        throw $failure
    }
}
$work = Get-FullPath $WorkDirectory
$receipts = Get-FullPath $ReceiptDirectory
foreach ($directory in @($work,$receipts)) { Assert-NoReparse $directory }
if ($VerifyRollback) {
    if (!$PSCmdlet.ShouldProcess($work, 'Verify Compact rollback using isolated synthetic files only')) { return }
    $fixture = Join-Path $work ('rollback-fixture-' + [Guid]::NewGuid().ToString('N'))
    $fixtureStage = Join-Path $fixture 'stage'
    $fixtureInstall = Join-Path $fixture 'installed [Русский]'
    New-Item -ItemType Directory -Path $fixtureStage,$fixtureInstall -Force | Out-Null
    foreach ($name in @('Egoist.Voice.exe','Egoist.Voice.dll','egoist-voice.portable','coreclr.dll')) {
        [IO.File]::WriteAllText((Join-Path $fixtureStage $name), ('new synthetic bytes: ' + $name), $utf8)
        [IO.File]::WriteAllText((Join-Path $fixtureInstall $name), ('old synthetic bytes: ' + $name), $utf8)
    }
    $models = @()
    foreach ($id in @(Get-ExpectedModelId -Version $RollbackFixtureVersion)) {
        $part = $id
        $relative = 'Models/Speech/' + $id + '/fixture.bin'
        $source = Get-PayloadPath $fixtureStage $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $source) -Force | Out-Null
        [IO.File]::WriteAllText($source, ('new synthetic model ' + $part), $utf8)
        $models += [ordered]@{Id=$id;FileName='fixture.bin';SizeBytes=(Get-Item -LiteralPath $source).Length;Sha256=(Get-Hash $source)}
    }
    Write-Json (Join-Path $fixtureStage 'compact-models.json') $models
    $data = Join-Path $fixtureInstall 'Data'
    New-Item -ItemType Directory -Path $data | Out-Null
    $fixtureSettings = [ordered]@{theme='Dark';captureDeviceId='fixture-device';saveRecentRecordings=$false;formatWithQwen=$true;custom='preserve'}
    if ($RollbackSpeechPunctuation -ne 'Missing') { $fixtureSettings.formatSpeechPunctuation = $RollbackSpeechPunctuation -eq 'Enabled' }
    Write-Json (Join-Path $data 'dictation.json') $fixtureSettings
    $sentinel = Join-Path $fixtureInstall 'unknown [keep].txt'
    [IO.File]::WriteAllText($sentinel, 'unrelated file remains', $utf8)
    $sentinelHash = Get-Hash $sentinel
    $files = @(Get-ChildItem -LiteralPath $fixtureStage -File -Recurse | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($fixtureStage,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-Hash $_.FullName)}
    })
    $fixtureCatalog = [pscustomobject]@{schemaVersion=1;version=$RollbackFixtureVersion;sourceRevision=('0' * 40);sourceDirty=$false;fileCount=$files.Count;files=$files}
    $fixtureManifest = Join-Path $fixture 'fixture.manifest.json'
    Write-Json $fixtureManifest $fixtureCatalog
    $fixtureCatalog = Read-VerifiedPayload -Stage $fixtureStage -Manifest $fixtureManifest -ExpectedHash (Get-Hash $fixtureManifest)
    $result = Invoke-UpgradeTransaction -Stage $fixtureStage -Catalog $fixtureCatalog -Root $fixtureInstall -Work (Join-Path $fixture 'transactions') -Receipts $receipts -Fixture $true
    if ((Get-Hash $sentinel) -ne $sentinelHash) { throw 'Rollback changed an unrelated file.' }
    $result.unknownFilePreserved = $true
    $result.fixtureRoot = $fixture
    Write-Json (Join-Path $receipts 'compact-rollback-check.json') $result
    $result | ConvertTo-Json -Depth 5
    return
}
$install = Get-FullPath $InstallDirectory
Assert-CompactInstallation $install
foreach ($directory in @($work,$receipts)) {
    if ($directory -ieq $install -or (Test-Under $directory $install) -or (Test-Under $install $directory)) { throw 'Work/receipt and installation directories must be separate.' }
}
if ($RecoverTransaction) {
    $transaction = Get-FullPath $RecoverTransaction
    if (!(Test-Under $transaction $work)) { throw 'Recovery must be inside the explicit task work directory.' }
    Assert-NoReparse $transaction
    $journal = Get-Content -LiteralPath (Join-Path $transaction 'transaction.json') -Raw | ConvertFrom-Json
    if ($journal.schemaVersion -ne 1 -or $journal.installRoot -ine $install -or $journal.state -ne 'applying') { throw 'No applying transaction for this exact installation.' }
    if (!$PSCmdlet.ShouldProcess($install, 'Recover the previous known Compact payload and settings')) { return }
    Stop-MatchingApplication $install @(Get-MatchingProcess $install)
    Restore-Transaction $journal $transaction
    if ($journal.wasRunning) {
        $restored = Start-Process -FilePath (Join-Path $install 'Egoist.Voice.exe') -ArgumentList '--background' -WindowStyle Hidden -PassThru
        if ($restored.WaitForExit(2000)) { throw 'Previous application exited after interrupted-transaction recovery.' }
    }
    New-Item -ItemType Directory -Path $receipts -Force | Out-Null
    $result = [ordered]@{passed=$true;recovered=$true;transactionRoot=$transaction;installerExecuted=$false;generatedAt=[DateTime]::UtcNow.ToString('o')}
    Write-Json (Join-Path $receipts 'compact-recovery.json') $result
    $result | ConvertTo-Json
    return
}
$stage = Get-FullPath $StagingDirectory
if ($stage -ieq $install -or (Test-Under $stage $install) -or (Test-Under $install $stage)) { throw 'Staging and installation directories must be separate.' }
foreach ($directory in @($work,$receipts)) {
    if ($directory -ieq $stage -or (Test-Under $directory $stage)) { throw 'Work/receipt directories cannot be inside staging.' }
}
$manifest = Get-FullPath $ManifestPath
$catalog = Read-VerifiedPayload -Stage $stage -Manifest $manifest -ExpectedHash $ManifestSha256
foreach ($file in @($catalog.files)) { $null = Get-PayloadPath $install $file.path }
if (Test-Path -LiteralPath $work) {
    foreach ($candidate in @(Get-ChildItem -LiteralPath $work -Filter 'compact-upgrade-*' -Directory)) {
        $journalPath = Join-Path $candidate.FullName 'transaction.json'
        Assert-NoReparse $journalPath
        if (Test-Path -LiteralPath $journalPath) {
            $pending = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
            if ($pending.state -eq 'applying') { throw ('Pending transaction must be recovered before another update: ' + $candidate.FullName) }
        }
    }
}
if ($PlanOnly -or !$PSCmdlet.ShouldProcess($install, ('Upgrade exact Compact payload to ' + $catalog.version + ' and select literal Russian RNNT'))) {
    [ordered]@{passed=$true;planOnly=$true;version=$catalog.version;fileCount=$catalog.fileCount;modelAssetCount=$catalog.modelAssetCount;manifestSha256=$ManifestSha256.ToLowerInvariant();unknownFilesPreserved=$true;userChoicesPreserved=$true;installerExecuted=$false} | ConvertTo-Json
    return
}
Invoke-UpgradeTransaction -Stage $stage -Catalog $catalog -Root $install -Work $work -Receipts $receipts -Fixture $false -ReadySeconds $ReadyTimeoutSeconds | ConvertTo-Json -Depth 5
