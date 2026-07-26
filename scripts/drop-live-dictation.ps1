$root = Split-Path -Parent $PSScriptRoot
$targets = @(
    'Services\LiveDictationService.cs',
    'Core\StreamingSegmenter.cs',
    'tests\Egoist.Voice.Tests\LiveDictationTests.cs',
    'tests\Egoist.Voice.Tests\StreamingSegmenterTests.cs'
)
foreach ($relative in $targets) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
        Write-Output "removed: $relative"
    }
}
Write-Output 'done'
