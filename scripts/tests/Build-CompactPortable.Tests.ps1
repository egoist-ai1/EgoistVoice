#requires -Version 7.0
Describe 'Russian quality portable build boundaries' {
    BeforeAll {
        if (!$env:EGOIST_UPGRADE_TEST_WORK) { throw 'Use the explicit task work directory for script tests.' }
        $sourceProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $testRoot = Join-Path ([IO.Path]::GetFullPath($env:EGOIST_UPGRADE_TEST_WORK)) ('portable-tests-' + [Guid]::NewGuid().ToString('N'))
        $fixtureProject = Join-Path $testRoot 'project [Русский]'
        $fixtureScripts = Join-Path $fixtureProject 'scripts'
        New-Item -ItemType Directory -Path $fixtureScripts -Force | Out-Null
        $builder = Join-Path $fixtureScripts 'Build-CompactPortable.ps1'
        Copy-Item -LiteralPath (Join-Path $sourceProject 'scripts\Build-CompactPortable.ps1') -Destination $builder
        $utf8 = [Text.UTF8Encoding]::new($false)
        foreach ($name in @('LICENSE','THIRD-PARTY-NOTICES.md')) { [IO.File]::WriteAllText((Join-Path $fixtureProject $name), 'Synthetic notice', $utf8) }
        [IO.File]::WriteAllText((Join-Path $fixtureProject 'Egoist.Voice.csproj'), '<Project><PropertyGroup><Version>2.4.0</Version><FileVersion>2.4.0.0</FileVersion></PropertyGroup></Project>', $utf8)
        $knownCatalog = @(
            [pscustomobject]@{Id='gigaam-v3-rnnt-int8-v1';FileName='encoder.onnx';SizeBytes=318995995;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-rnnt-decoder-v1';FileName='decoder.onnx';SizeBytes=3331577;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-rnnt-joiner-v1';FileName='joint.onnx';SizeBytes=1440448;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-rnnt-tokens-v1';FileName='tokens.txt';SizeBytes=195;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-e2e-rnnt-int8-v1';FileName='encoder.onnx';SizeBytes=318995997;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-e2e-rnnt-decoder-v1';FileName='decoder.onnx';SizeBytes=4600058;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-e2e-rnnt-joiner-v1';FileName='joint.onnx';SizeBytes=2712896;Sha256=('1' * 64)},
            [pscustomobject]@{Id='gigaam-v3-e2e-rnnt-tokens-v1';FileName='tokens.txt';SizeBytes=13353;Sha256=('1' * 64)}
        )
        function Set-PortableTestCatalog([object[]]$Models) {
            [IO.File]::WriteAllText($env:EGOIST_PORTABLE_TEST_CATALOG, ($Models | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        }
        function New-PortableFixture {
            $stage = Join-Path $fixtureProject ('artifacts\stage ' + [Guid]::NewGuid().ToString('N') + ' [Русский]')
            New-Item -ItemType Directory -Path $stage -Force | Out-Null
            foreach ($name in @('Egoist.Voice.exe','coreclr.dll')) { [IO.File]::WriteAllText((Join-Path $stage $name), 'Synthetic payload', $utf8) }
            return @{OutputDirectory=$stage;UseExistingPublish=$true;WorkDirectory=(Join-Path $testRoot 'diagnostics');InstalledModelsRoot=(Join-Path $testRoot 'missing-model-input')}
        }
    }
    BeforeEach {
        $env:EGOIST_PORTABLE_TEST_CATALOG = Join-Path $testRoot ('export-catalog-' + [Guid]::NewGuid().ToString('N') + '.json')
        Set-PortableTestCatalog -Models $knownCatalog
        Mock Start-Process {
            param($FilePath,$ArgumentList,$WindowStyle,$PassThru)
            $manifestPath = [string]$ArgumentList[1]
            [IO.File]::WriteAllText($manifestPath.Trim('"'), [IO.File]::ReadAllText($env:EGOIST_PORTABLE_TEST_CATALOG), [Text.UTF8Encoding]::new($false))
            $process = [pscustomobject]@{ExitCode=0}
            $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {param($Milliseconds) return $true}
            $process | Add-Member -MemberType ScriptMethod -Name Kill -Value {}
            return $process
        }
    }
    It 'requests the quality export flag using a hidden process and preserves environment on failure' {
        $arguments = New-PortableFixture
        $priorData = $env:EGOIST_VOICE_DATA_ROOT
        $priorLogs = $env:EGOISTVOICE_LOG_DIRECTORY
        { & $builder @arguments } | Should -Throw '*Build model missing*'
        Should -Invoke Start-Process -Times 1 -Exactly -ParameterFilter { $ArgumentList[0] -eq '--export-russian-quality-models' -and $WindowStyle -eq 'Hidden' }
        $env:EGOIST_VOICE_DATA_ROOT | Should -Be $priorData
        $env:EGOISTVOICE_LOG_DIRECTORY | Should -Be $priorLogs
        Test-Path -LiteralPath (Join-Path $arguments.OutputDirectory 'Models') | Should -BeFalse
    }
    It 'rejects the previous four-model export before copying any model' {
        Set-PortableTestCatalog -Models @($knownCatalog[0..3])
        $arguments = New-PortableFixture
        { & $builder @arguments } | Should -Throw '*eight primary and formatting*'
        Test-Path -LiteralPath (Join-Path $arguments.OutputDirectory 'Models') | Should -BeFalse
    }
    It 'rejects eight entries without the formatting half even with the same total bytes' {
        Set-PortableTestCatalog -Models @($knownCatalog | ForEach-Object { [pscustomobject]@{Id=$_.Id.Replace('-e2e-', '-other-');FileName=$_.FileName;SizeBytes=$_.SizeBytes;Sha256=$_.Sha256} })
        $arguments = New-PortableFixture
        { & $builder @arguments } | Should -Throw '*eight primary and formatting*'
        Test-Path -LiteralPath (Join-Path $arguments.OutputDirectory 'Models') | Should -BeFalse
    }
    It 'rejects a staging path outside its exact project without touching unrelated files' {
        $outside = Join-Path $testRoot 'unrelated'
        New-Item -ItemType Directory -Path $outside | Out-Null
        $sentinel = Join-Path $outside 'keep.txt'
        [IO.File]::WriteAllText($sentinel, 'Preserve', $utf8)
        { & $builder -OutputDirectory $outside -UseExistingPublish } | Should -Throw '*inside this project artifacts*'
        [IO.File]::ReadAllText($sentinel) | Should -Be 'Preserve'
        Should -Invoke Start-Process -Times 0 -Exactly
    }
    It 'copies and hashes all eight accepted model files with MIT attribution under the 1 GB contract' -Skip:(!$env:EGOIST_QUALITY_MODELS_TEST_ROOT -or !$env:EGOIST_QUALITY_MODEL_CATALOG_TEST) {
        $copyCatalog = @((Get-Content -LiteralPath $env:EGOIST_QUALITY_MODEL_CATALOG_TEST -Raw | ConvertFrom-Json).russianQuality)
        Set-PortableTestCatalog -Models $copyCatalog
        $arguments = New-PortableFixture
        $arguments.InstalledModelsRoot = $env:EGOIST_QUALITY_MODELS_TEST_ROOT
        $result = (& $builder @arguments) | ConvertFrom-Json
        $result.Bytes | Should -BeLessThan 1000000000
        $result.Bytes | Should -BeGreaterThan 650090519
        foreach ($model in $copyCatalog) {
            $path = Join-Path $arguments.OutputDirectory ('Models\Speech\' + $model.Id + '\' + $model.FileName)
            (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() | Should -Be $model.Sha256
            Test-Path -LiteralPath ($path + '.verified.json') | Should -BeTrue
        }
        Test-Path -LiteralPath (Join-Path $arguments.OutputDirectory 'Models\Licenses\GigaAM-MIT-LICENSE.txt') | Should -BeTrue
        [IO.File]::ReadAllText((Join-Path $arguments.OutputDirectory 'egoist-voice.portable')) | Should -Be 'gigaam-v3-rnnt-russian-quality-cpu-v3'
        [IO.File]::ReadAllText((Join-Path $arguments.OutputDirectory 'START-HERE.txt')) | Should -Match '8 модельных файлов'
    }
}
