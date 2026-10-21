using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rose.Domain.Common;
using Rose.Infrastructure.Lingo;

namespace Rose.UnitTests.Infrastructure;

public sealed class MyMemoryTranslatorTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private static (MyMemoryTranslator Translator, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? email = null)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mymemory.translated.net/") };
        var options = Options.Create(new LingoOptions { TranslatorEmail = email });
        return (new MyMemoryTranslator(http, options, NullLogger<MyMemoryTranslator>.Instance), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Reads_the_translation_and_decodes_html_entities()
    {
        var (translator, handler) = Create(_ => Json("""
            {"responseData":{"translatedText":"It&#39;s ありがとう","match":1},"quotaFinished":false,"responseStatus":200,"responseDetails":""}
            """), email: "me@example.com");

        var result = await translator.TranslateAsync("thank you & bye", "en", "ja", CancellationToken.None);

        Assert.Equal("It's ありがとう", result);
        Assert.Equal("https://api.mymemory.translated.net/get?q=thank%20you%20%26%20bye&langpair=en|ja&de=me%40example.com", handler.LastUri!.OriginalString);
    }

    [Fact]
    public async Task A_used_up_quota_says_so()
    {
        var (translator, _) = Create(_ => Json("""{"responseData":{"translatedText":"MYMEMORY WARNING"},"quotaFinished":true,"responseStatus":429}"""));

        var error = await Assert.ThrowsAsync<UpstreamException>(() => translator.TranslateAsync("hi", "en", "de", CancellationToken.None));

        Assert.Contains("free translation limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_status_in_the_body_is_reported()
    {
        // MyMemory sends some statuses as strings and keeps HTTP 200.
        var (translator, _) = Create(_ => Json("""{"responseData":{"translatedText":"INVALID LANGUAGE PAIR"},"responseStatus":"403","responseDetails":"INVALID LANGUAGE PAIR"}"""));

        var error = await Assert.ThrowsAsync<UpstreamException>(() => translator.TranslateAsync("hi", "en", "de", CancellationToken.None));

        Assert.Equal("The translator could not translate that: INVALID LANGUAGE PAIR", error.Message);
    }

    [Fact]
    public async Task Network_failures_become_a_friendly_error()
    {
        var (translator, _) = Create(_ => throw new HttpRequestException("The SSL connection could not be established"));

        var error = await Assert.ThrowsAsync<UpstreamException>(() => translator.TranslateAsync("hi", "en", "de", CancellationToken.None));

        Assert.Contains("not reachable", error.Message, StringComparison.Ordinal);
    }
}
