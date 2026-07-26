$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\release\installer-staging'
if (-not (Test-Path -LiteralPath $staging)) { Write-Output 'no staging'; exit 1 }

Write-Output '=== крупнейшие файлы staging ==='
Get-ChildItem -LiteralPath $staging -Recurse -File |
    Sort-Object Length -Descending |
    Select-Object -First 12 @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, Name |
    Format-Table -AutoSize | Out-String | Write-Output

Write-Output '=== обязательные компоненты ==='
# Модели складываются в отдельный model-staging, не в installer-staging: установщик собирает их
# из двух источников.
$modelStaging = Join-Path $root 'artifacts\release\model-staging'
$searchRoots = @($staging)
if (Test-Path -LiteralPath $modelStaging) { $searchRoots += $modelStaging }

$required = @(
    'Egoist.Voice.exe',
    'gigaam_v3_e2e_rnnt_encoder_int8.onnx',
    'gigaam_v3_e2e_rnnt_decoder.onnx',
    'gigaam_v3_e2e_rnnt_joint.onnx',
    'gigaam_v3_e2e_rnnt_tokens.txt',
    'ggml-large-v3-turbo-q5_0.bin',
    'cublas64_13.dll',
    'cublasLt64_13.dll',
    'cudart64_13.dll'
)
$missing = 0
foreach ($name in $required) {
    $found = $null
    foreach ($searchRoot in $searchRoots) {
        $found = Get-ChildItem -LiteralPath $searchRoot -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { break }
    }
    if ($found) {
        Write-Output ("  OK   {0,-40} {1,8:N1} MB" -f $name, ($found.Length / 1MB))
    } else {
        Write-Output ("  MISS {0}" -f $name)
        $missing++
    }
}

$modelBytes = 0
if (Test-Path -LiteralPath $modelStaging) {
    $modelBytes = (Get-ChildItem -LiteralPath $modelStaging -Recurse -File | Measure-Object -Property Length -Sum).Sum
}
Write-Output ("model-staging: {0:N1} MB" -f ($modelBytes / 1MB))

if ($missing -gt 0) {
    Write-Output ("INCOMPLETE: missing components = {0}" -f $missing)
    exit 1
}
Write-Output 'COMPLETE'
