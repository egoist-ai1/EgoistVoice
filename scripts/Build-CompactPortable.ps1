[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$InstalledModelsRoot = (Join-Path $env:LOCALAPPDATA 'EgoistVoice\Models'),
    [switch]$UseExistingPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $artifactRoot ('portable-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')) }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (!$destination.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable staging must be inside this project artifacts directory.'
}
if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Reparse staging directory refused.'
}
if (!$UseExistingPublish) {
    if (Test-Path -LiteralPath $destination) { throw 'Choose a new staging directory; existing files are preserved.' }
    & dotnet publish (Join-Path $projectRoot 'Egoist.Voice.csproj') -c Release -r win-x64 --self-contained true '-p:VoiceFlavor=Compact' -o $destination --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
}
$executable = Join-Path $destination 'Egoist.Voice.exe'
if (!(Test-Path -LiteralPath $executable) -or !(Test-Path -LiteralPath (Join-Path $destination 'coreclr.dll'))) {
    throw 'Expected self-contained publish is missing.'
}
if (Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object { $_.Name -match '^(cublas|cudart|ggml-cuda|whisper\.dll)' }) {
    throw 'Non-compact runtime detected.'
}
$manifestPath = Join-Path $destination 'compact-models.json'
$priorDataRoot = $env:EGOIST_VOICE_DATA_ROOT
try {
    $env:EGOIST_VOICE_DATA_ROOT = Join-Path (Split-Path -Parent $destination) 'build-diagnostics'
    $process = Start-Process -FilePath $executable -ArgumentList @('--export-compact-models', ('"' + $manifestPath + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'Model manifest export timed out.' }
    if ($process.ExitCode -ne 0) { throw 'Model manifest export failed.' }
} finally { $env:EGOIST_VOICE_DATA_ROOT = $priorDataRoot }
$models = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (@($models).Count -ne 4 -or ($models | Measure-Object SizeBytes -Sum).Sum -ne 326322304) {
    throw 'Unexpected model catalog; review the compact size contract.'
}
foreach ($model in $models) {
    if ($model.Id -notmatch '^gigaam-[a-z0-9-]+$' -or $model.FileName -notmatch '^[a-zA-Z0-9_.-]+$') { throw 'Invalid catalog path.' }
    $relative = Join-Path 'Speech' (Join-Path $model.Id $model.FileName)
    $source = Join-Path $InstalledModelsRoot $relative
    if (!(Test-Path -LiteralPath $source)) { throw "Installed model missing: $($model.Id). No downloads will be attempted." }
    if ((Get-Item -LiteralPath $source).Length -ne $model.SizeBytes -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $model.Sha256) { throw "Installed model hash mismatch: $($model.Id)" }
    $target = Join-Path (Join-Path $destination 'Models') $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $model.Sha256) { throw 'Copied model hash mismatch.' }
    @{ id=$model.Id; sizeBytes=$model.SizeBytes; sha256=$model.Sha256 } | ConvertTo-Json | Set-Content -LiteralPath ($target + '.verified.json') -Encoding utf8
}
[IO.File]::WriteAllText((Join-Path $destination 'egoist-voice.portable'), 'gigaam-russian-cpu-v1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $destination
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination
[IO.File]::WriteAllText((Join-Path $destination 'START-HERE.txt'), @'
Egoist Voice Portable — Windows 10 (1903 и новее) / Windows 11, x64

Запустите Egoist.Voice.exe из установленной или перенесённой целиком папки.
.NET и отдельная видеокарта не нужны. Модели уже включены; сеть не требуется.
Удерживайте настроенную кнопку мыши или выберите сочетание клавиш в меню трея.
Настройки и журнал пишутся в Data рядом с приложением. История 3 последних записей включена по умолчанию для надёжности.
Переносите всю папку. Перед переносом закройте приложение.

Состав: GigaAM v3 INT8, русский язык, CPU. Whisper и переводчик не входят.
Qwen-оформление требует отдельно установленной текстовой модели и локального сервера.
«Текст» позволяет распознать аудиофайл и скопировать результат.
«История» позволяет слушать, удалять и повторно распознавать три последние записи.

Закройте установленный Egoist Voice перед запуском Portable: они используют один hotkey.
Это неподписанная локальная сборка. На другом устройстве проверьте выбранный микрофон и клавишу активации.
'@)
$files = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject][ordered]@{ path=$_.FullName.Substring($destination.Length + 1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$total = ($files | Measure-Object bytes -Sum).Sum
if ($total -gt 600000000) { throw "Compact folder exceeds 600 MB: $total bytes" }
$gitCmd = Get-Command git.exe -ErrorAction SilentlyContinue
$gitExe = if ($gitCmd) { $gitCmd.Source } else { $null }
if (!$gitExe -and (Test-Path 'C:\Users\Egoist\AppData\Local\GitHubDesktop\app-3.6.5\resources\app\git\cmd\git.exe')) {
    $gitExe = 'C:\Users\Egoist\AppData\Local\GitHubDesktop\app-3.6.5\resources\app\git\cmd\git.exe'
}
$sourceRev = "release-compact-2.2.0"
$sourceDirty = $false
if ($gitExe) {
    try {
        $rev = (& $gitExe -C $projectRoot rev-parse HEAD 2>$null)
        if ($rev) { $sourceRev = $rev.Trim() }
        $dirty = @(& $gitExe -C $projectRoot status --porcelain 2>$null)
        $sourceDirty = [bool]($dirty.Count)
    } catch { }
}
$receipt = [ordered]@{ schemaVersion=1; generatedAt=[DateTime]::UtcNow.ToString('o'); flavor='Russian CPU Portable'; unpackedBytes=$total; fileCount=$files.Count; sourceRevision=$sourceRev; sourceDirty=$sourceDirty; files=$files }
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $destination) 'portable-stage.manifest.json') -Encoding utf8
[pscustomobject]@{ Staging=$destination; Files=$files.Count; Bytes=$total; MB=[math]::Round($total / 1000000, 2) } | ConvertTo-Json
