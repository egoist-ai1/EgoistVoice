$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net8.0-windows\Egoist.Voice.exe'

$before = @(Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue)
if ($before.Count -eq 0) {
    Write-Output 'already stopped'
    exit 0
}

$p = Start-Process -FilePath $exe -ArgumentList '--shutdown' -PassThru -Wait
Write-Output ("shutdown exit={0}" -f $p.ExitCode)
Start-Sleep -Seconds 2

$after = @(Get-Process -Name 'Egoist.Voice' -ErrorAction SilentlyContinue)
if ($after.Count -eq 0) {
    Write-Output 'stopped'
    exit 0
}

Write-Output ("still running: {0}" -f ($after.Id -join ','))
exit 1
