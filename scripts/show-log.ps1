$log = Join-Path $env:LOCALAPPDATA 'EgoistVoice\Logs\app.log'
if (-not (Test-Path -LiteralPath $log)) { Write-Output "no log at $log"; exit 1 }
Get-Content -LiteralPath $log -Tail 40 -Encoding UTF8
