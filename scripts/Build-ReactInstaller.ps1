[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$SpeechModelsRoot,
    [Parameter(Mandatory)][string]$WebModulesDirectory,
    [Parameter(Mandatory)][string]$PackagingModulesDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (!$output.StartsWith((Join-Path $project 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be inside project artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh artifact directory.' }
$source = Join-Path $project 'installer\react'
$node = (Get-Command node -ErrorAction Stop).Source
[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $project 'Egoist.Voice.csproj') -Raw
$version = [string]$versionDocument.Project.PropertyGroup.Version
$electronPackage = Get-Content -LiteralPath (Join-Path $PackagingModulesDirectory 'electron\package.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $output, $work -Force | Out-Null
$speechWork = Join-Path $work 'speech'
& (Join-Path $PSScriptRoot 'Build-RussianInstaller.ps1') -OutputDirectory (Join-Path $output 'speech-package') -WorkDirectory $speechWork -SpeechModelsRoot $SpeechModelsRoot -Build | Out-Null
$stage = Join-Path $work 'react-app'
$payload = Join-Path $work 'react-payload'
New-Item -ItemType Directory -Path $stage, $payload -Force | Out-Null
foreach ($name in @('main.cjs', 'preload.cjs')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $project 'assets\EgoistVoice.ico') -Destination (Join-Path $stage 'icon.ico')
Copy-Item -LiteralPath (Join-Path $WebModulesDirectory 'react\LICENSE') -Destination (Join-Path $stage 'react-LICENSE.txt')
$utf8 = [Text.UTF8Encoding]::new($false)
$metadata = [ordered]@{name='egoist-voice-installer'; version=$version; description='Egoist Voice offline installer'; author='EGOIST'; main='main.cjs'}
[IO.File]::WriteAllText((Join-Path $stage 'package.json'), ($metadata | ConvertTo-Json), $utf8)
$env:EGOIST_WEB_MODULES = [IO.Path]::GetFullPath($WebModulesDirectory)
$env:EGOIST_UI_OUTPUT = Join-Path $stage 'dist'
$env:EGOIST_UI_CACHE = Join-Path $work 'vite-cache'
& $node (Join-Path $source 'build-ui.mjs')
if ($LASTEXITCODE -ne 0) { throw 'React UI build failed.' }
$inner = Join-Path $speechWork 'inner'
$files = @(Get-ChildItem -LiteralPath $inner -File | Where-Object { $_.Extension -in @('.exe','.bin') })
$launch = @($files | Where-Object Extension -eq '.exe')
if ($launch.Count -ne 1 -or $files.Count -lt 2) { throw 'Unexpected Inno payload.' }
$entries = @($files | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $payload
    [ordered]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[IO.File]::WriteAllText((Join-Path $payload 'payload.json'), ([ordered]@{launch=$launch[0].Name;files=$entries} | ConvertTo-Json -Depth 4), $utf8)
$configuration = [ordered]@{
    appId='Egoist.Voice.Setup'; productName='Egoist Voice Setup'; electronVersion=$electronPackage.version
    electronDist=(Join-Path $PackagingModulesDirectory 'electron\dist')
    directories=@{app=$stage;output=$output}; asar=$true; npmRebuild=$false; compression='normal'
    files=@('main.cjs','preload.cjs','icon.ico','package.json','react-LICENSE.txt','dist/**/*')
    extraResources=@(@{from=$payload;to='payload';filter=@('**/*')})
    win=@{target=@(@{target='portable';arch=@('x64')});icon=(Join-Path $stage 'icon.ico');signExecutable=$false;artifactName=('EgoistVoice-Setup-Russian-' + $version + '-win-x64.exe')}
    portable=@{requestExecutionLevel='user';unpackDirName=$false}
}
$configPath = Join-Path $work 'electron-builder.json'
[IO.File]::WriteAllText($configPath, ($configuration | ConvertTo-Json -Depth 8), $utf8)
& $node (Join-Path $PackagingModulesDirectory 'electron-builder\cli.js') --config $configPath --win portable --x64 --publish never
if ($LASTEXITCODE -ne 0) { throw 'React installer packaging failed.' }
$installer = Join-Path $output ('EgoistVoice-Setup-Russian-' + $version + '-win-x64.exe')
$receipt = [ordered]@{ version=$version; sourceRevision=(& git -C $project rev-parse HEAD).Trim(); installer=$installer; bytes=(Get-Item -LiteralPath $installer).Length; sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant(); react='19.2.8';electron=$electronPackage.version;installerExecutedOnHost=$false;cleanWindowsVerified=$false }
[IO.File]::WriteAllText((Join-Path $output 'react-installer.json'), ($receipt | ConvertTo-Json), $utf8)
[IO.File]::WriteAllText(($installer + '.sha256'), ($receipt.sha256 + '  ' + [IO.Path]::GetFileName($installer) + "`n"), $utf8)
$receipt | ConvertTo-Json
