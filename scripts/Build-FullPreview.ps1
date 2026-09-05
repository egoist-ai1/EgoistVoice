[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppStaging,
    [Parameter(Mandatory)][string]$EngineBundleRoot,
    [Parameter(Mandatory)][string]$ModelsRoot,
    [Parameter(Mandatory)][string]$QwenModel,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Apply
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (!$Apply) { @{planOnly=$true;output=$output;steps='Validate explicit app, model and engine files; build Full preview without installing it'} | ConvertTo-Json; exit 0 }
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; existing builds are preserved.' }
foreach($root in @($AppStaging,$EngineBundleRoot,$ModelsRoot)) {
    if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'An input directory is missing.' }
    if (Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}) { throw 'Reparse input refused.' }
}
function Assert-Hash([string]$Path,[long]$Bytes,[string]$Hash) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -ne $Bytes -or (Get-FileHash -LiteralPath $Path).Hash -ne $Hash) { throw ('Input integrity failure: '+[IO.Path]::GetFileName($Path)) }
}
$engineManifest=Get-Content -LiteralPath (Join-Path $EngineBundleRoot 'engine-bundle-manifest.json') -Raw | ConvertFrom-Json
if ($engineManifest.engineVersion -ne '1.0.1' -or $engineManifest.runtimeId -ne 'llama-b10219-vulkan-win-x64-vc143') { throw 'Unexpected engine identity.' }
foreach($entry in $engineManifest.files) {
    if ($entry.path -match '(^/|\\|\.\.|:)' ) { throw 'Unsafe engine path.' }
    Assert-Hash (Join-Path $EngineBundleRoot $entry.path) $entry.bytes $entry.sha256
}
if (@(Get-ChildItem -LiteralPath $EngineBundleRoot -Recurse -File).Count -ne @($engineManifest.files).Count+1) { throw 'Undeclared engine file.' }
Assert-Hash $QwenModel 2497280256 '7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5'
$models=@(
    @('gigaam-v3-e2e-rnnt-int8-v1','gigaam_v3_e2e_rnnt_encoder_int8.onnx',318995997L,'2cac62d0c270bd128f898f2be1a2d34780d524a6e9483888ebac7b00f97410f1'),
    @('gigaam-v3-e2e-rnnt-decoder-v1','gigaam_v3_e2e_rnnt_decoder.onnx',4600058L,'781971998e6a355d6a714f6932a30eab295e7ba0d14fd7e0f78c83b87e811860'),
    @('gigaam-v3-e2e-rnnt-joiner-v1','gigaam_v3_e2e_rnnt_joint.onnx',2712896L,'602ff7017a93311aad34df1437c8d7f49911353c13d6eae7a6ee7b041339465c'),
    @('gigaam-v3-e2e-rnnt-tokens-v1','gigaam_v3_e2e_rnnt_tokens.txt',13353L,'7ddf22514c42c531358182c81446a8159771e9921019f09ae743ea622d40221d'),
    @('whisper-large-v3-turbo-q5_0-v1','ggml-large-v3-turbo-q5_0.bin',574041195L,'394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2')
)
foreach($m in $models) { Assert-Hash (Join-Path $ModelsRoot ('Speech/'+$m[0]+'/'+$m[1])) $m[2] $m[3] }
$app=[IO.Path]::GetFullPath($AppStaging).TrimEnd('\')
$appFiles=@(Get-ChildItem -LiteralPath $app -Recurse -File)
if ($appFiles | Where-Object {$_.FullName.Substring($app.Length+1) -match '(^|[\\/])(Data|Logs|Backups|Models|TextModels|setup)([\\/]|$)|\.(wav|aac|mp3|log)$|^(dictation|settings|activation|dictionary)\.json$'}) { throw 'Personal or undeclared state in application staging.' }
foreach($required in @('Egoist.Voice.exe','Egoist.Voice.dll','coreclr.dll','cublas64_13.dll','cublasLt64_13.dll','cudart64_13.dll','assets/EgoistVoice.ico')) { if (!(Test-Path -LiteralPath (Join-Path $app $required))) { throw ('Missing full runtime file: '+$required) } }
if (Test-Path -LiteralPath (Join-Path $app 'egoist-voice.portable')) { throw 'Full must not use the compact profile.' }
New-Item -ItemType Directory -Path $output | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$lines=[Collections.Generic.List[string]]::new()
$payload=[Collections.Generic.List[object]]::new()
function Add-Payload([string]$Source,[string]$Destination,[string]$Flags='ignoreversion') {
    if ($Source.Contains('"') -or $Destination.Contains('"')) { throw 'Unsafe Inno path.' }
    $leaf=[IO.Path]::GetFileName($Destination)
    $dir=$Destination.Substring(0,$Destination.Length-$leaf.Length).TrimEnd('\','/')
    $lines.Add(('Source: "{0}"; DestDir: "{1}"; DestName: "{2}"; Flags: {3}' -f $Source,$dir,$leaf,$Flags))
    $payload.Add(@{path=$Destination;bytes=(Get-Item -LiteralPath $Source).Length;sha256=(Get-FileHash -LiteralPath $Source).Hash.ToLowerInvariant()})
}
foreach($f in $appFiles) { Add-Payload $f.FullName ('{app}\'+$f.FullName.Substring($app.Length+1)) }
foreach($f in Get-ChildItem -LiteralPath (Join-Path $project 'licenses') -File) { Add-Payload $f.FullName ('{app}\licenses\'+$f.Name) }
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md')) { Add-Payload (Join-Path $project $name) ('{app}\'+$name) }
foreach($m in $models) {
    $rel='Speech/'+$m[0]+'/'+$m[1]
    Add-Payload (Join-Path $ModelsRoot $rel) ('{localappdata}\EgoistVoice\Models\'+$rel.Replace('/','\')) 'ignoreversion uninsneveruninstall'
    $marker=Join-Path $output ($m[0]+'.verified.json')
    [IO.File]::WriteAllText($marker,(@{id=$m[0];sizeBytes=$m[2];sha256=$m[3]}|ConvertTo-Json),$utf8)
    Add-Payload $marker ('{localappdata}\EgoistVoice\Models\'+$rel.Replace('/','\')+'.verified.json') 'ignoreversion uninsneveruninstall'
}
Add-Payload $QwenModel '{localappdata}\EgoistVoice\TextModels\Qwen3-4B-Q4_K_M.gguf' 'ignoreversion uninsneveruninstall'
$defaults=Join-Path $output 'dictation-defaults.json'
[IO.File]::WriteAllText($defaults,'{"formatWithQwen":true,"startLocalQwen":true,"textModelEndpoint":"http://127.0.0.1:47823/v1","textModelId":"egoist-qwen3-4b","formatBudgetSeconds":2,"applyDictionary":true,"saveRecentRecordings":false}',$utf8)
Add-Payload $defaults '{localappdata}\EgoistVoice\dictation.json' 'onlyifdoesntexist uninsneveruninstall'
foreach($entry in @($engineManifest.files)+@(@{path='engine-bundle-manifest.json'})) {
    $rel=$entry.path.Replace('/','\')
    if($rel -match '^(host-payload|offline-pack)\\') { Add-Payload (Join-Path $EngineBundleRoot $rel) ('{tmp}\egoist-voice-engine\'+$rel) 'ignoreversion deleteafterinstall' }
    else { Add-Payload (Join-Path $EngineBundleRoot $rel) ('{app}\setup\translation-engine\'+$rel) }
}
$include=Join-Path $output 'full-payload.iss'
[IO.File]::WriteAllLines($include,$lines,[Text.UTF8Encoding]::new($true))
[IO.File]::WriteAllText((Join-Path $output 'full-payload.manifest.json'),(@{schemaVersion=1;files=$payload}|ConvertTo-Json -Depth 5),$utf8)
$compiler=Join-Path $env:USERPROFILE '.nuget\packages\dotnet-innosetup\6.2.1\tools\is\ISCC.exe'
& $compiler '/Qp' ('/DPayloadInclude='+$include) ('/DOutputDir='+$output) (Join-Path $project 'installer\EgoistVoiceFullPreview.iss')
if($LASTEXITCODE -ne 0) { throw 'Full preview compilation failed.' }
$result=Get-ChildItem -LiteralPath $output -File | Where-Object {$_.Extension -in @('.exe','.bin')}
if(@($result | Where-Object {$_.Length -ge 2147483648}).Count) { throw 'GitHub asset exceeds 2 GiB.' }
$receipt=@{schemaVersion=1;preview='v2.2.0-preview.2';applicationSha256=(Get-FileHash -LiteralPath (Join-Path $app 'Egoist.Voice.dll')).Hash.ToLowerInvariant();files=@($result | ForEach-Object {@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})}
[IO.File]::WriteAllText((Join-Path $output 'full-installer.json'),($receipt|ConvertTo-Json -Depth 5),$utf8)
$receipt | ConvertTo-Json -Depth 5
