#requires -Version 7.0
Describe 'Compact installation transaction boundaries' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $updater = Join-Path $projectRoot 'scripts\Update-CompactInstallation.ps1'
        if (!$env:EGOIST_UPGRADE_TEST_WORK) { throw 'Set EGOIST_UPGRADE_TEST_WORK to this task work directory; tests never use global TEMP.' }
        $testRoot = Join-Path ([IO.Path]::GetFullPath($env:EGOIST_UPGRADE_TEST_WORK)) ('pester-compact-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $testRoot | Out-Null
        $utf8 = [Text.UTF8Encoding]::new($false)
        function New-UpgradeFixture([string]$Version = '2.3.0', [switch]$PrimaryOnly) {
            $fixture = Join-Path $testRoot ([Guid]::NewGuid().ToString('N') + ' [Русский]')
            $stage = Join-Path $fixture 'stage'
            $installed = Join-Path $fixture 'installed'
            New-Item -ItemType Directory -Path $stage,$installed -Force | Out-Null
            foreach ($name in @('Egoist.Voice.exe','Egoist.Voice.dll','coreclr.dll','egoist-voice.portable')) {
                [IO.File]::WriteAllText((Join-Path $stage $name), ('new synthetic ' + $name), $utf8)
                [IO.File]::WriteAllText((Join-Path $installed $name), ('old synthetic ' + $name), $utf8)
            }
            $modelIds = @('gigaam-v3-rnnt-int8-v1','gigaam-v3-rnnt-decoder-v1','gigaam-v3-rnnt-joiner-v1','gigaam-v3-rnnt-tokens-v1')
            if ([version]($Version -split '-', 2)[0] -ge [version]'2.4.0' -and !$PrimaryOnly) {
                $modelIds += @('gigaam-v3-e2e-rnnt-int8-v1','gigaam-v3-e2e-rnnt-decoder-v1','gigaam-v3-e2e-rnnt-joiner-v1','gigaam-v3-e2e-rnnt-tokens-v1')
            }
            $models = foreach ($id in $modelIds) {
                $part = $id
                $relative = 'Models/Speech/' + $id + '/fixture.bin'
                $path = Join-Path $stage $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
                [IO.File]::WriteAllText($path, $part, $utf8)
                [ordered]@{Id=$id;FileName='fixture.bin';SizeBytes=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
            }
            [IO.File]::WriteAllText((Join-Path $stage 'compact-models.json'), ($models | ConvertTo-Json), $utf8)
            $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
                [ordered]@{path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
            })
            $manifest = Join-Path $fixture 'manifest.json'
            $catalog = [ordered]@{schemaVersion=1;version=$Version;sourceRevision=('a' * 40);sourceDirty=$false;fileCount=$files.Count;files=$files}
            [IO.File]::WriteAllText($manifest, ($catalog | ConvertTo-Json -Depth 8), $utf8)
            $sentinel = Join-Path $installed 'unknown.txt'
            [IO.File]::WriteAllText($sentinel, 'user-owned unrelated bytes', $utf8)
            return @{fixture=$fixture;stage=$stage;installed=$installed;manifest=$manifest;catalog=$catalog;sentinel=$sentinel;arguments=@{StagingDirectory=$stage;ManifestPath=$manifest;ManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash;InstallDirectory=$installed;WorkDirectory=(Join-Path $fixture 'work');ReceiptDirectory=(Join-Path $fixture 'receipts')}}
        }
        $tokens = $null; $parseErrors = $null
        $updaterAst = [Management.Automation.Language.Parser]::ParseFile($updater, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw 'Updater parse errors.' }
        $readinessDefinition = $updaterAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-ReadinessState'}, $true)
        . ([scriptblock]::Create($readinessDefinition.Extent.Text))
        function Save-ChangedCatalog($Fixture) {
            [IO.File]::WriteAllText($Fixture.manifest, ($Fixture.catalog | ConvertTo-Json -Depth 8), $utf8)
            $Fixture.arguments.ManifestSha256 = (Get-FileHash -LiteralPath $Fixture.manifest).Hash
        }
    }

    It 'validates paths with brackets and Russian text without creating work or changing installed files in PlanOnly' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $before = (Get-FileHash -LiteralPath (Join-Path $fixture.installed 'Egoist.Voice.dll')).Hash
        $result = (& $updater @upgradeArguments -PlanOnly) | ConvertFrom-Json
        $result.planOnly | Should -BeTrue
        $result.modelAssetCount | Should -Be 4
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
        Test-Path -LiteralPath $fixture.arguments.ReceiptDirectory | Should -BeFalse
        (Get-FileHash -LiteralPath (Join-Path $fixture.installed 'Egoist.Voice.dll')).Hash | Should -Be $before
        [IO.File]::ReadAllText($fixture.sentinel) | Should -Be 'user-owned unrelated bytes'
    }

    It 'accepts all eight quality models from 2.4 onward and reports the actual count' {
        foreach ($version in @('2.4.0','2.4.0-rc.1','2.5.0')) {
            $fixture = New-UpgradeFixture -Version $version
            $upgradeArguments = $fixture.arguments
            $result = (& $updater @upgradeArguments -PlanOnly) | ConvertFrom-Json
            $result.modelAssetCount | Should -Be 8
            Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
        }
    }

    It 'rejects a quality package with only the primary four models before creating a transaction' {
        $fixture = New-UpgradeFixture -Version '2.4.0' -PrimaryOnly
        $upgradeArguments = $fixture.arguments
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*formatting RNNT assets*'
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects a renamed missing formatter even when the catalog still has eight entries' {
        $fixture = New-UpgradeFixture -Version '2.4.0'
        $upgradeArguments = $fixture.arguments
        $modelsPath = Join-Path $fixture.stage 'compact-models.json'
        $models = @(Get-Content -LiteralPath $modelsPath -Raw | ConvertFrom-Json)
        $models[7].Id = 'gigaam-unrelated-model'
        [IO.File]::WriteAllText($modelsPath, ($models | ConvertTo-Json), $utf8)
        $entry = @($fixture.catalog.files | Where-Object path -eq 'compact-models.json')[0]
        $entry.sha256 = (Get-FileHash -LiteralPath $modelsPath).Hash.ToLowerInvariant()
        $entry.bytes = (Get-Item -LiteralPath $modelsPath).Length
        Save-ChangedCatalog $fixture
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*formatting RNNT model asset is missing*'
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects eight models declared as the legacy 2.3 package' {
        $fixture = New-UpgradeFixture -Version '2.4.0'
        $fixture.catalog.version = '2.3.0'
        Save-ChangedCatalog $fixture
        $upgradeArguments = $fixture.arguments
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*Version requires*'
    }

    It 'supports WhatIf without creating transaction paths' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $result = (& $updater @upgradeArguments -WhatIf) | ConvertFrom-Json
        $result.planOnly | Should -BeTrue
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects tampered manifests before any write' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        [IO.File]::AppendAllText($fixture.manifest, ' ', $utf8)
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*manifest hash mismatch*'
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects changed model bytes rather than trusting the previous catalog' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $encoder = Join-Path $fixture.stage 'Models/Speech/gigaam-v3-rnnt-int8-v1/fixture.bin'
        [IO.File]::WriteAllText($encoder, 'changed model bytes', $utf8)
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*integrity failure*'
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects an undeclared staging file' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        [IO.File]::WriteAllText((Join-Path $fixture.stage 'undeclared.txt'), 'extra', $utf8)
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*file count*'
    }

    It 'rejects Data or parent traversal in the payload list' {
        foreach ($unsafe in @('Data/dictation.json','../outside.txt')) {
            $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
            $fixture.catalog.files[0].path = $unsafe
            Save-ChangedCatalog $fixture
            { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*Unsafe/private payload*'
            Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
        }
    }

    It 'rejects duplicate payload entries' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $fixture.catalog.files[1].path = $fixture.catalog.files[0].path
        Save-ChangedCatalog $fixture
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*Invalid/duplicate*'
    }

    It 'rejects a model catalog inconsistent with the exact payload' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $modelsPath = Join-Path $fixture.stage 'compact-models.json'
        $models = @(Get-Content -LiteralPath $modelsPath -Raw | ConvertFrom-Json)
        $models[0].Sha256 = ('0' * 64)
        [IO.File]::WriteAllText($modelsPath, ($models | ConvertTo-Json), $utf8)
        $entry = @($fixture.catalog.files | Where-Object path -eq 'compact-models.json')[0]
        $entry.sha256 = (Get-FileHash -LiteralPath $modelsPath).Hash.ToLowerInvariant()
        $entry.bytes = (Get-Item -LiteralPath $modelsPath).Length
        Save-ChangedCatalog $fixture
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*catalog differs*'
    }

    It 'refuses a new transaction while a previous replacement is pending' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $pending = Join-Path $fixture.arguments.WorkDirectory 'compact-upgrade-pending'
        New-Item -ItemType Directory -Path $pending -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $pending 'transaction.json'), '{"state":"applying"}', $utf8)
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*Pending transaction*'
        [IO.File]::ReadAllText($fixture.sentinel) | Should -Be 'user-owned unrelated bytes'
    }

    It 'performs actual DLL/settings replacement and verifies complete rollback in its own isolated fixture' {
        $fixtureWork = Join-Path $testRoot 'rollback work [проверка]'
        $receipts = Join-Path $testRoot 'rollback receipts'
        $result = (& $updater -VerifyRollback -WorkDirectory $fixtureWork -ReceiptDirectory $receipts) | ConvertFrom-Json
        $result.passed | Should -BeTrue
        $result.rollbackVerified | Should -BeTrue
        $result.modelAssetCount | Should -Be 8
        $result.speechPunctuationEnabled | Should -BeTrue
        $result.formatterRequired | Should -BeTrue
        $result.speechPreferencePreserved | Should -BeTrue
        $result.russianProfileApplied | Should -BeTrue
        $result.userChoicesPreserved | Should -BeTrue
        $result.unknownFilePreserved | Should -BeTrue
        $result.userProcessesTouched | Should -BeFalse
        $journal = Get-Content -LiteralPath (Join-Path $result.transactionRoot 'transaction.json') -Raw | ConvertFrom-Json
        $journal.state | Should -Be 'rolled-back'
        foreach ($file in $journal.files) {
            $target = Join-Path $journal.installRoot $file.path
            if ($file.existed) {
                (Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() | Should -Be $file.previousSha256
            } else { Test-Path -LiteralPath $target | Should -BeFalse }
        }
        $settings = Join-Path $journal.installRoot 'Data/dictation.json'
        (Get-FileHash -LiteralPath $settings).Hash.ToLowerInvariant() | Should -Be $journal.previousSettingsSha256
    }

    It 'preserves explicit disabled audio punctuation during replacement and exact rollback' {
        $result = (& $updater -VerifyRollback -RollbackSpeechPunctuation Disabled -WorkDirectory (Join-Path $testRoot 'disabled work') -ReceiptDirectory (Join-Path $testRoot 'disabled receipts')) | ConvertFrom-Json
        $result.rollbackVerified | Should -BeTrue
        $result.modelAssetCount | Should -Be 8
        $result.speechPunctuationEnabled | Should -BeFalse
        $result.formatterRequired | Should -BeFalse
        $result.speechPreferencePreserved | Should -BeTrue
        $journal = Get-Content -LiteralPath (Join-Path $result.transactionRoot 'transaction.json') -Raw | ConvertFrom-Json
        $restored = Get-Content -LiteralPath (Join-Path $journal.installRoot 'Data/dictation.json') -Raw | ConvertFrom-Json
        $restored.formatSpeechPunctuation | Should -BeFalse
        (Get-FileHash -LiteralPath (Join-Path $journal.installRoot 'Data/dictation.json')).Hash.ToLowerInvariant() | Should -Be $journal.previousSettingsSha256
    }

    It 'keeps four-asset legacy rollback compatible without requiring a formatter' {
        $result = (& $updater -VerifyRollback -RollbackFixtureVersion '2.3.0' -WorkDirectory (Join-Path $testRoot 'legacy work') -ReceiptDirectory (Join-Path $testRoot 'legacy receipts')) | ConvertFrom-Json
        $result.rollbackVerified | Should -BeTrue
        $result.modelAssetCount | Should -Be 4
        $result.speechPunctuationEnabled | Should -BeTrue
        $result.formatterRequired | Should -BeFalse
    }

    It 'rejects an invalid punctuation preference before changing payload or creating work' {
        $fixture = New-UpgradeFixture -Version '2.4.0'
        $upgradeArguments = $fixture.arguments
        $data = Join-Path $fixture.installed 'Data'
        New-Item -ItemType Directory -Path $data | Out-Null
        $settingsPath = Join-Path $data 'dictation.json'
        [IO.File]::WriteAllText($settingsPath, '{"formatSpeechPunctuation":"false"}', $utf8)
        $beforeSettings = (Get-FileHash -LiteralPath $settingsPath).Hash
        $beforePayload = (Get-FileHash -LiteralPath (Join-Path $fixture.installed 'Egoist.Voice.dll')).Hash
        { & $updater @upgradeArguments } | Should -Throw '*must be a JSON boolean*'
        (Get-FileHash -LiteralPath $settingsPath).Hash | Should -Be $beforeSettings
        (Get-FileHash -LiteralPath (Join-Path $fixture.installed 'Egoist.Voice.dll')).Hash | Should -Be $beforePayload
        Test-Path -LiteralPath $fixture.arguments.WorkDirectory | Should -BeFalse
    }

    It 'requires fresh primary and formatter markers for the exact PID when enabled' {
        $started = [datetime]'2026-10-02T03:00:00Z'
        $primary = '2026-10-02T03:00:01Z [7654] Russian ASR ready: engine=GigaAM v3 RNNT'
        $formatter = '2026-10-02T03:00:02Z [7654] Russian formatter ready: engine=GigaAM v3 E2E RNNT'
        (Get-ReadinessState -Lines @($primary) -ProcessId 7654 -StartedUtc $started -RequireFormatter $true).Ready | Should -BeFalse
        $state = Get-ReadinessState -Lines @($primary,$formatter) -ProcessId 7654 -StartedUtc $started -RequireFormatter $true
        $state.AsrReady | Should -BeTrue
        $state.FormatterReady | Should -BeTrue
        $state.Ready | Should -BeTrue
    }

    It 'accepts primary readiness alone when audio punctuation is explicitly disabled' {
        $state = Get-ReadinessState -Lines @('2026-10-02T03:00:01Z [7654] Russian ASR ready: engine=GigaAM v3 RNNT') -ProcessId 7654 -StartedUtc ([datetime]'2026-10-02T03:00:00Z') -RequireFormatter $false
        $state.Ready | Should -BeTrue
        $state.FormatterReady | Should -BeFalse
    }

    It 'rejects stale other-PID wrong-engine and malformed readiness markers' {
        $lines = @('2026-10-02T02:59:59Z [7654] Russian ASR ready: engine=GigaAM v3 RNNT','2026-10-02T03:00:01Z [7655] Russian ASR ready: engine=GigaAM v3 RNNT','2026-10-02T03:00:01Z [7654] Russian ASR ready: engine=GigaAM v3 E2E RNNT','invalid-time [7654] Russian ASR ready: engine=GigaAM v3 RNNT','2026-10-02T03:00:02Z [7654] Russian formatter ready: engine=GigaAM v3 E2E RNNT')
        $state = Get-ReadinessState -Lines $lines -ProcessId 7654 -StartedUtc ([datetime]'2026-10-02T03:00:00Z') -RequireFormatter $true
        $state.AsrReady | Should -BeFalse
        $state.FormatterReady | Should -BeTrue
        $state.Ready | Should -BeFalse
    }

    It 'recovers a recorded interrupted transaction without touching a user process' {
        $fixtureWork = Join-Path $testRoot 'recovery work'
        $receipts = Join-Path $testRoot 'recovery receipts'
        $result = (& $updater -VerifyRollback -WorkDirectory $fixtureWork -ReceiptDirectory $receipts) | ConvertFrom-Json
        $journalPath = Join-Path $result.transactionRoot 'transaction.json'
        $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
        $journal.state = 'applying'
        [IO.File]::WriteAllText($journalPath, ($journal | ConvertTo-Json -Depth 8), $utf8)
        [IO.File]::WriteAllText((Join-Path $journal.installRoot 'Egoist.Voice.dll'), 'interrupted replacement', $utf8)
        [IO.File]::WriteAllText((Join-Path $journal.installRoot 'Data/dictation.json'), '{"interrupted":true}', $utf8)
        $recovered = (& $updater -RecoverTransaction $result.transactionRoot -InstallDirectory $journal.installRoot -WorkDirectory (Split-Path -Parent $result.transactionRoot) -ReceiptDirectory $receipts) | ConvertFrom-Json
        $recovered.passed | Should -BeTrue
        $recovered.recovered | Should -BeTrue
        (Get-FileHash -LiteralPath (Join-Path $journal.installRoot 'Egoist.Voice.dll')).Hash.ToLowerInvariant() | Should -Be (@($journal.files | Where-Object path -eq 'Egoist.Voice.dll')[0].previousSha256)
        (Get-FileHash -LiteralPath (Join-Path $journal.installRoot 'Data/dictation.json')).Hash.ToLowerInvariant() | Should -Be $journal.previousSettingsSha256
    }

    It 'refuses payloads under a reparse ancestor without writing outside staging' {
        $fixture = New-UpgradeFixture
        $upgradeArguments = $fixture.arguments
        $link = Join-Path $fixture.fixture 'linked-install'
        New-Item -ItemType Junction -Path $link -Target $fixture.installed | Out-Null
        $fixture.arguments.InstallDirectory = $link
        { & $updater @upgradeArguments -PlanOnly } | Should -Throw '*Reparse*'
        [IO.File]::ReadAllText($fixture.sentinel) | Should -Be 'user-owned unrelated bytes'
    }
}
