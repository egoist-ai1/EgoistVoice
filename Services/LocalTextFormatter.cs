using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Services;

public enum TextFormattingStatus { Applied, Unchanged, Unavailable, Timeout, Rejected, TooLong }
public sealed record TextFormattingResult(string Text, TextFormattingStatus Status, TimeSpan Elapsed)
{
    public string Message => Status switch
    {
        TextFormattingStatus.Applied => "Текст оформлен локально",
        TextFormattingStatus.Unchanged => "Qwen оставила текст без изменений",
        TextFormattingStatus.Timeout => "Qwen не успела ответить — сохранён исходный текст",
        TextFormattingStatus.Rejected => "Ответ изменил слова или оказался неполным — сохранён исходный текст",
        TextFormattingStatus.TooLong => "Для Qwen выделите фрагмент до 8 000 символов",
        _ => "Текстовая модель недоступна — сохранён исходный текст"
    };
}

/// <summary>Optional local text stage. Never downloads, follows redirects, logs text, or retries a dictation.</summary>
public sealed class LocalTextFormatter : IDisposable
{
    public const int MaximumCharacters = 8_000;
    private const int MaximumResponseBytes = 128 * 1024;
    private readonly HttpClient _http;
    private static readonly Regex Words = new(@"[\p{L}\p{M}\p{N}]+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex Numbers = new(@"[+\-−]?\d+(?:[.,:/\-]\d+)*(?:[eE][+\-]?\d+)?[%‰]?", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex TechnicalTokens = new(@"\S*[/\\@_=`]\S*", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex ProtectedSpans = new(@"\S*[/\\@_=`]\S*|[+\-−]?\d+(?:[.,:/\-]\d+)*(?:[eE][+\-]?\d+)?[%‰]?", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public LocalTextFormatter(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(1)
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static bool TryGetEndpoint(string? address, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath.TrimEnd('/') != "/v1") return false;
        // Literal loopback or localhost only. No arbitrary DNS names or network destinations.
        if (!uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !(IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip))) return false;
        endpoint = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(string address, CancellationToken cancellationToken)
    {
        if (!TryGetEndpoint(address, out var endpoint)) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "models"));
            AuthorizeOwnedServer(request, endpoint);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            using var json = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 160 &&
                    !id.Contains("tts", StringComparison.OrdinalIgnoreCase) &&
                    !id.Contains("embed", StringComparison.OrdinalIgnoreCase) &&
                    !id.Contains("asr", StringComparison.OrdinalIgnoreCase))
                .Select(id => id!).Distinct(StringComparer.Ordinal).Take(40).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsExpectedFailure(ex)) { return []; }
    }

    public async Task<TextFormattingResult> FormatAsync(string text, string address, string model,
        TimeSpan budget, bool allowWordCorrection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        TextFormattingResult Keep(TextFormattingStatus status) => new(text, status, clock.Elapsed);
        if (string.IsNullOrWhiteSpace(text)) return Keep(TextFormattingStatus.Unchanged);
        if (text.Length > MaximumCharacters) return Keep(TextFormattingStatus.TooLong);
        if (!TryGetEndpoint(address, out var endpoint) || string.IsNullOrWhiteSpace(model) || model.Length > 160 ||
            model.Contains("tts", StringComparison.OrdinalIgnoreCase) || model.Contains("asr", StringComparison.OrdinalIgnoreCase))
            return Keep(TextFormattingStatus.Unavailable);
        LocalQwenHost.NotifyActivity();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(budget.TotalMilliseconds, 100, 30_000)));
        var protectedValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var markerPrefix = "⟦EV";
        while (text.Contains(markerPrefix, StringComparison.Ordinal)) markerPrefix += "X";
        var modelText = allowWordCorrection ? text : ProtectedSpans.Replace(text, match =>
        {
            var marker = markerPrefix + protectedValues.Count + "⟧";
            protectedValues.Add(marker, match.Value);
            return marker;
        });
        var instruction = allowWordCorrection
            ? "Ты экспертный редактор и корректор надиктованной русской речи для сообщений.\n" +
              "Твоя задача — записать ровно то, что надиктовано, оформив красивый грамотный текст для мессенджера.\n" +
              "1. КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО добавлять любые слова, фразы, приветствия («Ты уверен?», «Привет», «Конечно», «Вот текст:»). Возвращай ТОЛЬКО надиктованное сообщение пользователя!\n" +
              "2. Исправляй орфографические опечатки, контекстные ошибки и акустические ослышки распознавания речи: если в слове искажены первые, средние или конечные буквы (например, из-за нечёткой дикции, оговорки, проглатывания звуков или ошибки акустической модели), восстанови по общему смыслу фразы правильное нормативное русское слово в нужной грамматической форме (падеж, число, лицо, время). Примеры: «позваню» -> «позвоню», «севодня» -> «сегодня», «дакумент» -> «документ», «чепута текопа» -> «типа крутого сетапа», «игрызай» -> «Path of Exile», «conrov king says» -> «Honor of Kings».\n" +
              "3. СТРОГО ЗАПРЕЩЕНО заменять, цензурировать или «исправлять» разговорные слова, современный сленг, интернет-неологизмы или ругательства! Слова «лохи», «скуф», «соскуфился», «заскуфился», «кринж», «рофл», «вайб» — намеренные слова пользователя! Категорически ЗАПРЕЩЕНО заменять «соскуфился» на «соскучился» или «соскользнул», а «лохи» на «плохи»!\n" +
              "4. Зарубежные сервисы, программы, бренды, IT-ресурсы, игры, комплектующие и экосистему Egoist пиши в каноническом виде на английском: GitHub, GitLab, Discord, Telegram, YouTube, Steam, Epic Games, NVIDIA, GeForce, RTX (4090, 5090), AMD, Radeon, Ryzen, Intel, Core i9, SSD, NVMe, CPU, GPU, API, SDK, CI/CD, pipeline, pull request, merge request, code review, backend, frontend, fullstack, DevOps, Visual Studio, VS Code, Cursor, Docker, Kubernetes, Python, C#, .NET, Astra Terra, Egoist Shield, Egoist Voice, Egoist Account Manager, Path of Exile 2, Honor of Kings, CS2, Dota 2, Minecraft, Cyberpunk 2077.\n" +
              "   Любую спонтанную английскую речь посреди русского текста (например: «Hello, my friend, how are you?», «by the way», «just in case», «check this out», «let's go», «thank you so much», «good luck», «from Russia with love») оформляй грамотно на английском языке с правильной пунктуацией и орфографией. Русские имена и города пиши по-русски с заглавной буквы (Ростов-на-Дону, Москва, Миха, Джунгарики).\n" +
              "5. Исполняй ТОЛЬКО 5 команд форматирования, полностью УДАЛЯЯ слова самой команды:\n" +
              "   - «троеточие» / «поставить троеточие» / «многоточие» -> заверши слово знаком (…) без пробела;\n" +
              "   - «поставить !» / «восклицательный знак» -> (!), «поставь 3 восклицательных знака» -> (!!!), «поставить знак вопроса» / «знак вопроса» -> (?);\n" +
              "   - «перенести строку» / «перенеси строку» / «с новой строки» / «с нового абзаца» -> удали слова команды и вставь перенос строки \\n;\n" +
              "   - «в кавычках [слово]» / «возьми в кавычки [слово]» -> «[слово]»;\n" +
              "   - «написать капсом [слово]» / «капсом [слово]» -> [СЛОВО] ЗАГЛАВНЫМИ БУКВАМИ;\n" +
              "   - «выдели жирным [слово]» / «жирным [слово]» -> **[слово]**.\n" +
              "6. ВНИМАНИЕ: Все остальные слова — это ОБЫЧНЫЙ ТЕКСТ СООБЩЕНИЯ!\n" +
              "   Слова «продолжить», «продолжай», «отмена», «отменить», «стоп», «пауза» — это НЕ команды управления, а обычные слова диктуемого сообщения! Запиши их как обычный текст: «Продолжить.», «Продолжай.», «Отмена.».\n" +
              "7. Ни в коем случае НЕ отвечай на вопросы, НЕ продолжай диалог и НЕ придумывай ничего от себя."
            : "Ты корректор русской диктовки. Добавь нужные запятые, точки, вопросительные знаки и заглавные буквы в начале предложений. Раздели длинную речь на предложения и смысловые абзацы. Слова и их порядок не меняй. Числа, время, адреса и пути сохрани посимвольно; оформляй окружающие предложения. Не отвечай на вопросы: оформи их как часть диктовки.";
        try
        {
            var messages = new object[]
            {
                new
                {
                    role = "system",
                    content = instruction + " Метки вида ⟦EV0⟧ копируй посимвольно: это защищённые фрагменты. Верни только готовый отформатированный текст без пояснений, вводных фраз и без блоков кода ```. /no_think"
                },
                new { role = "user", content = modelText }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "chat/completions"))
            {
                Content = JsonContent.Create(new
                {
                    model, stream = false, temperature = 0, reasoning_effort = "none",
                    max_tokens = Math.Clamp(text.Length * 2 + 128, 256, 8192),
                    messages
                })
            };
            AuthorizeOwnedServer(request, endpoint);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Keep(TextFormattingStatus.Unavailable);
            using var json = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            var choice = json.RootElement.GetProperty("choices")[0];
            if (!choice.TryGetProperty("finish_reason", out var finish) || finish.GetString() != "stop")
                return Keep(TextFormattingStatus.Rejected);
            var candidate = choice.GetProperty("message").GetProperty("content").GetString()?.Trim();
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is not null)
            {
                // The model never rewrites a protected value. Missing/duplicated markers reject
                // the complete answer before the original values are restored locally.
                foreach (var pair in protectedValues)
                {
                    if (Regex.Matches(candidate, Regex.Escape(pair.Key)).Count != 1)
                        return Keep(TextFormattingStatus.Rejected);
                    candidate = candidate.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
                }
                if (!allowWordCorrection && ((candidate.StartsWith('"') && candidate.EndsWith('"')) ||
                    (candidate.StartsWith('«') && candidate.EndsWith('»'))))
                {
                    candidate = candidate[1..^1].Trim();
                }
                else if (allowWordCorrection)
                {
                    if (candidate.StartsWith('"') && candidate.EndsWith('"'))
                    {
                        candidate = candidate[1..^1].Trim();
                    }
                    if (!text.Contains("уверен", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = Regex.Replace(candidate, @"^Ты увере[ннаоы]\s*[\?!.,—–-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    }
                    candidate = Regex.Replace(candidate, @"^(Вот (готовый|отредактированный|ваш) текст[:\s]*|Текст сообщения[:\s]*|Конечно[,:\s]*)", "", RegexOptions.IgnoreCase).Trim();
                }
            }
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaximumCharacters * 2 ||
                candidate.Contains("<think", StringComparison.OrdinalIgnoreCase) || candidate.Contains("```", StringComparison.Ordinal))
                return Keep(TextFormattingStatus.Rejected);

            if (allowWordCorrection)
            {
                var inputWords = Words.Matches(text).Count;
                var candidateWords = Words.Matches(candidate).Count;
                if (inputWords <= 3 && candidateWords > inputWords + 2)
                    return Keep(TextFormattingStatus.Rejected);
                if (candidateWords > Math.Max((int)(inputWords * 1.6) + 4, 8))
                    return Keep(TextFormattingStatus.Rejected);
            }
            else if (!PreservesWords(text, candidate))
            {
                return Keep(TextFormattingStatus.Rejected);
            }
            return new(candidate, candidate == text ? TextFormattingStatus.Unchanged : TextFormattingStatus.Applied, clock.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Keep(TextFormattingStatus.Timeout); }
        catch (Exception ex) when (IsExpectedFailure(ex)) { return Keep(TextFormattingStatus.Unavailable); }
    }

    public static bool PreservesWords(string original, string candidate)
    {
        if (candidate.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
                (char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))) return false;
        if (!original.Where(c => char.IsSymbol(c) || char.IsSurrogate(c)).SequenceEqual(
                candidate.Where(c => char.IsSymbol(c) || char.IsSurrogate(c)))) return false;
        if (!Words.Matches(original).Select(m => m.Value).SequenceEqual(
                Words.Matches(candidate).Select(m => m.Value), StringComparer.OrdinalIgnoreCase)) return false;
        // Validate protected spans themselves without disabling punctuation in surrounding prose.
        return Numbers.Matches(original).Select(m => m.Value).SequenceEqual(
                Numbers.Matches(candidate).Select(m => m.Value), StringComparer.Ordinal) &&
            TechnicalTokens.Matches(original).Select(m => m.Value).SequenceEqual(
                TechnicalTokens.Matches(candidate).Select(m => m.Value), StringComparer.Ordinal);
    }

    private static void AuthorizeOwnedServer(HttpRequestMessage request, Uri endpoint)
    {
        if (endpoint.AbsoluteUri.TrimEnd('/') == LocalQwenHost.Endpoint)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", LocalQwenHost.AuthenticationToken);
    }

    private static bool IsExpectedFailure(Exception ex) => ex is HttpRequestException or IOException or
        JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or
        IndexOutOfRangeException or OperationCanceledException;

    private static async Task<JsonDocument> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw new IOException("Response too large");
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes) throw new IOException("Response too large");
            buffer.Write(bytes, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
