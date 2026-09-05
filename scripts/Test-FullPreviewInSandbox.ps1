[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(!$Apply){@{planOnly=$true;scope='Isolated Windows Sandbox';steps='Offline install, payload identity, ASR, Qwen, translation, repair, other-owner preservation, uninstall'}|ConvertTo-Json;exit 0}
if($env:USERNAME -ne 'WDAGUtilityAccount' -or $PSScriptRoot -ne 'C:\VoiceInput'){throw 'Only the disposable Sandbox guest is supported.'}
$steps=[Collections.Generic.List[string]]::new()
$report='C:\VoiceOutput\full-lifecycle.json'
function Run([string]$Exe,[string[]]$Arguments,[int]$Timeout=600000){
    $p=Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $handle=$p.Handle
    if(!$p.WaitForExit($Timeout)){throw ('Timed out: '+[IO.Path]::GetFileName($Exe))}
    $p.WaitForExit();$p.Refresh()
    if($p.ExitCode -ne 0){throw ('Exit '+$p.ExitCode+': '+[IO.Path]::GetFileName($Exe))}
}
function Progress([string]$Text){
    $steps.Add($Text)
    [IO.File]::WriteAllText('C:\VoiceOutput\full-progress.json',(@{steps=$steps;at=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 4))
}
try{
    $receipt=Get-Content -LiteralPath 'C:\VoiceInput\full-installer.json' -Raw|ConvertFrom-Json
    foreach($f in $receipt.files){if((Get-FileHash -LiteralPath (Join-Path 'C:\FullInput' $f.name)).Hash -ne $f.sha256){throw 'Input hash differs.'}}
    $dest=Join-Path $env:LOCALAPPDATA 'Programs\Voice проверка\Full'
    $bootstrap='C:\FullInput\EgoistVoice-Full-Setup-2.2.0-preview.2.exe'
    Run $bootstrap @('--verify-only','--payload-dir','C:\FullInput')
    Run $bootstrap @('--offline','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$dest+'"'),'/LOG="C:\VoiceOutput\full-install.log"')
    $manifest=Get-Content -LiteralPath 'C:\VoiceInput\full-payload.manifest.json' -Raw|ConvertFrom-Json
    foreach($f in $manifest.files){
        if($f.path.StartsWith('{tmp}')){continue}
        $path=$f.path.Replace('{app}',$dest).Replace('{localappdata}',$env:LOCALAPPDATA)
        if((Get-FileHash -LiteralPath $path).Hash -ne $f.sha256){throw ('Installed hash differs: '+$f.path)}
    }
    $exe=Join-Path $dest 'Egoist.Voice.exe'
    if((Get-FileHash -LiteralPath (Join-Path $dest 'Egoist.Voice.dll')).Hash -ne $receipt.applicationSha256){throw 'Application identity differs.'}
    Progress 'Offline installation and every persistent payload hash passed'
    Run $exe @('--local-asr-check','C:\VoiceInput\synthetic-russian.wav','C:\VoiceInput\synthetic-reference.txt','C:\VoiceOutput\full-asr.json')
    if(!(Get-Content -LiteralPath 'C:\VoiceOutput\full-asr.json' -Raw|ConvertFrom-Json).passed){throw 'Native ASR failed.'}
    Progress 'Full native offline ASR passed'
    Run $exe @('--local-qwen-check','C:\VoiceOutput\full-qwen.json')
    if(!(Get-Content -LiteralPath 'C:\VoiceOutput\full-qwen.json' -Raw|ConvertFrom-Json).passed){throw 'Qwen formatting failed.'}
    Progress 'Packaged Qwen startup, formatting and correction fixtures passed'
    Run $exe @('--local-translation-comparison','C:\VoiceOutput\full-translation.json')
    $translation=Get-Content -LiteralPath 'C:\VoiceOutput\full-translation.json' -Raw|ConvertFrom-Json
    if(!$translation.completed -or @($translation.rows|Where-Object {!$_.hySucceeded}).Count){throw 'Packaged translation failed.'}
    Progress 'Packaged HY-MT translation completed for three synthetic examples'
    $data=Join-Path $env:LOCALAPPDATA 'EgoistVoice'
    $settings=Join-Path $data 'dictation.json'
    $dict=Join-Path $data 'dictionary.json'
    [IO.File]::WriteAllText($settings,'{"soundVolume":0.13,"saveRecentRecordings":false,"formatWithQwen":false}')
    [IO.File]::WriteAllText($dict,'[]')
    $settingsHash=(Get-FileHash -LiteralPath $settings).Hash
    $dictHash=(Get-FileHash -LiteralPath $dict).Hash
    $engine=Join-Path $env:LOCALAPPDATA 'EGOIST\TranslationEngine'
    $owner=Join-Path $engine 'owners\egoist-voice.owner.json'
    if(!(Test-Path -LiteralPath $owner)){throw 'Voice owner missing.'}
    $other=Join-Path $engine 'owners\egoist-translator.owner.json'
    $otherPath=Join-Path $env:LOCALAPPDATA 'Programs\Synthetic Translator Owner'
    New-Item -ItemType Directory -Path $otherPath -Force|Out-Null
    $key='Software\Microsoft\Windows\CurrentVersion\Uninstall\EgoistVoicePreviewSyntheticOwner'
    New-Item -Path ('HKCU:\'+$key) -Force|Out-Null
    $otherData=Get-Content -LiteralPath $owner -Raw|ConvertFrom-Json
    $otherData.ownerId='egoist-translator';$otherData.ownerInstallPath=$otherPath;$otherData.ownerUninstallKey='HKCU\'+$key
    [IO.File]::WriteAllText($other,($otherData|ConvertTo-Json -Depth 5))
    $otherHash=(Get-FileHash -LiteralPath $other).Hash
    $runtime=Join-Path $engine 'v1\runtime\llama-b10219-vulkan-win-x64-vc143\llama-server.exe'
    $runtimeHash=(Get-FileHash -LiteralPath $runtime).Hash
    Run $bootstrap @('--offline','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$dest+'"'),'/LOG="C:\VoiceOutput\full-repair.log"')
    if((Get-FileHash -LiteralPath $settings).Hash -ne $settingsHash -or (Get-FileHash -LiteralPath $dict).Hash -ne $dictHash -or (Get-FileHash -LiteralPath $other).Hash -ne $otherHash){throw 'Repair changed user/other-owner state.'}
    Progress 'Repair preserves settings, dictionary and a synthetic second owner'
    Run (Join-Path $dest 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG="C:\VoiceOutput\full-uninstall.log"')
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while((Test-Path -LiteralPath $exe) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    if(Test-Path -LiteralPath $exe){throw 'Application not removed.'}
    if(Test-Path -LiteralPath $owner){throw 'Voice owner not removed.'}
    if((Get-FileHash -LiteralPath $settings).Hash -ne $settingsHash -or (Get-FileHash -LiteralPath $dict).Hash -ne $dictHash -or (Get-FileHash -LiteralPath $other).Hash -ne $otherHash -or (Get-FileHash -LiteralPath $runtime).Hash -ne $runtimeHash){throw 'Uninstall damaged retained state.'}
    if(Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{79A42D80-A0E3-45CA-BBBC-E6B2E48EBBE2}_is1'){throw 'Uninstall registration remains.'}
    Progress 'Uninstall removes Voice and its owner; preserves settings, dictionary, runtime and other owner'
    @{passed=$true;generatedAt=[DateTime]::UtcNow.ToString('o');steps=$steps;applicationSha256=$receipt.applicationSha256;environment='Windows Sandbox, networking disabled';secondOwner='Synthetic lifecycle fixture, not a complete two-product coexistence test'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $report -Encoding UTF8
}catch{
    @{passed=$false;generatedAt=[DateTime]::UtcNow.ToString('o');steps=$steps;error=$_.Exception.Message}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $report -Encoding UTF8
    exit 1
}
