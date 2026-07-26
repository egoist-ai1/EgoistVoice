# Запускает сборку установщика отдельным процессом с логом, чтобы её нельзя было оборвать
# таймаутом вызывающей стороны: компиляция Inno на ~1.7 ГБ входных данных идёт минутами.
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$out = Join-Path $logDir 'build-installer.out.log'
$err = Join-Path $logDir 'build-installer.err.log'
Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue

$script = Join-Path $PSScriptRoot 'build-installer.ps1'
$process = Start-Process -FilePath 'powershell.exe' `
    -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) `
    -RedirectStandardOutput $out `
    -RedirectStandardError $err `
    -WindowStyle Hidden `
    -PassThru

Set-Content -LiteralPath (Join-Path $logDir 'build-installer.pid') -Value $process.Id
Write-Output ("started pid={0}" -f $process.Id)
