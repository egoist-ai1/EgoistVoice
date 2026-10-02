#requires -Version 7.0
Describe 'Compact installation transaction boundaries' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $updater = Join-Path $projectRoot 'scripts\Update-CompactInstallation.ps1'
        if (!$env:EGOIST_UPGRADE_TEST_WORK) { throw 'Set EGOIST_UPGRADE_TEST_WORK to this task work directory; tests never use global TEMP.' }
        $testRoot = Join-Path ([IO.Path]::GetFullPath($env:EGOIST_UPGRADE_TEST_WORK)) ('pester-compact-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $testRoot | Out-Null
        $utf8 = [Text.UTF8Encoding]::new($false)
        function New-UpgradeFixture {
            $fixture = Join-Path $testRoot ([Guid]::NewGuid().ToString('N') + ' [Русский]')
            $stage = Join-Path $fixture 'stage'
            $installed = Join-Path $fixture 'installed'
            New-Item -ItemType Directory -Path $stage,$installed -Force | Out-Null
            foreach ($name in @('Egoist.Voice.exe','Egoist.Voice.dll','coreclr.dll','egoist-voice.portable')) {
                [IO.File]::WriteAllText((Join-Path $stage $name), ('new synthetic ' + $name), $utf8)
                [IO.File]::WriteAllText((Join-Path $installed $name), ('old synthetic ' + $name), $utf8)
            }
            $models = foreach ($part in @('encoder','decoder','joiner','tokens')) {
                $id = 'gigaam-' + $part
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
            $catalog = [ordered]@{schemaVersion=1;version='2.3.0';sourceRevision=('a' * 40);sourceDirty=$false;fileCount=$files.Count;files=$files}
            [IO.File]::WriteAllText($manifest, ($catalog | ConvertTo-Json -Depth 8), $utf8)
            $sentinel = Join-Path $installed 'unknown.txt'
            [IO.File]::WriteAllText($sentinel, 'user-owned unrelated bytes', $utf8)
            return @{fixture=$fixture;stage=$stage;installed=$installed;manifest=$manifest;catalog=$catalog;sentinel=$sentinel;arguments=@{StagingDirectory=$stage;ManifestPath=$manifest;ManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash;InstallDirectory=$installed;WorkDirectory=(Join-Path $fixture 'work');ReceiptDirectory=(Join-Path $fixture 'receipts')}}
        }
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
        $encoder = Join-Path $fixture.stage 'Models/Speech/gigaam-encoder/fixture.bin'
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
