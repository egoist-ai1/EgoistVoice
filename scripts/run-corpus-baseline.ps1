[CmdletBinding()]
param(
  [string]$CorpusPath = (Join-Path $PSScriptRoot "..\tests\corpus"),
  [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\bench\baseline.json"),
  [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$')]
  [string]$Label = "voice-2.1.1-dirty-baseline",
  [ValidateSet("baseline", "hotwords")]
  [string]$DecoderMode = "baseline",
  [switch]$Record,
  [switch]$NoBuild,
  [switch]$Force
)

$ErrorActionPreference = "Stop"

if ($DecoderMode -eq "hotwords") {
  if (-not $PSBoundParameters.ContainsKey("OutputPath")) {
    $OutputPath = Join-Path $PSScriptRoot "..\artifacts\bench\hotwords.json"
  }
  if (-not $PSBoundParameters.ContainsKey("Label")) {
    $Label = "voice-2.2-hotwords-candidate"
  }
}

$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$CorpusRoot = [IO.Path]::GetFullPath($CorpusPath)
$ReportPath = [IO.Path]::GetFullPath($OutputPath)
$CandidatePath = $ReportPath + ".candidate"
$Executable = Join-Path $ProjectRoot "bin\Release\net8.0-windows\Egoist.Voice.exe"

if (-not (Test-Path -LiteralPath (Join-Path $CorpusRoot "script.jsonl") -PathType Leaf)) {
  throw "Corpus script is missing. Use the recorder workflow from tests/corpus/README.md."
}
if ((Test-Path -LiteralPath $ReportPath) -and -not $Force) {
  throw "Baseline already exists. Pass -Force only when intentionally replacing the frozen baseline."
}

if (-not $NoBuild) {
  & dotnet build (Join-Path $ProjectRoot "Egoist.Voice.sln") -c Release --no-restore --nologo
  if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
  }
}
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
  throw "Release executable is missing. Run without -NoBuild first."
}

if ($Record) {
  Write-Host "Recorder is private and local: WAV/reference.jsonl remain ignored by Git and are never logged."
  & $Executable --corpus-record $CorpusRoot
  if ($LASTEXITCODE -ne 0) {
    throw "Corpus recorder failed with exit code $LASTEXITCODE."
  }
}

if (-not (Test-Path -LiteralPath (Join-Path $CorpusRoot "reference.jsonl") -PathType Leaf)) {
  throw "Private corpus is incomplete: reference.jsonl is missing. Run this command with -Record."
}

$ReportDirectory = Split-Path -Parent $ReportPath
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
if (Test-Path -LiteralPath $CandidatePath) {
  Remove-Item -LiteralPath $CandidatePath -Force
}

& $Executable --corpus-benchmark $CorpusRoot $CandidatePath $Label $DecoderMode
$BenchmarkExitCode = $LASTEXITCODE
if ($BenchmarkExitCode -ne 0) {
  throw "Offline corpus benchmark failed with exit code $BenchmarkExitCode. The aggregate-only candidate report contains the stable error code."
}
if (-not (Test-Path -LiteralPath $CandidatePath -PathType Leaf)) {
  throw "Corpus benchmark exited successfully without a report."
}

Move-Item -LiteralPath $CandidatePath -Destination $ReportPath -Force
$Report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
Write-Host "Corpus report frozen: mode=$DecoderMode label=$($Report.label) clips=$($Report.corpus.clips) WER=$([math]::Round($Report.wer * 100, 2))% p95=$([math]::Round($Report.p95Ms))ms corpus=$($Report.corpus.sha256.Substring(0, 12))"
Write-Host "Privacy=$($Report.privacy); report contains metrics and stable IDs, never audio/reference/hypothesis text."
