using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice;

public partial class App
{
    private async Task RunLocalTranslationComparisonAsync(string reportPath)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var translator = new TranslatorClient();
            using var qwen = new LocalQwenHost();
            var health = await translator.EnsureReadyAsync(cancellation.Token);
            if (!await qwen.StartAsync()) throw new InvalidOperationException("Qwen unavailable.");
            using var client = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler
                { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", LocalQwenHost.AuthenticationToken);
            var rows = new List<object>();
            foreach (var (source, language) in new[] {
                ("Пожалуйста, перенесите встречу на завтра в 15:30.", "English"),
                ("Не удаляйте файл. Сумма составляет -15.50 евро.", "English"),
                ("I did not approve the transfer. Please keep the original document.", "Russian") })
            {
                var clock = Stopwatch.StartNew();
                var translation = await translator.TranslateAsync(source, language, _ => { }, cancellation.Token);
                var hyMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                using var response = await client.PostAsync(LocalQwenHost.Endpoint + "/chat/completions",
                    System.Net.Http.Json.JsonContent.Create(new { model = LocalQwenHost.ModelId, temperature = 0, max_tokens = 256,
                        messages = new[] { new { role = "system", content = "Translate the user's text into " + language + ". Preserve facts, negation, names and numeric values. Return only the translation. /no_think" }, new { role = "user", content = source } } }), cancellation.Token);
                response.EnsureSuccessStatusCode();
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation.Token));
                var output = result.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                rows.Add(new { syntheticSource = source, language, hySucceeded = translation.Succeeded, hyOutput = translation.Text,
                    hyFailure = translation.Failure.ToString(), hyMs, qwenOutput = output, qwenMs = clock.Elapsed.TotalMilliseconds });
            }
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { completed = true, engineHealth = health.State.ToString(), rows,
                note = "Only hard-coded synthetic text. Exploratory comparison, not a replacement decision or private quality evaluation." }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown();
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { completed = false, errorType = ex.GetType().Name }));
            Shutdown(1);
        }
    }

    private async Task RunLocalHistoryCheckAsync(string audioPath, string referencePath, string reportPath)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var history = new RecentRecordingHistoryService(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "history-fixture-" + Guid.NewGuid().ToString("N")));
            var samples = await Task.Run(() => AudioSampleReader.ReadMono16Khz(audioPath), cancellation.Token);
            if (!history.TryQueue(samples, 16_000, TimeSpan.FromSeconds(samples.Length / 16000d), RecentRecordingStatus.ProcessingFailed))
                throw new InvalidOperationException("Synthetic history queue failed.");
            await history.WaitForIdleAsync(TimeSpan.FromSeconds(15));
            var item = history.GetItems().Single();
            var decoded = await history.ReadSamplesForTranscriptionAsync(item.Id, cancellation.Token);
            // Prove ASR can finish after the user deletes the history row.
            await history.DeleteAsync(item.Id, cancellation.Token);
            using var manager = new ModelManager(VoiceRuntimeProfile.Models, allowDownload: false);
            using var engine = VoiceRuntimeProfile.CreateTranscription(manager);
            await engine.WarmUpAsync(null, cancellation.Token);
            var result = await ((ISampleTranscriptionService)engine).TranscribeSamplesAsync(decoded, 16_000, cancellation.Token);
            var score = RecognitionScorer.Score(await File.ReadAllTextAsync(referencePath, cancellation.Token), result.Text);
            var passed = score.WordErrors == 0 && history.GetItems().Count == 0;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed, sourceStatus = item.Status.ToString(),
                decodedSamples = decoded.Length, elapsedMs = result.Elapsed.TotalMilliseconds, score.WordErrors, score.ReferenceWords,
                deletedBeforeInference = true, note = "Synthetic AAC recovery; no recognized text persisted." }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(passed ? 0 : 1);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, errorType = ex.GetType().Name }));
            Shutdown(1);
        }
    }

    private async Task RunAsrThreadCheckAsync(string audioPath, string referencePath, string reportPath)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            using var manager = new ModelManager(ModelCatalog.CreateCompactModels(), allowDownload: false);
            var samples = await Task.Run(() => AudioSampleReader.ReadMono16Khz(audioPath), cancellation.Token);
            var reference = await File.ReadAllTextAsync(referencePath, cancellation.Token);
            var rows = new List<object>();
            foreach (var threads in new[] { 12, 4, 8, 2, 12 })
            {
                using var engine = new GigaAmTranscriptionService(manager, inferenceThreads: threads);
                await engine.WarmUpAsync(null, cancellation.Token);
                for (var run = 0; run < 4; run++)
                {
                    var result = await engine.TranscribeSamplesAsync(samples, 16_000, cancellation.Token);
                    var score = RecognitionScorer.Score(reference, result.Text);
                    rows.Add(new { threads, run, elapsedMs = result.Elapsed.TotalMilliseconds, score.WordErrors, score.ReferenceWords });
                }
            }
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { completed = true, Environment.ProcessorCount, rows,
                note = "Same synthetic fixture, native CPU ASR; repeat 12-thread baseline at both ends. No private accuracy claim." }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown();
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { completed = false, errorType = ex.GetType().Name }));
            Shutdown(1);
        }
    }

    private async Task RunQwenLifetimeProbeAsync(string reportPath)
    {
        using var host = new LocalQwenHost();
        var ready = await host.StartAsync();
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { ready }));
        if (!ready) { Shutdown(1); return; }
        // CLI-only native crash/Job Object oracle. Always self-terminates if the harness stops.
        await Task.Delay(TimeSpan.FromMinutes(2));
        Shutdown();
    }
    private async Task RunLocalQwenCheckAsync(string reportPath)
    {
        try
        {
            using var host = new LocalQwenHost();
            var startup = Stopwatch.StartNew();
            if (!await host.StartAsync())
            {
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, stage = "startup", host.Status }));
                Shutdown(1);
                return;
            }
            var startupMs = startup.Elapsed.TotalMilliseconds;
            using var formatter = new LocalTextFormatter();
            var fixtures = new[]
            {
                (Source: "привет как дела", Reference: "Привет, как дела?", Correction: false),
                (Source: "сегодня мы проверяем новую версию сначала запишем короткую фразу затем откроем аудиофайл", Reference: "Сегодня мы проверяем новую версию. Сначала запишем короткую фразу, затем откроем аудиофайл.", Correction: false),
                (Source: "это не ошибка я не хочу удалять файл", Reference: "Это не ошибка. Я не хочу удалять файл.", Correction: false),
                (Source: "сумма -15.50 адрес https://example.com/a", Reference: "сумма -15.50 адрес https://example.com/a", Correction: false),
                (Source: "завтра в 15:30 будет встреча пожалуйста не опаздывайте", Reference: "Завтра в 15:30 будет встреча. Пожалуйста, не опаздывайте.", Correction: false),
                (Source: "сумма -15.50 проверь её завтра", Reference: "Сумма -15.50. Проверь её завтра.", Correction: false),
                (Source: @"открой C:\work\app и проверь настройки", Reference: @"Открой C:\work\app и проверь настройки.", Correction: false),
                (Source: "я позваню тебе завтра", Reference: "Я позвоню тебе завтра.", Correction: true),
                (Source: "мы договарились встретиться завтра", Reference: "Мы договорились встретиться завтра.", Correction: true),
                (Source: "пожалуста сохрани этот дакумент", Reference: "Пожалуйста, сохрани этот документ.", Correction: true),
                (Source: "завтра начинаеться новая неделя", Reference: "Завтра начинается новая неделя.", Correction: true),
                (Source: "мне очень нравиться этот фильм", Reference: "Мне очень нравится этот фильм.", Correction: true),
                (Source: "я не буду удалять этот файл", Reference: "Я не буду удалять этот файл.", Correction: true)
            };
            var rows = new List<object>();
            var passed = true;
            foreach (var (source, reference, correction) in fixtures)
            {
                var result = await formatter.FormatAsync(source, LocalQwenHost.Endpoint, LocalQwenHost.ModelId,
                    TimeSpan.FromSeconds(30), correction, CancellationToken.None);
                var score = RecognitionScorer.Score(reference, result.Text);
                var intact = correction || LocalTextFormatter.PreservesWords(source, result.Text);
                var accepted = result.Status is TextFormattingStatus.Applied or TextFormattingStatus.Unchanged;
                // The misspelling "договарились" has two legitimate tense/aspect corrections.
                var alternative = source == "мы договарились встретиться завтра" &&
                    RecognitionScorer.Score("Мы договаривались встретиться завтра.", result.Text).WordErrors == 0;
                var fixturePass = correction ? accepted && (score.WordErrorRate == 0 || alternative)
                    : source.Contains("https://", StringComparison.Ordinal)
                        ? intact && (accepted || result.Status == TextFormattingStatus.Rejected)
                        : intact && accepted && result.Text != source && result.Text.Any(char.IsPunctuation);
                passed &= fixturePass;
                rows.Add(new { index = rows.Count, correction, status = result.Status.ToString(), elapsedMs = result.Elapsed.TotalMilliseconds,
                    preservedWords = LocalTextFormatter.PreservesWords(source, result.Text), score.WordErrors, score.ReferenceWords, acceptableAlternative = alternative,
                    changed = result.Text != source, punctuationCount = result.Text.Count(char.IsPunctuation), passed = fixturePass });
            }
            var live = await formatter.FormatAsync("сегодня проверяем новую версию", LocalQwenHost.Endpoint, LocalQwenHost.ModelId,
                TimeSpan.FromSeconds(2), false, CancellationToken.None);
            passed &= live.Status is TextFormattingStatus.Applied or TextFormattingStatus.Unchanged;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, startupMs, model = LocalQwenHost.ModelId, LocalQwenHost.ModelSha256, rows,
                liveBudget = new { status = live.Status.ToString(), elapsedMs = live.Elapsed.TotalMilliseconds },
                note = "Synthetic text only. Corrections remain review-only. No transcript/response stored."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(passed ? 0 : 1);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, errorType = ex.GetType().Name }));
            Shutdown(1);
        }
    }

    private async Task RunLocalAsrCheckAsync(string audioPath, string referencePath, string reportPath,
        bool scorePostProcessing = false)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var manager = new ModelManager(VoiceRuntimeProfile.Models, allowDownload: false);
            using var engine = VoiceRuntimeProfile.CreateTranscription(manager);
            var warm = Stopwatch.StartNew();
            await engine.WarmUpAsync(null, cancellation.Token);
            var warmupMs = warm.Elapsed.TotalMilliseconds;
            var reference = await File.ReadAllTextAsync(referencePath, cancellation.Token);
            var samples = await Task.Run(() => AudioSampleReader.ReadMono16Khz(audioPath), cancellation.Token);
            var postProcessor = new TranscriptPostProcessor(UserDictionary.BuiltIn,
                new PostProcessingOptions(ApplyNumberNormalization: true));
            var entityCatalog = new[] { "Ростов-на-Дону", "Egoist Games", "Path of Exile 2", "GitHub", "Discord", "ChatGPT" };
            var runs = new List<object>();
            var passed = true;
            for (var index = 0; index < 4; index++)
            {
                var clock = Stopwatch.StartNew();
                var result = await ((ISampleTranscriptionService)engine).TranscribeSamplesAsync(samples, 16_000, cancellation.Token);
                var score = RecognitionScorer.Score(reference, result.Text);
                var processed = postProcessor.Process(result.Text);
                var processedScore = RecognitionScorer.Score(reference, processed);
                var expectedEntities = entityCatalog.Where(entity => reference.Contains(entity, StringComparison.OrdinalIgnoreCase)).ToArray();
                var matchedEntities = expectedEntities.Count(entity => processed.Contains(entity, StringComparison.Ordinal));
                passed &= (scorePostProcessing ? processedScore.WordErrorRate : score.WordErrorRate) <= 0.15 && result.Text.Length > 0;
                if (scorePostProcessing) passed &= matchedEntities == expectedEntities.Length;
                runs.Add(new { index, elapsedMs = clock.Elapsed.TotalMilliseconds, reportedMs = result.Elapsed.TotalMilliseconds,
                    score.ReferenceWords, score.WordErrors, score.WordErrorRate, result.Text.Length,
                    processedWordErrors = processedScore.WordErrors, processedWordErrorRate = processedScore.WordErrorRate,
                    expectedEntityCount = expectedEntities.Length, matchedEntityCount = matchedEntities });
            }
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, scorePostProcessing, vocabularyVersion = BuiltInVocabulary.Version,
                profile = VoiceRuntimeProfile.Label, warmupMs, audioSeconds = samples.Length / 16000d,
                runs, note = "Synthetic public fixture only. No claim about private speech or end-to-end delivery. No transcript persisted."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(passed ? 0 : 1);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, errorType = ex.GetType().Name }));
            Shutdown(1);
        }
    }
}
