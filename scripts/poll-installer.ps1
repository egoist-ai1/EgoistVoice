param([int]$WaitSeconds = 0)

$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'artifacts\release'
$pidFile = Join-Path $logDir 'build-installer.pid'

if ($WaitSeconds -gt 0) { Start-Sleep -Seconds $WaitSeconds }

$alive = $false
if (Test-Path -LiteralPath $pidFile) {
    $buildPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
    $alive = $null -ne (Get-Process -Id $buildPid -ErrorAction SilentlyContinue)
    Write-Output ("build pid={0} alive={1}" -f $buildPid, $alive)
}

$installer = Join-Path $logDir 'EgoistVoice-Setup-2.0.0-win-x64.exe'
if (Test-Path -LiteralPath $installer) {
    $item = Get-Item -LiteralPath $installer
    Write-Output ("installer: {0:N1} MB  {1}" -f ($item.Length / 1MB), $item.LastWriteTime)
} else {
    Write-Output 'installer: not yet'
}

foreach ($name in @('build-installer.out.log', 'build-installer.err.log')) {
    $path = Join-Path $logDir $name
    if (Test-Path -LiteralPath $path) {
        $content = Get-Content -LiteralPath $path -Tail 8
        if ($content) {
            Write-Output "--- $name ---"
            $content | Write-Output
        }
    }
}
