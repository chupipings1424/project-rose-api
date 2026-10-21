using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rose.Api.IntegrationTests;

public sealed class LingoTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/lingo/{path}", UriKind.Relative);

    [Fact]
    public async Task Languages_and_kana_are_listed()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var languages = await (await client.GetAsync(Url("languages"))).JsonAsync();
        Assert.Equal(["ja", "ko", "de", "en"], languages.EnumerateArray().Select(l => l.GetProperty("code").GetString()));
        Assert.Equal("ko-KR", languages[1].GetProperty("speech").GetString());

        var kana = await (await client.GetAsync(Url("kana"))).JsonAsync();
        Assert.Contains(kana.EnumerateArray(), k => k.GetProperty("hiragana").GetString() == "あ" && k.GetProperty("katakana").GetString() == "ア");
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
    }

    [Fact]
    public async Task Translator_failures_are_a_502_and_bad_requests_a_400()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var offline = await client.PostAsJsonAsync(Url("translate"), new { text = "offline", from = "en", to = "de" });
        Assert.Equal(HttpStatusCode.BadGateway, offline.StatusCode);
        Assert.Contains("not reachable", await offline.DetailAsync(), StringComparison.Ordinal);

        var same = await client.PostAsJsonAsync(Url("translate"), new { text = "hi", from = "en", to = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        var unknown = await client.PostAsJsonAsync(Url("translate"), new { text = "salut", from = "fr", to = "en" });
        Assert.Equal("from must be one of ja, ko, de, en.", await unknown.DetailAsync());
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
    public async Task Starter_deck_feeds_a_daily_quiz_that_can_be_taken_once()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var empty = await (await client.GetAsync(Url("quiz?language=de"))).JsonAsync();
        Assert.Equal(0, empty.GetProperty("questions").GetArrayLength());
        Assert.Equal(4, empty.GetProperty("cards_needed").GetInt32());

        var imported = await (await client.PostAsync(Url("starter/de"), content: null)).JsonAsync();
        Assert.True(imported.GetProperty("added").GetInt32() > 4);
        var again = await (await client.PostAsync(Url("starter/de"), content: null)).JsonAsync();
        Assert.Equal(0, again.GetProperty("added").GetInt32());

        var quiz = await (await client.GetAsync(Url("quiz?language=de"))).JsonAsync();
        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(5, questions.Count);
        Assert.All(questions, q => Assert.Contains(q.GetProperty("kind").GetString(), new[] { "meaning", "term", "listen" }));

        var wrongId = questions[0].GetProperty("card_id").GetString();
        var results = questions.Select((q, i) => new { card_id = q.GetProperty("card_id").GetString(), correct = i > 0 }).ToList();
        var submitted = await client.PostAsJsonAsync(Url("quiz"), new { language = "de", results });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(4, (await submitted.JsonAsync()).GetProperty("score").GetInt32());

        var twice = await client.PostAsJsonAsync(Url("quiz"), new { language = "de", results });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        var done = await (await client.GetAsync(Url("quiz?language=de"))).JsonAsync();
        Assert.Equal(4, done.GetProperty("completed").GetProperty("score").GetInt32());
        Assert.Equal(0, done.GetProperty("questions").GetArrayLength());

        var stats = await (await client.GetAsync(Url("stats?language=de"))).JsonAsync();
        Assert.True(stats.GetProperty("quiz_done_today").GetBoolean());
        Assert.Equal(1, stats.GetProperty("streak").GetInt32());

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
