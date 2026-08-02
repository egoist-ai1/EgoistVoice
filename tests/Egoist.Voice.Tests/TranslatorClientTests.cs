using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class TranslatorClientTests
{
    private const string ModelsResponse =
        """{"object":"list","data":[{"id":"C:\\Models\\HY-MT1.5-7B-Q8_0.gguf"}]}""";

    [Fact]
    public async Task HealthyHyMtServer_ReturnsTranslation()
    {
        string? requestBody = null;
        using var client = CreateClient(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Hello"}}]}""");
        });

        var result = await client.TranslateAsync("Привет", "English", _ => { }, CancellationToken.None);

        Assert.Equal("Hello", result);
        using var request = JsonDocument.Parse(requestBody!);
        var prompt = request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("Привет", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonSuccessResponse_NeverMasqueradesAsTranslation()
    {
        using var client = CreateClient((_, _) => Task.FromResult(
            Json(HttpStatusCode.ServiceUnavailable, """{"choices":[{"message":{"content":"Loading"}}]}""")));

        var result = await client.TranslateAsync("Текст", "English", _ => { }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task MalformedResponse_FallsBackToOriginalTextContract()
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "not-json")));

        var result = await client.TranslateAsync("Текст", "English", _ => { }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"id\":\"other-model.gguf\"}]}")]
    [InlineData("not-json")]
    public void UnexpectedPortOwner_IsRejectedBeforePrivateTextIsSent(string response) =>
        Assert.False(TranslatorClient.HasExpectedModel(response));

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}")));
        client.Dispose();
        client.Dispose();
    }

    private static TranslatorClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> apiResponder)
    {
        var health = new HttpClient(new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath switch
            {
                "/health" => Json(HttpStatusCode.OK, """{"status":"ok"}"""),
                "/v1/models" => Json(HttpStatusCode.OK, ModelsResponse),
                _ => Json(HttpStatusCode.NotFound, "{}")
            })));
        var api = new HttpClient(new StubHandler(apiResponder));
        return new TranslatorClient(health, api);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}
