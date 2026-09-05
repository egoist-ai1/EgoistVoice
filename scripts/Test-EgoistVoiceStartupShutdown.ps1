[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Executable = "",
    [string]$OutputPath = "",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$defaultExecutable = Join-Path $projectRoot "bin\Release\net8.0-windows\Egoist.Voice.exe"
$defaultOutput = Join-Path $projectRoot "artifacts\verification\ev2220-current\startup-shutdown.json"
$app = [IO.Path]::GetFullPath($(if ([string]::IsNullOrWhiteSpace($Executable)) { $defaultExecutable } else { $Executable }))
$output = [IO.Path]::GetFullPath($(if ([string]::IsNullOrWhiteSpace($OutputPath)) { $defaultOutput } else { $OutputPath }))
$verificationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\verification"))

if (-not $app.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw "Startup/shutdown executable must be an existing project artifact."
}
if (-not $output.StartsWith($verificationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Startup/shutdown evidence must remain under artifacts\verification."
}
if (Get-Process -Name "Egoist.Voice" -ErrorAction SilentlyContinue) {
    throw "Egoist Voice is already running; refusing to alter an existing user session."
}
if (-not $Apply) {
    [pscustomobject]@{ executable = $app; output = $output; action = "background-no-capture startup and graceful shutdown" } |
        ConvertTo-Json -Compress
    return
}

$main = $null
$shutdownSent = $false
try {
    if (-not $PSCmdlet.ShouldProcess($app, "run background-no-capture startup and graceful shutdown oracle")) {
        return
    }
    $started = [DateTimeOffset]::UtcNow
    $main = Start-Process -FilePath $app -ArgumentList "--background" -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 2000
    if ($main.HasExited) {
        throw "Background app exited before the startup observation."
    }

    $shutdown = Start-Process -FilePath $app -ArgumentList "--shutdown" -WindowStyle Hidden -Wait -PassThru
    $shutdownSent = $true
    if (-not $main.WaitForExit(15000)) {
        throw "Background app did not exit after the shutdown signal."
    }

    $result = [ordered]@{
        schema = "egoist.voice.startup-shutdown/v1"
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("o")
        mode = "background-no-capture"
        startupObservedRunning = $true
        shutdownSignalExitCode = [int]$shutdown.ExitCode
        mainExitCode = [int]$main.ExitCode
        elapsedMs = [math]::Round(([DateTimeOffset]::UtcNow - $started).TotalMilliseconds, 3)
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
    [IO.File]::WriteAllText($output, (($result | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
}
finally {
    if ($null -ne $main -and -not $main.HasExited -and -not $shutdownSent) {
        Start-Process -FilePath $app -ArgumentList "--shutdown" -WindowStyle Hidden -Wait | Out-Null
        [void]$main.WaitForExit(15000)
    }
    if ($null -ne $main) {
        $main.Dispose()
    }
}
