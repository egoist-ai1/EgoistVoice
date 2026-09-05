[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublicationManifest,[switch]$Apply)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$plan=Get-Content -LiteralPath $PublicationManifest -Raw|ConvertFrom-Json
if($plan.repository -ne 'egoist-ai1/EgoistVoice' -or $plan.tag -ne 'v2.2.0-preview.2' -or $plan.sourceCommit -notmatch '^[a-f0-9]{40}$'){throw 'Unexpected publication identity.'}
$source=[IO.Path]::GetFullPath($plan.sourceDirectory)
$assets=[IO.Path]::GetFullPath($plan.assetDirectory).TrimEnd('\')
$missingAssets=[Collections.Generic.List[string]]::new()
foreach($f in $plan.assets){
    if($f.name -notmatch '^[A-Za-z0-9_.-]+$' -or $f.sha256 -notmatch '^[a-f0-9]{64}$' -or $f.bytes -ge 2147483648){throw 'Unsafe release asset.'}
    $path=Join-Path $assets $f.name
    if((Get-Item -LiteralPath $path).Length -ne $f.bytes -or (Get-FileHash -LiteralPath $path).Hash -ne $f.sha256){throw ('Asset changed: '+$f.name)}
}
$head=(& git.exe -C $source rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $head -ne $plan.sourceCommit){throw 'Source HEAD differs from reviewed commit.'}
$origin=(& git.exe -C $source remote get-url origin).Trim()
if($origin -ne 'https://github.com/egoist-ai1/EgoistVoice.git'){throw 'Unexpected Git destination.'}
& git.exe -C $source diff --quiet HEAD
if($LASTEXITCODE -ne 0){throw 'Tracked source differs from reviewed commit.'}
if((Get-FileHash -LiteralPath $plan.notesPath).Hash -ne $plan.notesSha256){throw 'Release notes changed.'}
if(!$Apply){@{passed=$true;planOnly=$true;repository=$plan.repository;tag=$plan.tag;commit=$head;assetCount=@($plan.assets).Count}|ConvertTo-Json;exit 0}
function Git([string[]]$Arguments){& git.exe -C $source @Arguments;if($LASTEXITCODE -ne 0){throw 'Git publication step failed.'}}
function Gh([string[]]$Arguments){& gh.exe @Arguments;if($LASTEXITCODE -ne 0){throw 'GitHub publication step failed.'}}
$remoteMain=(& git.exe -C $source ls-remote origin refs/heads/main) -split '\s+'
if($LASTEXITCODE -ne 0 -or $remoteMain[0] -notin @($plan.baseCommit,$head)){throw 'Remote main advanced; preserve it and review integration.'}
$tagExists=& git.exe -C $source tag --list $plan.tag
if($tagExists){$tagCommit=(& git.exe -C $source rev-list -n 1 $plan.tag).Trim();if($tagCommit -ne $head){throw 'Existing tag belongs to another commit.'}}
else{Git @('tag','-a',$plan.tag,$head,'-m','Egoist Voice 2.2 Preview 2: Compact RU and Full + Qwen')}
Git @('push','--atomic','origin',($head+':refs/heads/codex/voice-preview2'),('refs/tags/'+$plan.tag))
$raw=& gh.exe api ('repos/'+$plan.repository+'/releases/tags/'+$plan.tag) 2>$null
if($LASTEXITCODE -ne 0){
    Gh @('release','create',$plan.tag,'--repo',$plan.repository,'--verify-tag','--target',$head,'--prerelease','--draft','--title','Egoist Voice 2.2 Preview 2 — Compact RU / Full + Qwen','--notes-file',$plan.notesPath)
    $raw=& gh.exe api ('repos/'+$plan.repository+'/releases/tags/'+$plan.tag)
    if($LASTEXITCODE -ne 0){throw 'Draft release not readable.'}
}
$release=($raw -join "`n")|ConvertFrom-Json
if(!$release.draft){throw 'Release is already public; do not alter it through this creation script.'}
foreach($f in $plan.assets){
    $existing=@($release.assets|Where-Object {$_.name -eq $f.name})
    if($existing.Count){
        if($existing.Count -ne 1 -or $existing[0].digest -ne ('sha256:'+$f.sha256) -or $existing[0].size -ne $f.bytes){throw 'Existing draft asset differs; no overwrite is allowed.'}
    }else{
        $missingAssets.Add((Join-Path $assets $f.name))
    }
}
if($missingAssets.Count){
    Write-Output ('Uploading '+$missingAssets.Count+' reviewed release assets')
    Gh -Arguments (@('release','upload',$plan.tag,'--repo',$plan.repository)+$missingAssets.ToArray())
}
$remoteRaw=& gh.exe api ('repos/'+$plan.repository+'/releases/'+$release.id+'/assets') --paginate
if($LASTEXITCODE -ne 0){throw 'Uploaded assets not readable.'}
$remote=($remoteRaw -join "`n")|ConvertFrom-Json
if(@($remote).Count -ne @($plan.assets).Count){throw 'Unexpected remote asset count.'}
foreach($f in $plan.assets){
    $r=@($remote|Where-Object {$_.name -eq $f.name})
    if($r.Count -ne 1 -or $r[0].state -ne 'uploaded' -or $r[0].size -ne $f.bytes -or $r[0].digest -ne ('sha256:'+$f.sha256)){throw ('Remote asset failed identity check: '+$f.name)}
}
Gh @('release','edit',$plan.tag,'--repo',$plan.repository,'--draft=false','--prerelease','--latest=false')
Git @('push','origin',($head+':refs/heads/main'))
Gh @('repo','edit',$plan.repository,'--description','Локальная диктовка для Windows: Compact RU portable и Full с Whisper, Qwen и переводом. Без облачного аккаунта.','--homepage',('https://github.com/'+$plan.repository+'/releases/tag/'+$plan.tag),'--add-topic','speech-to-text','--add-topic','offline','--add-topic','windows','--add-topic','dictation','--add-topic','wpf','--add-topic','qwen','--add-topic','whisper','--add-topic','portable')
Write-Output ('Published https://github.com/'+$plan.repository+'/releases/tag/'+$plan.tag)
