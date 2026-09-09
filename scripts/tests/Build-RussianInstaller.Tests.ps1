Describe 'Russian installer build boundaries' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $builder = Join-Path $projectRoot 'scripts\Build-RussianInstaller.ps1'
        $inputDirectory = Join-Path $TestDrive 'inputs [with brackets]'
        New-Item -ItemType Directory -Path $inputDirectory | Out-Null
        $model = Join-Path $inputDirectory 'model.gguf'
        $runtime = Join-Path $inputDirectory 'runtime.zip'
        [IO.File]::WriteAllText($model, 'synthetic invalid model')
        [IO.File]::WriteAllText($runtime, 'synthetic invalid archive')
        $buildArguments = @{
            OutputDirectory = Join-Path $projectRoot ('artifacts\plan-check-' + [Guid]::NewGuid().ToString('N'))
            WorkDirectory = Join-Path $TestDrive 'work not created'
            SpeechModelsRoot = $inputDirectory
            TextModelPath = $model
            TextRuntimeZip = $runtime
            VcRuntimeDirectory = $inputDirectory
            IncludeTextEditor = $true
        }
    }

    It 'plans using literal input paths without creating output or work directories' {
        $result = (& $builder @buildArguments) | ConvertFrom-Json
        $result.planOnly | Should -BeTrue
        Test-Path -LiteralPath $buildArguments.OutputDirectory | Should -BeFalse
        Test-Path -LiteralPath $buildArguments.WorkDirectory | Should -BeFalse
    }

    It 'rejects an output outside this project without touching its contents' {
        $outside = Join-Path $TestDrive 'unrelated output'
        New-Item -ItemType Directory -Path $outside | Out-Null
        $sentinel = Join-Path $outside 'keep.txt'
        [IO.File]::WriteAllText($sentinel, 'preserve')
        $arguments = $buildArguments.Clone()
        $arguments.OutputDirectory = $outside
        { & $builder @arguments -Build } | Should -Throw '*inside project artifacts*'
        [IO.File]::ReadAllText($sentinel) | Should -Be 'preserve'
    }

    It 'plans the default speech package without text model or runtime inputs' {
        $arguments = $buildArguments.Clone()
        foreach ($key in @('TextModelPath', 'TextRuntimeZip', 'VcRuntimeDirectory', 'IncludeTextEditor')) {
            $arguments.Remove($key)
        }
        $result = (& $builder @arguments) | ConvertFrom-Json
        $result.includeTextEditor | Should -BeFalse
        Test-Path -LiteralPath $arguments.OutputDirectory | Should -BeFalse
    }

    It 'rejects corrupt model input before creating a staging directory' {
        { & $builder @buildArguments -Build } | Should -Throw '*Input integrity failure*'
        Test-Path -LiteralPath $buildArguments.OutputDirectory | Should -BeFalse
        [IO.File]::ReadAllText($model) | Should -Be 'synthetic invalid model'
    }
}
