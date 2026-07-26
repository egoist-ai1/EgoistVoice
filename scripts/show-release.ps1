$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'artifacts\release'
if (-not (Test-Path -LiteralPath $release)) { Write-Output 'no release dir'; exit 1 }

Get-ChildItem -LiteralPath $release -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 5 Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime |
    Format-Table -AutoSize | Out-String | Write-Output

Write-Output '=== staging ==='
$staging = Join-Path $release 'installer-staging'
if (Test-Path -LiteralPath $staging) {
    $files = Get-ChildItem -LiteralPath $staging -Recurse -File
    Write-Output ("files={0} totalMB={1}" -f $files.Count, [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1))
    $exe = Join-Path $staging 'Egoist.Voice.exe'
    if (Test-Path -LiteralPath $exe) {
        Write-Output ("exe version={0}" -f (Get-Item -LiteralPath $exe).VersionInfo.FileVersion)
    }
} else {
    Write-Output 'no staging'
}
