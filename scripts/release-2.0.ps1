# Финальная сборка релиза. Один вход: остановить приложение, собрать, прогнать тесты,
# отрисовать состояния, собрать установщик.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\release-2.0.ps1

$ErrorActionPreference = 'Continue'

# WPF строит путь к шрифтовому кэшу из %WINDIR%; в автоматизированных оболочках переменная
# иногда не унаследована, и тогда любое окно падает ещё до разбора XAML.
if ([string]::IsNullOrEmpty($env:WINDIR)) { $env:WINDIR = 'C:\Windows' }

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Output '=== 1. остановка запущенного экземпляра ==='
& (Join-Path $PSScriptRoot 'stop-app.ps1')

Write-Output '=== 2. сборка ==='
dotnet build .\Egoist.Voice.sln -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Output 'BUILD FAILED'; exit 1 }

Write-Output '=== 3. тесты ==='
dotnet test .\Egoist.Voice.sln -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Output 'TESTS FAILED'; exit 1 }

Write-Output '=== 4. визуальная регрессия ==='
& (Join-Path $PSScriptRoot 'v2-phase0-check.ps1')

Write-Output '=== 5. установщик ==='
& (Join-Path $PSScriptRoot 'build-installer.ps1')
$installerExit = $LASTEXITCODE

$release = Join-Path $root 'artifacts\release'
if (Test-Path $release) {
    Get-ChildItem $release -Filter '*.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 3 Name, @{n='MB';e={[math]::Round($_.Length / 1MB, 1)}}, LastWriteTime |
        Format-Table -AutoSize | Out-String | Write-Output
}

Write-Output ("installer exit={0}" -f $installerExit)
