[CmdletBinding()]
param(
  [string]$OutputPath,
  [switch]$NoDownload,
  [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
  Join-Path $ProjectRoot "artifacts\corpora\public-proxy-v1"
} else {
  $OutputPath
}
$ToolRoot = Join-Path $ProjectRoot "artifacts\tools\hf-datasets"
$CacheRoot = Join-Path $ProjectRoot "artifacts\hf-cache"
$Descriptor = Join-Path $ProjectRoot "tests\corpus\public-proxy-v1.sources.json"
$Importer = Join-Path $PSScriptRoot "acquire-public-proxy-corpus.py"

if (-not (Test-Path -LiteralPath (Join-Path $ToolRoot "datasets") -PathType Container)) {
  throw "Task-local datasets runtime is missing at artifacts/tools/hf-datasets. Install datasets==5.0.0 there; do not add it to the app."
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
  throw "Python 3.10+ is required for the research-only corpus importer."
}
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
  throw "ffmpeg is required to normalize public audio to PCM 16 kHz mono."
}

$env:PYTHONPATH = $ToolRoot
$env:HF_HOME = $CacheRoot
$env:HF_HUB_DISABLE_XET = "1"

$Arguments = @(
  $Importer,
  "--descriptor", $Descriptor,
  "--output", ([IO.Path]::GetFullPath($OutputPath)),
  "--source-root", (Join-Path $ProjectRoot "artifacts\sources\public-proxy-v1")
)
if ($NoDownload) { $Arguments += "--no-download" }
if ($Force) { $Arguments += "--force" }

& python @Arguments
if ($LASTEXITCODE -ne 0) {
  throw "Public proxy acquisition failed with exit code $LASTEXITCODE. A repeated run resumes verified files."
}
