using System.Threading;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\Egoist.Voice.SingleInstance";
    private const string ShutdownEventName = "Local\\Egoist.Voice.Shutdown";
    private Mutex? _singleInstance;
    private EventWaitHandle? _shutdownEvent;
    private RegisteredWaitHandle? _shutdownRegistration;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Core has no reference to the service layer, so the logging hook is wired here rather
        // than taken as a dependency.
        Core.UserDictionary.AppLogWrite = message => AppLog.Write(message);
        AppLog.Write($"Startup args=[{string.Join(", ", e.Args)}]");
        DispatcherUnhandledException += (_, args) =>
            AppLog.Write("Dispatcher unhandled exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write("AppDomain unhandled exception", args.ExceptionObject as Exception);

        if (e.Args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
        {
            SignalRunningInstanceToShutdown();
            if (!WaitForRunningInstanceToExit(TimeSpan.FromSeconds(20)))
            {
                Environment.ExitCode = 2;
            }
            Shutdown();
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--transcribe-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunTranscriptionSmokeAsync(e.Args[1], e.Args[2]);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunBenchmarkAsync(e.Args[1], e.Args[2]);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--corpus-benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunCorpusBenchmarkAsync(e.Args[1], e.Args[2], e.Args.Length >= 4 ? e.Args[3] : "hybrid");
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--stress-benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var iterations = e.Args.Length >= 4 && int.TryParse(e.Args[3], out var parsed)
                ? Math.Clamp(parsed, 2, 200)
                : 30;
            _ = RunStressBenchmarkAsync(e.Args[1], e.Args[2], iterations);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--giga-benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunGigaBenchmarkAsync(e.Args[1], e.Args[2]);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--whisper-benchmark", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunWhisperBenchmarkAsync(e.Args[1], e.Args[2]);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--pipeline-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunPipelineSmokeAsync(e.Args[1], e.Args[2]);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--microphone-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunMicrophoneSmokeAsync(e.Args[1]);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--model-source-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunModelSourceSmokeAsync(e.Args[1]);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--render-tray-preview", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                EgoistTrayVisualPreview.Render(e.Args[1]);
            }
            catch (Exception exception)
            {
                AppLog.Write("Tray preview failed", exception);
                Environment.ExitCode = 1;
            }
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--render-shortcut-preview", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var dialog = new CustomShortcutDialog(new KeyboardShortcut(
                HotkeyModifiers.Control | HotkeyModifiers.Shift,
                0x56));
            dialog.Show();
            Dispatcher.InvokeAsync(() =>
            {
                dialog.RenderPreview(e.Args[1]);
                dialog.Close();
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        _singleInstance = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName);
        _shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, _) => Dispatcher.BeginInvoke(Shutdown),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        var requiredModels = ModelCatalog.CreateRequiredModels();
        var modelManager = new ModelManager(requiredModels);
        var delivery = new DictationDeliveryService(
            new ClipboardService(),
            new TextInsertionService());
        var window = new MainWindow(
            new AudioCaptureService(),
            new HybridTranscriptionService(modelManager),
            delivery,
            modelManager);

        MainWindow = window;
        if (e.Args.Length >= 3 && e.Args[0].Equals("--render-state-preview", StringComparison.OrdinalIgnoreCase))
        {
            window.Show();
            window.ShowStatePreview(e.Args[1]);
            _ = RenderStatePreviewAsync(window, e.Args[2]);
            return;
        }

        if (e.Args.Length >= 2 &&
            (e.Args[0].Equals("--render-preview", StringComparison.OrdinalIgnoreCase) ||
             e.Args[0].Equals("--background-render-preview", StringComparison.OrdinalIgnoreCase)))
        {
            if (e.Args[0].Equals("--render-preview", StringComparison.OrdinalIgnoreCase))
            {
                window.Show();
            }
            window.ShowListeningPreview();
            Dispatcher.InvokeAsync(() =>
            {
                window.RenderPreview(e.Args[1]);
                window.Close();
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        _tray = new TrayService(window, modelManager, Shutdown);
        window.InitializeHotkey();
        var background = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (!background)
        {
            window.ShowReadyBriefly();
        }
        window.BeginWarmUp(showProgress: !background, announceModelDownloads: !modelManager.AreAllModelsReady);
        AppLog.Write($"Startup complete, background={background}");
    }

    private static void SignalRunningInstanceToShutdown()
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(ShutdownEventName);
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No running instance.
        }
    }

    private async Task RenderStatePreviewAsync(MainWindow window, string outputPath)
    {
        await Task.Delay(320);
        window.RenderPreview(outputPath);
        window.Close();
        Shutdown();
    }

    private static bool WaitForRunningInstanceToExit(TimeSpan timeout)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(MutexName);
            try
            {
                if (!mutex.WaitOne(timeout))
                {
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // The former process terminated without releasing the mutex.
            }

            mutex.ReleaseMutex();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    private async Task RunTranscriptionSmokeAsync(string audioPath, string outputPath)
    {
        try
        {
            using var service = CreateTranscriptionService();
            var result = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            await File.WriteAllTextAsync(outputPath, result.Text);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunModelSourceSmokeAsync(string outputPath)
    {
        var lines = new List<string>();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            foreach (var descriptor in ModelCatalog.CreateRequiredModels())
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.DownloadUri);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken.None);
                response.EnsureSuccessStatusCode();

                var advertisedSize = response.Content.Headers.ContentRange?.Length ??
                                     response.Content.Headers.ContentLength;
                if (advertisedSize is not null && advertisedSize != descriptor.SizeBytes)
                {
                    throw new InvalidDataException(
                        $"{descriptor.Id}: source size {advertisedSize} does not match {descriptor.SizeBytes}.");
                }

                lines.Add($"{descriptor.Id}|{(int)response.StatusCode}|{advertisedSize ?? descriptor.SizeBytes}");
            }

            lines.Insert(0, "PASS");
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            lines.Clear();
            lines.Add("ERROR");
            lines.Add(exception.ToString());
        }
        finally
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllLinesAsync(outputPath, lines);
            Shutdown();
        }
    }

    private async Task RunMicrophoneSmokeAsync(string outputPath)
    {
        try
        {
            using var capture = new AudioCaptureService();
            capture.Start();
            await Task.Delay(450);
            var captureResult = await capture.StopAsync(CancellationToken.None);
            var bytes = new FileInfo(captureResult.Path).Length;
            if (bytes <= 44)
            {
                throw new InvalidDataException("Microphone capture produced an empty WAV file.");
            }

            await File.WriteAllTextAsync(outputPath,
                $"PASS{Environment.NewLine}" +
                $"bytes={bytes}{Environment.NewLine}" +
                $"hasSpeech={captureResult.HasSpeech}{Environment.NewLine}" +
                $"speechMs={captureResult.DetectedSpeech.TotalMilliseconds:0}{Environment.NewLine}" +
                $"peakDb={captureResult.PeakDecibels:0.0}");
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>
    /// Transcribes an entire corpus and writes WER/CER plus latency percentiles. Runs the whole
    /// corpus even when individual clips fail: a report that stops at the first bad file cannot
    /// be compared against a baseline.
    /// </summary>
    private async Task RunCorpusBenchmarkAsync(string corpusDirectory, string outputPath, string label)
    {
        try
        {
            var references = CorpusBenchmark.LoadReferences(corpusDirectory);
            if (references.Count == 0)
            {
                Environment.ExitCode = 2;
                await File.WriteAllTextAsync(
                    outputPath,
                    $"EMPTY: не найден {CorpusBenchmark.ReferenceFileName} в {corpusDirectory}. " +
                    "См. tests/corpus/README.md.");
                return;
            }

            using var service = CreateTranscriptionService();
            // Warm-up is excluded from the measurements on purpose: the first decode pays for ONNX
            // graph optimization and would dominate every percentile computed after it.
            await service.WarmUpAsync(null, CancellationToken.None);

            var entries = new List<BenchmarkEntry>(references.Count);
            foreach (var reference in references)
            {
                var audioPath = Path.Combine(corpusDirectory, reference.Audio);
                if (!File.Exists(audioPath))
                {
                    entries.Add(new BenchmarkEntry(
                        reference.Id, reference.Set, reference.Text, string.Empty, 0, 0, "audio missing"));
                    continue;
                }

                var trace = new DictationTrace();
                trace.Mark(DictationStage.CaptureStopped);
                try
                {
                    var result = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
                    trace.Mark(DictationStage.PrimaryDecoded);
                    var text = TranscriptNormalizer.Normalize(result.Text);
                    trace.Mark(DictationStage.TextFormatted);
                    entries.Add(new BenchmarkEntry(
                        reference.Id,
                        reference.Set,
                        reference.Text,
                        text,
                        trace.PerceivedLatency.TotalMilliseconds,
                        result.Elapsed.TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    entries.Add(new BenchmarkEntry(
                        reference.Id, reference.Set, reference.Text, string.Empty, 0, 0, exception.Message));
                }
            }

            var report = CorpusBenchmark.Summarize(label, entries);
            CorpusBenchmark.Save(report, outputPath);
            if (entries.Any(entry => entry.Error is not null))
            {
                Environment.ExitCode = 3;
            }
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunBenchmarkAsync(string audioPath, string outputPath)
    {
        try
        {
            using var service = CreateTranscriptionService();
            var cold = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            var warm = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            await File.WriteAllTextAsync(outputPath,
                $"cold={cold.Elapsed.TotalSeconds:0.00}s{Environment.NewLine}" +
                $"warm={warm.Elapsed.TotalSeconds:0.00}s{Environment.NewLine}" +
                warm.Text);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunStressBenchmarkAsync(string audioPath, string outputPath, int iterations)
    {
        try
        {
            using var service = CreateTranscriptionService();
            using var process = Process.GetCurrentProcess();
            var cold = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            string? expectedText = null;
            for (var index = 0; index < 5; index++)
            {
                var settling = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
                expectedText ??= settling.Text;
                if (!string.Equals(expectedText, settling.Text, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("ASR output changed during native-runtime settling.");
                }
            }
            process.Refresh();
            var privateBytesBefore = process.PrivateMemorySize64;
            var handlesBefore = process.HandleCount;
            var elapsed = new double[iterations];
            for (var index = 0; index < iterations; index++)
            {
                var result = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
                expectedText ??= result.Text;
                if (!string.Equals(expectedText, result.Text, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"ASR output changed during deterministic stress run {index + 1}.");
                }
                elapsed[index] = result.Elapsed.TotalMilliseconds;
            }

            Array.Sort(elapsed);
            process.Refresh();
            var privateBytesAfter = process.PrivateMemorySize64;
            var handlesAfter = process.HandleCount;
            var p50 = elapsed[(int)Math.Ceiling(iterations * 0.50) - 1];
            var p95 = elapsed[(int)Math.Ceiling(iterations * 0.95) - 1];
            var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedText!))).ToLowerInvariant();
            await File.WriteAllTextAsync(outputPath,
                $"PASS{Environment.NewLine}" +
                $"iterations={iterations}{Environment.NewLine}" +
                $"coldMs={cold.Elapsed.TotalMilliseconds:0.0}{Environment.NewLine}" +
                $"p50Ms={p50:0.0}{Environment.NewLine}" +
                $"p95Ms={p95:0.0}{Environment.NewLine}" +
                $"maxMs={elapsed[^1]:0.0}{Environment.NewLine}" +
                $"privateBytesDelta={privateBytesAfter - privateBytesBefore}{Environment.NewLine}" +
                $"handleDelta={handlesAfter - handlesBefore}{Environment.NewLine}" +
                $"textSha256={textHash}");
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunGigaBenchmarkAsync(string audioPath, string outputPath)
    {
        try
        {
            using var manager = new ModelManager(ModelCatalog.CreateRequiredModels());
            using var service = new GigaAmTranscriptionService(manager);
            var cold = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            var warm = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            await File.WriteAllTextAsync(outputPath,
                $"cold={cold.Elapsed.TotalSeconds:0.000}s{Environment.NewLine}" +
                $"warm={warm.Elapsed.TotalSeconds:0.000}s{Environment.NewLine}" +
                warm.Text);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunWhisperBenchmarkAsync(string audioPath, string outputPath)
    {
        try
        {
            using var manager = new ModelManager(ModelCatalog.CreateRequiredModels());
            using var service = new WhisperTranscriptionService(manager);
            var cold = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            var warm = await service.TranscribeAsync(audioPath, null, CancellationToken.None);
            await File.WriteAllTextAsync(outputPath,
                $"cold={cold.Elapsed.TotalSeconds:0.000}s{Environment.NewLine}" +
                $"warm={warm.Elapsed.TotalSeconds:0.000}s{Environment.NewLine}" +
                warm.Text);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task RunPipelineSmokeAsync(string audioPath, string outputPath)
    {
        Window? testWindow = null;
        try
        {
            // AcceptsReturn is required, not cosmetic: a single-line TextBox silently truncates a
            // multi-line paste at the first newline. Long-form dictation produces paragraphs, so
            // without this the harness reports a character-count mismatch and blames the product
            // for a limitation of its own test window.
            var textBox = new System.Windows.Controls.TextBox
            {
                FontSize = 16,
                Margin = new Thickness(12),
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
            };
            testWindow = new Window
            {
                Title = "Egoist Voice pipeline smoke",
                Width = 520,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = textBox,
                ShowInTaskbar = false,
                Topmost = true
            };
            testWindow.Show();
            testWindow.Activate();
            textBox.Focus();
            Keyboard.Focus(textBox);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var target = new WindowInteropHelper(testWindow).Handle;
            NativeMethods.ActivateForDiagnostics(target);
            testWindow.Activate();
            textBox.Focus();
            Keyboard.Focus(textBox);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
            var activationDeadline = DateTime.UtcNow.AddSeconds(12);
            while (NativeMethods.GetForegroundWindow() != target && DateTime.UtcNow < activationDeadline)
            {
                await Task.Delay(100);
            }
            if (NativeMethods.GetForegroundWindow() != target)
            {
                throw new InvalidOperationException(
                    $"Pipeline smoke window was not activated within 12 seconds: target=0x{target:X}, foreground=0x{NativeMethods.GetForegroundWindow():X}.");
            }

            using var transcription = CreateTranscriptionService();
            var result = await transcription.TranscribeAsync(audioPath, null, CancellationToken.None);
            var text = TranscriptNormalizer.Normalize(result.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Smoke audio produced no text.");
            }

            NativeMethods.ActivateForDiagnostics(target);
            testWindow.Activate();
            textBox.Focus();
            Keyboard.Focus(textBox);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
            await new ClipboardService().CopyAsync(text, CancellationToken.None);
            await new TextInsertionService().InsertAsync(text, target, CancellationToken.None);
            await Task.Delay(150);

            if (!string.Equals(textBox.Text, text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"SendInput mismatch: expected {text.Length}, received {textBox.Text.Length} characters.");
            }

            await File.WriteAllTextAsync(outputPath,
                $"PASS{Environment.NewLine}" +
                $"characters={text.Length}{Environment.NewLine}" +
                $"elapsed={result.Elapsed.TotalSeconds:0.00}s");
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            await File.WriteAllTextAsync(outputPath, $"ERROR: {exception}");
        }
        finally
        {
            testWindow?.Close();
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write($"Exit code={e.ApplicationExitCode}");
        _tray?.Dispose();
        (MainWindow as IDisposable)?.Dispose();
        _shutdownRegistration?.Unregister(null);
        _shutdownEvent?.Dispose();
        _singleInstance?.Dispose();

        // Last, and after everything else has had its say: logging is asynchronous now, so the
        // records that explain a shutdown are still in the queue at this point.
        AppLog.Flush(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }

    private static ITranscriptionService CreateTranscriptionService()
    {
        var manager = new ModelManager(ModelCatalog.CreateRequiredModels());
        return new OwnedHybridTranscriptionService(manager);
    }
}

internal sealed class OwnedHybridTranscriptionService : ITranscriptionService
{
    private readonly IModelManager _manager;
    private readonly HybridTranscriptionService _inner;

    internal OwnedHybridTranscriptionService(IModelManager manager)
    {
        _manager = manager;
        _inner = new HybridTranscriptionService(manager);
    }

    public Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken) =>
        _inner.WarmUpAsync(progress, cancellationToken);

    public Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken) => _inner.TranscribeAsync(audioPath, progress, cancellationToken);

    public void Dispose()
    {
        _inner.Dispose();
        _manager.Dispose();
    }
}
