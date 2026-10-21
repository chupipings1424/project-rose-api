using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rose.Api.IntegrationTests;

public sealed class LingoTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/lingo/{path}", UriKind.Relative);

    [Fact]
    public async Task Languages_kana_hangul_and_translator_status_are_listed()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var languages = await (await client.GetAsync(Url("languages"))).JsonAsync();
        Assert.Equal(["tl", "en", "ja", "ko", "zh-Hans", "zh-Hant", "de"], languages.EnumerateArray().Select(l => l.GetProperty("code").GetString()));
        Assert.Equal("fil-PH", languages[0].GetProperty("speech").GetString());

        var kana = await (await client.GetAsync(Url("kana"))).JsonAsync();
        Assert.Contains(kana.EnumerateArray(), k => k.GetProperty("hiragana").GetString() == "あ" && k.GetProperty("katakana").GetString() == "ア");

        var hangul = await (await client.GetAsync(Url("hangul"))).JsonAsync();
        Assert.Equal(40, hangul.GetArrayLength());
        Assert.Contains(hangul.EnumerateArray(), h => h.GetProperty("letter").GetString() == "ㄱ" && h.GetProperty("syllable").GetString() == "가");

        var status = await (await client.GetAsync(Url("translator"))).JsonAsync();
        Assert.True(status.GetProperty("installed").GetBoolean());
        Assert.Equal("fake", status.GetProperty("engine").GetString());
    }

    [Fact]
    public async Task Translation_comes_back_with_a_reading()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Url("translate"), new { text = "thank you", from = "en", to = "ja" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("ありがとう", body.GetProperty("translation").GetString());
        Assert.Equal("arigatou", body.GetProperty("translation_reading").GetString());

        var chinese = await (await client.PostAsJsonAsync(Url("translate"), new { text = "你好", from = "ZH-hans", to = "tl" })).JsonAsync();
        Assert.Equal("nǐ hǎo", chinese.GetProperty("text_reading").GetString());
        Assert.Equal("zh-Hans", chinese.GetProperty("from").GetString());
    }

    [Fact]
    public async Task Translator_failures_are_a_502_or_503_and_bad_requests_a_400()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var broken = await client.PostAsJsonAsync(Url("translate"), new { text = "broken", from = "en", to = "de" });
        Assert.Equal(HttpStatusCode.BadGateway, broken.StatusCode);

        var uninstalled = await client.PostAsJsonAsync(Url("translate"), new { text = "uninstalled", from = "tl", to = "en" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, uninstalled.StatusCode);
        Assert.Contains("setup-lingo.cmd", await uninstalled.DetailAsync(), StringComparison.Ordinal);

        var same = await client.PostAsJsonAsync(Url("translate"), new { text = "hi", from = "en", to = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        var unknown = await client.PostAsJsonAsync(Url("translate"), new { text = "salut", from = "fr", to = "en" });
        Assert.Equal("from must be one of tl, en, ja, ko, zh-Hans, zh-Hant, de.", await unknown.DetailAsync());
    }

    [Fact]
    public async Task Cards_can_be_added_reviewed_edited_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(Url("cards"), new { language = "ko", term = "감사합니다", meaning = "Thank you" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = await created.JsonAsync();
        var id = card.GetProperty("id").GetString();
        Assert.Equal("gamsahamnida", card.GetProperty("reading").GetString());
        Assert.True(card.GetProperty("is_due").GetBoolean());

        var duplicate = await client.PostAsJsonAsync(Url("cards"), new { language = "ko", term = "감사합니다", meaning = "Thanks" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var due = await (await client.GetAsync(Url("cards/due?language=ko"))).JsonAsync();
        Assert.Contains(due.EnumerateArray(), c => c.GetProperty("id").GetString() == id);

        var reviewed = await (await client.PostAsJsonAsync(Url($"cards/{id}/review"), new { grade = "good" })).JsonAsync();
        Assert.Equal(1, reviewed.GetProperty("interval_days").GetInt32());
        Assert.False(reviewed.GetProperty("is_due").GetBoolean());

        var badGrade = await client.PostAsJsonAsync(Url($"cards/{id}/review"), new { grade = "2" });
        Assert.Equal(HttpStatusCode.BadRequest, badGrade.StatusCode);

        var edited = await client.PatchAsJsonAsync(Url($"cards/{id}"), new { term = "감사합니다", meaning = "Thank you (formal)", notes = "polite" });
        var editedBody = await edited.JsonAsync();
        Assert.Equal("Thank you (formal)", editedBody.GetProperty("meaning").GetString());
        Assert.Equal(1, editedBody.GetProperty("repetitions").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"cards/{id}"))).StatusCode);
        var gone = await client.PostAsJsonAsync(Url($"cards/{id}/review"), new { grade = "good" });
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("That card no longer exists.", await gone.DetailAsync());
    }

    [Fact]
    public async Task Quizzes_can_be_taken_or_made_any_number_of_times()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var empty = await (await client.PostAsJsonAsync(Url("quiz"), new { language = "de", count = 5 })).JsonAsync();
        Assert.Equal(0, empty.GetProperty("questions").GetArrayLength());
        Assert.Equal(4, empty.GetProperty("cards_needed").GetInt32());

        var imported = await (await client.PostAsync(Url("starter/de"), content: null)).JsonAsync();
        Assert.True(imported.GetProperty("added").GetInt32() > 4);
        var again = await (await client.PostAsync(Url("starter/de"), content: null)).JsonAsync();
        Assert.Equal(0, again.GetProperty("added").GetInt32());

        var badCount = await client.PostAsJsonAsync(Url("quiz"), new { language = "de", count = 7 });
        Assert.Equal(HttpStatusCode.BadRequest, badCount.StatusCode);

        var quiz = await (await client.PostAsJsonAsync(Url("quiz"), new { language = "de", count = 5 })).JsonAsync();
        Assert.Equal("random", quiz.GetProperty("mode").GetString());
        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(5, questions.Count);
        Assert.All(questions, q => Assert.Contains(q.GetProperty("kind").GetString(), new[] { "meaning", "term", "listen" }));

        var wrongId = questions[0].GetProperty("card_id").GetString();
        var results = questions.Select((q, i) => new { card_id = q.GetProperty("card_id").GetString(), correct = i > 0 }).ToList();
        var submitted = await client.PostAsJsonAsync(Url("quiz/results"), new { language = "de", mode = "random", results });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(4, (await submitted.JsonAsync()).GetProperty("score").GetInt32());

        var picked = questions.Take(2).Select(q => q.GetProperty("card_id").GetString()).ToList();
        var made = await (await client.PostAsJsonAsync(Url("quiz"), new { language = "de", card_ids = picked })).JsonAsync();
        Assert.Equal("custom", made.GetProperty("mode").GetString());
        Assert.Equal(picked.Order(), made.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("card_id").GetString()).Order());
        var perfect = picked.Select(id => new { card_id = id, correct = true }).ToList();
        var second = await client.PostAsJsonAsync(Url("quiz/results"), new { language = "de", mode = "custom", results = perfect });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var missing = await client.PostAsJsonAsync(Url("quiz"), new { language = "de", card_ids = new[] { "nope" } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var history = (await (await client.GetAsync(Url("quiz/results?language=de"))).JsonAsync()).EnumerateArray().ToList();
        Assert.Equal(["custom", "random"], history.Select(h => h.GetProperty("mode").GetString()));
        Assert.Equal(2, history[0].GetProperty("score").GetInt32());

        var stats = await (await client.GetAsync(Url("stats?language=de"))).JsonAsync();
        Assert.True(stats.GetProperty("cards").GetInt32() > 4);
        Assert.False(stats.TryGetProperty("streak", out _));

        var forgotten = await (await client.GetAsync(Url("cards?language=de"))).JsonAsync();
        var card = forgotten.EnumerateArray().Single(c => c.GetProperty("id").GetString() == wrongId);
        Assert.Equal(1, card.GetProperty("lapses").GetInt32());
    }

    [Fact]
    public async Task English_has_no_starter_deck()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(Url("starter/en"), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lingo_needs_a_login()
    {
        var response = await factory.CreateClient().GetAsync(Url("cards"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
