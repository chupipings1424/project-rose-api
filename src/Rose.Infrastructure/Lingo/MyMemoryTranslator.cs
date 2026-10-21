using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Domain.Common;

namespace Rose.Infrastructure.Lingo;

/// <summary>
/// Translates with MyMemory (https://mymemory.translated.net): free, no key needed.
/// The text you translate is sent to their servers, so do not translate anything private.
/// </summary>
internal sealed class MyMemoryTranslator(HttpClient http, IOptions<LingoOptions> options, ILogger<MyMemoryTranslator> logger) : ITranslator
{
    private const string Unreachable = "The translator is not reachable right now - check your internet and try again.";

    public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
    {
        var query = $"get?q={Uri.EscapeDataString(text)}&langpair={from}|{to}";
        if (options.Value.TranslatorEmail is { Length: > 0 } email) query += $"&de={Uri.EscapeDataString(email)}";

        JsonElement body;
        try
        {
            using var response = await http.GetAsync(new Uri(query, UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new UpstreamException(QuotaMessage);
            response.EnsureSuccessStatusCode();
            body = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(e, "MyMemory request failed");
            throw new UpstreamException(Unreachable);
        }

        if (body.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            throw new UpstreamException(QuotaMessage);

        var status = body.TryGetProperty("responseStatus", out var s) ? s.ToString() : "";
        var translated = body.TryGetProperty("responseData", out var data) && data.TryGetProperty("translatedText", out var t)
            ? t.GetString()
            : null;
        if (status != "200" || string.IsNullOrWhiteSpace(translated))
        {
            var details = body.TryGetProperty("responseDetails", out var d) ? d.ToString() : "";
            logger.LogWarning("MyMemory said {Status}: {Details}", status, details);
            throw new UpstreamException($"The translator could not translate that{(details.Length > 0 ? $": {details}" : ".")}");
        }

        // MyMemory sometimes returns HTML entities such as &#39;.
        return WebUtility.HtmlDecode(translated).Trim();
    }

    private static string QuotaMessage =>
        "Today's free translation limit is used up - try again tomorrow (or set Lingo:TranslatorEmail for a higher limit).";
}
