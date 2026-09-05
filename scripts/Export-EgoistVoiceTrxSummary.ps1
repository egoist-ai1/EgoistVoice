[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$verificationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\verification"))
$input = [IO.Path]::GetFullPath($InputPath)
$output = [IO.Path]::GetFullPath($OutputPath)

foreach ($path in @($input, $output)) {
    if (-not $path.StartsWith(
        $verificationRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "TRX evidence path must remain under artifacts\verification."
    }
}
if (-not (Test-Path -LiteralPath $input -PathType Leaf)) {
    throw "TRX input is missing."
}

[xml]$trx = Get-Content -LiteralPath $input -Raw -Encoding UTF8
$counters = $trx.TestRun.ResultSummary.Counters
$start = [DateTimeOffset]::Parse([string]$trx.TestRun.Times.start)
$finish = [DateTimeOffset]::Parse([string]$trx.TestRun.Times.finish)
$summary = [ordered]@{
    schema = "egoist.voice.test-summary/v1"
    generatedUtc = $finish.ToUniversalTime().ToString("o")
    configuration = "Release"
    targetFramework = "net8.0-windows"
    outcome = [string]$trx.TestRun.ResultSummary.outcome
    total = [int]$counters.total
    executed = [int]$counters.executed
    passed = [int]$counters.passed
    failed = [int]$counters.failed
    skipped = [int]$counters.notExecuted
    durationMs = [math]::Round(($finish - $start).TotalMilliseconds, 3)
    sourceRunnerArtifact = "excluded-private-test-runner-metadata"
}
$json = ($summary | ConvertTo-Json) + "`n"

if (-not $Apply) {
    $json
    return
}
if ($PSCmdlet.ShouldProcess($output, "write privacy-safe TRX aggregate")) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
    [IO.File]::WriteAllText($output, $json, [Text.UTF8Encoding]::new($false))
}
