using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class LocalTextFormatterTests
{
    [Fact]
    public void Local_model_names_are_canonicalized_without_replacing_KVN()
    {
        var output = UserDictionary.BuiltIn.Apply("квен и гига ам в войс студио. Смотрю КВН.");
        Assert.Equal("Qwen и GigaAM в VoiceStudio. Смотрю КВН.", output);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", true)]
    [InlineData("http://localhost:1234/v1/", true)]
    [InlineData("http://[::1]:8080/v1", true)]
    [InlineData("https://api.example.com/v1", false)]
    [InlineData("http://192.168.1.2:8080/v1", false)]
    [InlineData("http://127.0.0.1.example.org/v1", false)]
    [InlineData("http://u:p@localhost/v1", false)]
    [InlineData("http://localhost/v1?remote=1", false)]
    [InlineData("http://localhost/v1#fragment", false)]
    [InlineData("http://localhost/api", false)]
    public void Endpoint_is_explicitly_loopback_only(string address, bool valid) =>
        Assert.Equal(valid, LocalTextFormatter.TryGetEndpoint(address, out _));

    [Theory]
    [InlineData("привет как дела", "Привет! Как дела?", true)]
    [InlineData("первая мысль вторая мысль", "Первая мысль.\n\nВторая мысль.", true)]
    [InlineData("это не работает", "Это работает.", false)]
    [InlineData("срок пятнадцать дней", "Срок пятьдесят дней.", false)]
    [InlineData("нужен GitHub", "Нужен GitLab.", false)]
    [InlineData("он сказал да", "Он сказал да да.", false)]
    [InlineData("код 1.5", "Код 15.", false)]
    [InlineData("код 1.5", "код 1,5", false)]
    [InlineData("код -15", "код 15", false)]
    [InlineData("https://a.b/test", "https: //a.b/test", false)]
    [InlineData("привет", "Привет 👋", false)]
    [InlineData("привет", "При\u200bвет", false)]
    [InlineData("https://a.b/test", "https://a.b/test.", false)]
    [InlineData("C:\\work\\app", "C:\\work/app", false)]
    public void Automatic_mode_preserves_words_negation_numbers_and_identifiers(string original, string output, bool valid) =>
        Assert.Equal(valid, LocalTextFormatter.PreservesWords(original, output));

    [Fact]
    public async Task Adds_punctuation_through_local_chat_without_logging_or_wrapping()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Reply("Привет, мир!"));
        });
        using var service = new LocalTextFormatter(handler);
        var result = await Format(service, "привет мир");
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Привет, мир!", result.Text);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("завтра в 15:30 будет встреча пожалуйста не опаздывайте", "Завтра в ⟦EV0⟧ будет встреча. Пожалуйста, не опаздывайте.", "Завтра в 15:30 будет встреча. Пожалуйста, не опаздывайте.")]
    [InlineData("сумма -15.50 проверь её завтра", "Сумма ⟦EV0⟧. Проверь её завтра.", "Сумма -15.50. Проверь её завтра.")]
    [InlineData("открой C:\\work\\app и проверь настройки", "Открой ⟦EV0⟧ и проверь настройки.", "Открой C:\\work\\app и проверь настройки.")]
    public async Task Numbers_and_paths_do_not_disable_formatting_of_surrounding_speech(string source, string candidate, string expected)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Reply(candidate)));
        using var service = new LocalTextFormatter(handler);
        var result = await Format(service, source);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal(expected, result.Text);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("Встреча в 16:00 завтра.")]
    [InlineData("Встреча в ⟦EV0⟧ ⟦EV0⟧ завтра.")]
    [InlineData("Встреча в ⟦EV1⟧ завтра.")]
    public async Task Missing_duplicated_or_changed_protected_marker_keeps_original(string candidate)
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply(candidate))));
        var result = await Format(service, "встреча в 15:30 завтра");
        Assert.Equal(TextFormattingStatus.Rejected, result.Status);
        Assert.Equal("встреча в 15:30 завтра", result.Text);
    }

    [Theory]
    [InlineData("Я согласен.", "stop")]
    [InlineData("Я не согласен.", "length")]
    [InlineData("<think>Я не согласен.</think>", "stop")]
    [InlineData("", "stop")]
    public async Task Invalid_or_incomplete_completion_preserves_original(string candidate, string finish)
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply(candidate, finish))));
        var result = await Format(service, "Я не согласен");
        Assert.Equal("Я не согласен", result.Text);
        Assert.Equal(TextFormattingStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task Manual_correction_can_propose_different_words()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Он позвонит."))));
        var result = await service.FormatAsync("он позванит", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
    }

    [Fact]
    public async Task Manual_correction_preserves_markdown_bold_and_guillemets()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Завтра встреча в **Discord**."))));
        var result = await service.FormatAsync("завтра встреча в дискорде выдели жирным последнее слово", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Завтра встреча в **Discord**.", result.Text);
    }

    [Fact]
    public async Task Manual_correction_preserves_quoted_terms()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("«Egoist Shield»"))));
        var result = await service.FormatAsync("агатхилд возьми в кавычки", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("«Egoist Shield»", result.Text);
    }

    [Fact]
    public async Task Manual_correction_supports_exclamation_marks_command()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Поздравляю!!!"))));
        var result = await service.FormatAsync("поздравляю поставь 3 восклицательных знака", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Поздравляю!!!", result.Text);
    }

    [Fact]
    public async Task Manual_correction_rejects_hallucinated_expansion_for_short_phrases()
    {
        // When user says "продолжить", a hallucinated response with 15 words must be rejected.
        const string hallucination = "Продолжаю выполнение задачи: 1. Проверить API. 2. Залить на GitHub. 3. Протестировать.";
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply(hallucination))));
        var result = await service.FormatAsync("продолжить", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Rejected, result.Status);
        Assert.Equal("продолжить", result.Text);
    }

    [Fact]
    public async Task Manual_correction_accepts_clean_continuation_word()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Продолжить."))));
        var result = await service.FormatAsync("продолжить", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Продолжить.", result.Text);
    }

    [Fact]
    public async Task Manual_correction_supports_ellipsis_and_caps()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Я подумаю…"))));
        var result = await service.FormatAsync("я подумаю поставить троеточие", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Я подумаю…", result.Text);
    }

    [Fact]
    public async Task Manual_correction_strips_hallucinated_prompt_leak_prefix()
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(Reply("Ты уверен? Привет всем, как дела."))));
        var result = await service.FormatAsync("привет всем как дела", "http://127.0.0.1:11434/v1", "qwen3:4b",
            TimeSpan.FromSeconds(1), true, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Applied, result.Status);
        Assert.Equal("Привет всем, как дела.", result.Text);
    }

    [Fact]
    public async Task Timeout_returns_original_without_retry()
    {
        var handler = new StubHandler(async (_, token) => { await Task.Delay(10_000, token); return Reply("unused"); });
        using var service = new LocalTextFormatter(handler);
        var result = await service.FormatAsync("исходный текст", "http://localhost:11434/v1", "qwen",
            TimeSpan.FromMilliseconds(100), false, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Timeout, result.Status);
        Assert.Equal("исходный текст", result.Text);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_never_returns_text_for_delivery()
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new LocalTextFormatter(new StubHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(10_000, token);
            return Reply("unused");
        }));
        var operation = Format(service, "исходный текст", stop.Token);
        await entered.Task;
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Http_failure_does_not_break_dictation(HttpStatusCode code)
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(code))));
        Assert.Equal(TextFormattingStatus.Unavailable, (await Format(service, "текст")).Status);
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    public async Task Malformed_response_returns_original(string json)
    {
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })));
        Assert.Equal("текст", (await Format(service, "текст")).Text);
    }

    [Fact]
    public async Task Long_input_and_TTS_never_send_a_request()
    {
        var handler = new StubHandler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected HTTP"));
        using var service = new LocalTextFormatter(handler);
        Assert.Equal(TextFormattingStatus.TooLong, (await Format(service, new string('a', 8001))).Status);
        var tts = await service.FormatAsync("текст", "http://localhost:3900/v1", "Qwen3-TTS",
            TimeSpan.FromSeconds(1), false, CancellationToken.None);
        Assert.Equal(TextFormattingStatus.Unavailable, tts.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Model_discovery_excludes_audio_and_embedding_models()
    {
        const string json = "{\"data\":[{\"id\":\"Qwen3-TTS\"},{\"id\":\"qwen3:4b\"},{\"id\":\"qwen3-asr\"},{\"id\":\"embed-v1\"}]}";
        using var service = new LocalTextFormatter(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })));
        Assert.Equal(new[] { "qwen3:4b" }, await service.GetModelsAsync("http://localhost:11434/v1", CancellationToken.None));
    }

    [Fact]
    public void Compact_catalog_contains_only_existing_Russian_core_and_fits_model_budget()
    {
        var models = ModelCatalog.CreateCompactModels();
        Assert.Equal(4, models.Count);
        Assert.Equal(326_322_304L, models.Sum(m => m.SizeBytes));
        Assert.DoesNotContain(ModelCatalog.Whisper, models);
        Assert.All(models, m => Assert.Contains(m, ModelCatalog.CreateRequiredModels()));
    }

    [Fact]
    public void LocalQwenHost_TrimWorkingSet_ExecutesSafely()
    {
        var exception = Record.Exception(() => LocalQwenHost.TrimWorkingSet());
        Assert.Null(exception);
    }

    private static Task<TextFormattingResult> Format(LocalTextFormatter service, string text, CancellationToken token = default) =>
        service.FormatAsync(text, "http://127.0.0.1:11434/v1", "qwen3:4b", TimeSpan.FromSeconds(1), false, token);

    private static HttpResponseMessage Reply(string text, string finish = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        { choices = new[] { new { message = new { content = text }, finish_reason = finish } } }), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return response(request, cancellationToken); }
    }
}
