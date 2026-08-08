param()

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$source = Join-Path $projectRoot "installer\EgoistVoiceWebBootstrap.cs"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("EgoistVoiceWebTest-" + [guid]::NewGuid().ToString("N"))
$serverRoot = Join-Path $testRoot "server"
$cacheRoot = Join-Path $testRoot "cache"
$manifestSource = Join-Path $testRoot "TestManifest.cs"
$testInstaller = Join-Path $testRoot "EgoistVoiceWebBootstrap.test.exe"
$requestLog = Join-Path $testRoot "requests.log"

New-Item -ItemType Directory -Path $serverRoot, $cacheRoot -Force | Out-Null
try {
    $launchName = "EgoistVoice-Setup-test-inner.exe"
    $sliceName = "EgoistVoice-Setup-test-inner-1.bin"
    $launchPath = Join-Path $serverRoot $launchName
    $slicePath = Join-Path $serverRoot $sliceName
    [System.IO.File]::WriteAllBytes($launchPath, [System.Text.Encoding]::UTF8.GetBytes("Egoist Voice web bootstrap fixture`n"))
    $sliceBytes = New-Object byte[] (3 * 1024 * 1024 + 137)
    for ($index = 0; $index -lt $sliceBytes.Length; $index++) {
        $sliceBytes[$index] = [byte](($index * 31 + 17) % 251)
    }
    [System.IO.File]::WriteAllBytes($slicePath, $sliceBytes)

    $listenerProbe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listenerProbe.Start()
    $port = ([System.Net.IPEndPoint]$listenerProbe.LocalEndpoint).Port
    $listenerProbe.Stop()
    $baseUrl = "http://127.0.0.1:$port/v2.2.0-test/"
    $launchHash = (Get-FileHash -LiteralPath $launchPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sliceHash = (Get-FileHash -LiteralPath $slicePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = @"
internal static class EgoistVoiceReleaseManifest
{
    internal const string ApplicationVersion = "2.2.0-test";
    internal const string ReleaseTag = "v2.2.0-test";
    internal const string ReleaseBaseUrl = "$baseUrl";
    internal const string LaunchFile = "$launchName";
    internal static readonly PayloadFile[] Files = new PayloadFile[]
    {
        new PayloadFile("$launchName", $((Get-Item -LiteralPath $launchPath).Length)L, "$launchHash"),
        new PayloadFile("$sliceName", $((Get-Item -LiteralPath $slicePath).Length)L, "$sliceHash")
    };
}
"@
    [System.IO.File]::WriteAllText($manifestSource, $manifest, (New-Object System.Text.UTF8Encoding($false)))

    & $compiler `
        /nologo `
        /define:TEST `
        /target:winexe `
        /platform:x64 `
        /optimize+ `
        "/out:$testInstaller" `
        /reference:System.dll `
        /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll `
        $source `
        $manifestSource
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $testInstaller -PathType Leaf)) {
        throw "Test web bootstrap compilation failed with exit code $LASTEXITCODE"
    }

    $serverJob = Start-Job -ArgumentList $serverRoot, $requestLog, $port -ScriptBlock {
        param($Root, $Log, $Port)
        $listener = New-Object System.Net.HttpListener
        $listener.Prefixes.Add("http://127.0.0.1:$Port/")
        $listener.Start()
        try {
            while ($true) {
                $context = $listener.GetContext()
                if ($context.Request.Url.AbsolutePath -eq '/__stop') {
                    $context.Response.StatusCode = 204
                    $context.Response.Close()
                    break
                }
                $relative = [System.Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart('/'))
                $relative = ($relative -split '/', 3)[-1]
                $file = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))
                if (-not $file.StartsWith($Root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $file -PathType Leaf)) {
                    $context.Response.StatusCode = 404
                    $context.Response.Close()
                    continue
                }
                $range = [string]$context.Request.Headers['Range']
                [System.IO.File]::AppendAllText($Log, "$relative`t$range`n")
                $stream = [System.IO.File]::OpenRead($file)
                try {
                    $start = 0L
                    if ($range -match '^bytes=([0-9]+)-$') {
                        $start = [long]$Matches[1]
                        if ($start -ge $stream.Length) {
                            $context.Response.StatusCode = 416
                            $context.Response.Close()
                            continue
                        }
                        $context.Response.StatusCode = 206
                        $context.Response.AddHeader('Content-Range', "bytes $start-$($stream.Length - 1)/$($stream.Length)")
                    }
                    else {
                        $context.Response.StatusCode = 200
                    }
                    $stream.Position = $start
                    $context.Response.ContentLength64 = $stream.Length - $start
                    $buffer = New-Object byte[] 65536
                    while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $context.Response.OutputStream.Write($buffer, 0, $read)
                    }
                    $context.Response.OutputStream.Close()
                }
                finally {
                    $stream.Dispose()
                    $context.Response.Close()
                }
            }
        }
        finally {
            $listener.Stop()
            $listener.Close()
        }
    }

    try {
        Start-Sleep -Milliseconds 500
        $first = Start-Process `
            -FilePath $testInstaller `
            -ArgumentList @("--download-only", "--payload-dir", $cacheRoot) `
            -Wait `
            -PassThru `
            -WindowStyle Hidden
        if ($first.ExitCode -ne 0) { throw "Initial download-only test failed with exit code $($first.ExitCode)" }

        $cachedSlice = Join-Path $cacheRoot $sliceName
        $partSlice = "$cachedSlice.part"
        $half = [int]([Math]::Floor((Get-Item -LiteralPath $slicePath).Length / 2))
        $sourceStream = [System.IO.File]::OpenRead($slicePath)
        $partStream = [System.IO.File]::Create($partSlice)
        try {
            $buffer = New-Object byte[] 65536
            $remaining = $half
            while ($remaining -gt 0) {
                $read = $sourceStream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))
                if ($read -le 0) { throw "Fixture ended early." }
                $partStream.Write($buffer, 0, $read)
                $remaining -= $read
            }
        }
        finally {
            $sourceStream.Dispose()
            $partStream.Dispose()
        }
        Remove-Item -LiteralPath $cachedSlice -Force

        $second = Start-Process `
            -FilePath $testInstaller `
            -ArgumentList @("--download-only", "--payload-dir", $cacheRoot) `
            -Wait `
            -PassThru `
            -WindowStyle Hidden
        if ($second.ExitCode -ne 0) { throw "Resume download-only test failed with exit code $($second.ExitCode)" }
        if ((Get-FileHash -LiteralPath $cachedSlice -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sliceHash) {
            throw "Resumed payload hash does not match the fixture."
        }
        $expectedRange = "bytes=$half-"
        if (-not (Select-String -LiteralPath $requestLog -SimpleMatch $expectedRange -Quiet)) {
            throw "Resume test did not observe the expected Range request: $expectedRange"
        }
        Write-Output "web bootstrap download/resume/hash fixture passed"
    }
    finally {
        if ($serverJob) {
            try {
                Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/__stop" -TimeoutSec 5 | Out-Null
                Wait-Job -Job $serverJob -Timeout 5 | Out-Null
            }
            catch { }
            Stop-Job -Job $serverJob -ErrorAction SilentlyContinue
            Remove-Job -Job $serverJob -Force -ErrorAction SilentlyContinue
        }
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
