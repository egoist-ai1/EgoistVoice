# Проверка Фазы 0: приложение стартует под PerMonitorV2-манифестом, рендерит все состояния
# капсулы и превью трея. Это визуальная регрессия для правок позиционирования и DPI.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v2-phase0-check.ps1
$ErrorActionPreference = 'Continue'

# WPF строит путь к шрифтовому кэшу из %WINDIR%. В некоторых автоматизированных оболочках эта
# переменная не унаследована, и тогда конструктор любого Window падает с UriFormatException ещё
# до разбора XAML. К приложению это отношения не имеет, но диагностику ломает наглухо.
if ([string]::IsNullOrEmpty($env:WINDIR)) {
    $env:WINDIR = [Environment]::GetFolderPath('Windows')
    if ([string]::IsNullOrEmpty($env:WINDIR)) { $env:WINDIR = 'C:\Windows' }
    Write-Output "WINDIR не был задан, подставлен: $env:WINDIR"
}

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net8.0-windows\Egoist.Voice.exe'
$out = Join-Path $root 'artifacts\v2-phase0'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Output "MISSING: $exe"
    exit 1
}

New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-ChildItem -LiteralPath $out -File -ErrorAction SilentlyContinue | Remove-Item -Force

# --render-state-preview лежит за mutex единственного экземпляра в App.OnStartup, поэтому при
# запущенном Egoist Voice процесс молча выходит с кодом 0 и ничего не рисует. Диагностируем это
# явно, а не выдаём за провал рендера.
$running = @(Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Output ("SKIPPED: запущен Egoist Voice (PID {0}). Состояния капсулы рисуются только при закрытом приложении." -f ($running.Id -join ', '))
    Write-Output 'Закройте приложение из трея и запустите скрипт повторно.'
    exit 2
}

$states = @('ready', 'listening', 'processing', 'success', 'clipboard', 'error')
foreach ($state in $states) {
    $target = Join-Path $out "$state.png"
    $process = Start-Process -FilePath $exe -ArgumentList '--render-state-preview', $state, $target -PassThru -Wait
    Write-Output ("state {0}: exit={1}" -f $state, $process.ExitCode)
}

$trayTarget = Join-Path $out 'tray.png'
$tray = Start-Process -FilePath $exe -ArgumentList '--render-tray-preview', $trayTarget -PassThru -Wait
Write-Output ("tray: exit={0}" -f $tray.ExitCode)

$rendered = Get-ChildItem -LiteralPath $out -File
$rendered | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Output

$missing = @($states | Where-Object { -not (Test-Path -LiteralPath (Join-Path $out "$_.png")) })
if ($missing.Count -gt 0) {
    Write-Output ("FAILED: не отрисованы состояния — {0}" -f ($missing -join ', '))
    exit 1
}

Write-Output ("OK: отрисовано файлов — {0}" -f $rendered.Count)
