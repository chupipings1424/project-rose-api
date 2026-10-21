using Rose.Domain.Lingo;

namespace Rose.UnitTests.Domain;

public sealed class RomanizerTests
{
    [Theory]
    [InlineData("ありがとう", "arigatou")]
    [InlineData("こんにちは", "konnichiwa")]
    [InlineData("きっぷ", "kippu")]
    [InlineData("まっちゃ", "matcha")]
    [InlineData("しゃしん", "shashin")]
    [InlineData("ぎゅうにゅう", "gyuunyuu")]
    [InlineData("じょうず", "jouzu")]
    [InlineData("きんようび", "kin'youbi")]
    [InlineData("ラーメン", "raamen")]
    [InlineData("コーヒー", "koohii")]
    [InlineData("パーティー", "paatii")]
    [InlineData("ファイト", "faito")]
    public void Kana_becomes_hepburn_romaji(string kana, string romaji) =>
        Assert.Equal(romaji, Romanizer.Romanize(Languages.Japanese, kana));

    [Theory]
    [InlineData("水")]
    [InlineData("日本語")]
    [InlineData("hello")]
    public void Kanji_and_latin_have_no_automatic_reading(string text) =>
        Assert.Equal("", Romanizer.Romanize(Languages.Japanese, text));

    [Theory]
    [InlineData("안녕하세요", "annyeonghaseyo")]
    [InlineData("감사합니다", "gamsahamnida")]
    [InlineData("한국어", "hangugeo")]
    [InlineData("사랑해요", "saranghaeyo")]
    [InlineData("맛있어요", "masisseoyo")]
    [InlineData("괜찮아요", "gwaenchanayo")]
    [InlineData("좋아요", "joayo")]
    [InlineData("좋다", "jota")]
    [InlineData("축하", "chuka")]
    [InlineData("신라", "silla")]
    [InlineData("서울", "seoul")]
    [InlineData("물", "mul")]
    [InlineData("한국 사람", "hanguk saram")]
    public void Hangul_becomes_revised_romanization(string hangul, string latin) =>
        Assert.Equal(latin, Romanizer.Romanize(Languages.Korean, hangul));

    [Fact]
    public void German_and_English_have_no_reading() =>
        Assert.Equal("", Romanizer.Romanize(Languages.German, "Danke"));

    [Fact]
    public void The_kana_chart_pairs_hiragana_with_katakana()
    {
        Assert.Contains(Kana.Chart, k => k is { Hiragana: "し", Katakana: "シ", Romaji: "shi", Group: "basic" });
        Assert.Contains(Kana.Chart, k => k is { Hiragana: "きゃ", Katakana: "キャ", Romaji: "kya", Group: "combo" });
        Assert.Equal(46, Kana.Chart.Count(k => k.Group == "basic"));
    }

    [Fact]
    public void Every_starter_word_has_a_reading_when_its_language_needs_one()
    {
        foreach (var (language, words) in StarterDecks.All)
        {
            var info = Languages.All.Single(l => l.Code == language);
            foreach (var word in words)
            {
                var card = new Card(language, word.Term, word.Reading, word.Meaning, word.Notes, DateTimeOffset.UnixEpoch, new DateOnly(2026, 1, 1));
                Assert.Equal(info.HasReading, card.Reading.Length > 0);
            }
        }
    }
}
