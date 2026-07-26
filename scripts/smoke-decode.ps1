# Проверяет, что движок реально запускается и декодирует после смены метода декодирования.
# Тесты этого не покрывают: они не грузят нативные модели.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-decode.ps1
$ErrorActionPreference = 'Continue'
if ([string]::IsNullOrEmpty($env:WINDIR)) { $env:WINDIR = 'C:\Windows' }

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net8.0-windows\Egoist.Voice.exe'
$out = Join-Path $root 'artifacts\v2-phase0'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$running = @(Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Output 'SKIPPED: закройте Egoist Voice и повторите'
    exit 2
}

# Микрофонный смоук поднимает захват и сразу останавливает — проверяет, что устройство доступно.
$mic = Join-Path $out 'microphone-smoke.txt'
$p = Start-Process -FilePath $exe -ArgumentList '--microphone-smoke', $mic -PassThru -Wait
Write-Output ("microphone-smoke exit={0}" -f $p.ExitCode)
if (Test-Path -LiteralPath $mic) { Get-Content -LiteralPath $mic | Write-Output }

# Пайплайн-смоук прогоняет реальный WAV через оба движка целиком.
$sample = Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -Recurse -Filter '*.wav' -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending | Select-Object -First 1
if ($null -eq $sample) {
    Write-Output 'нет тестового WAV в artifacts — пайплайн не проверен'
    exit 0
}

$pipeline = Join-Path $out 'pipeline-smoke.txt'
Write-Output ("sample: {0} ({1:N0} bytes)" -f $sample.Name, $sample.Length)
$p = Start-Process -FilePath $exe -ArgumentList '--pipeline-smoke', $sample.FullName, $pipeline -PassThru -Wait
Write-Output ("pipeline-smoke exit={0}" -f $p.ExitCode)
if (Test-Path -LiteralPath $pipeline) { Get-Content -LiteralPath $pipeline | Select-Object -First 20 | Write-Output }
