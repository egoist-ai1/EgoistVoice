[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$Apply)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$Apply){@{planOnly=$true;steps='Verify payload receipt, copy exact files, compile preview-pinned bootstrap, verify offline hashes'}|ConvertTo-Json;exit 0}
$payload=[IO.Path]::GetFullPath($PayloadDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$receipt=Get-Content -LiteralPath (Join-Path $payload 'full-installer.json') -Raw|ConvertFrom-Json
if($receipt.preview -ne 'v2.2.0-preview.2'){throw 'Unexpected preview identity.'}
$entries=[Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $output -Force|Out-Null
foreach($f in $receipt.files){
    if($f.name -notmatch '^EgoistVoice-Full-2\.2\.0-preview\.2-inner(-[1-9][0-9]*)?\.(exe|bin)$' -or $f.sha256 -notmatch '^[a-f0-9]{64}$' -or $f.bytes -ge 2147483648){throw 'Unsafe release entry.'}
    $source=Join-Path $payload $f.name
    if((Get-Item -LiteralPath $source).Length -ne $f.bytes -or (Get-FileHash -LiteralPath $source).Hash -ne $f.sha256){throw 'Payload mismatch.'}
    $target=Join-Path $output $f.name
    if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $target).Hash -ne $f.sha256){throw 'Existing asset differs.'}}
    else{Copy-Item -LiteralPath $source -Destination $target}
    $entries.Add(('new PayloadFile("{0}", {1}L, "{2}")' -f $f.name,$f.bytes,$f.sha256))
}
$manifest=@"
internal static class EgoistVoiceReleaseManifest {
 internal const string ApplicationVersion = "2.2.0";
 internal const string ReleaseTag = "v2.2.0-preview.2";
 internal const string ReleaseBaseUrl = "https://github.com/egoist-ai1/EgoistVoice/releases/download/v2.2.0-preview.2/";
 internal const string LaunchFile = "EgoistVoice-Full-2.2.0-preview.2-inner.exe";
 internal static readonly PayloadFile[] Files = new PayloadFile[] { $($entries -join ",`n") };
}
"@
$utf8=[Text.UTF8Encoding]::new($false)
$manifestPath=Join-Path $payload 'web-manifest.cs.txt'
[IO.File]::WriteAllText($manifestPath,$manifest,$utf8)
$assemblyPath=Join-Path $payload 'web-assembly.cs.txt'
[IO.File]::WriteAllText($assemblyPath,'using System.Reflection; [assembly: AssemblyTitle("Egoist Voice Full + Qwen")] [assembly: AssemblyCompany("EGOIST")] [assembly: AssemblyProduct("Egoist Voice")] [assembly: AssemblyVersion("2.2.0.0")] [assembly: AssemblyFileVersion("2.2.0.0")] [assembly: AssemblyInformationalVersion("2.2.0-preview.2")]',$utf8)
$exe=Join-Path $output 'EgoistVoice-Full-Setup-2.2.0-preview.2.exe'
if(Test-Path -LiteralPath $exe){throw 'Existing bootstrap preserved.'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+$exe) ('/win32icon:'+(Join-Path $project 'assets\EgoistVoice.ico')) /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $project 'installer\EgoistVoiceWebBootstrap.cs') $manifestPath $assemblyPath
if($LASTEXITCODE -ne 0){throw 'Bootstrap compilation failed.'}
$child=Start-Process -FilePath $exe -ArgumentList @('--verify-only','--payload-dir',('"'+$output+'"')) -WindowStyle Hidden -PassThru
$handle=$child.Handle
if(!$child.WaitForExit(120000)){throw 'Verification timed out.'}
$child.WaitForExit();$child.Refresh()
if($child.ExitCode -ne 0){throw 'Bootstrap offline verification failed.'}
$record=@{passed=$true;files=$receipt.files;name=[IO.Path]::GetFileName($exe);bytes=(Get-Item -LiteralPath $exe).Length;sha256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant();signed=$false;installerExecutedOnHost=$false}
[IO.File]::WriteAllText((Join-Path $payload 'bootstrap-verification.json'),($record|ConvertTo-Json -Depth 5),$utf8)
$record|ConvertTo-Json -Depth 5
