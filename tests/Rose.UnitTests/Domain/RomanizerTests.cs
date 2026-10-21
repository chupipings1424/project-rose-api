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

    [Theory]
    [InlineData(Languages.ChineseSimplified, "你好", "nǐ hǎo")]
    [InlineData(Languages.ChineseSimplified, "谢谢", "xiè xiè")]
    [InlineData(Languages.ChineseTraditional, "謝謝", "xiè xiè")]
    [InlineData(Languages.ChineseSimplified, "我爱你", "wǒ ài nǐ")]
    [InlineData(Languages.ChineseTraditional, "我愛你", "wǒ ài nǐ")]
    [InlineData(Languages.ChineseSimplified, "中国", "zhōng guó")]
    [InlineData(Languages.ChineseSimplified, "你好, ROSE!", "nǐ hǎo , ROSE!")]
    public void Chinese_becomes_pinyin_with_tone_marks(string language, string hanzi, string pinyin) =>
        Assert.Equal(pinyin, Romanizer.Romanize(language, hanzi));

    [Fact]
    public void Chinese_without_hanzi_has_no_reading() =>
        Assert.Equal("", Romanizer.Romanize(Languages.ChineseSimplified, "hello"));

    [Theory]
    [InlineData(Languages.German, "Danke")]
    [InlineData(Languages.English, "Thanks")]
    [InlineData(Languages.Tagalog, "Salamat")]
    public void Latin_alphabet_languages_have_no_reading(string language, string text) =>
        Assert.Equal("", Romanizer.Romanize(language, text));

    [Fact]
    public void The_hangul_chart_has_all_forty_letters_with_example_blocks()
    {
        Assert.Equal(40, HangulChart.Chart.Count);
        Assert.Equal(40, HangulChart.Chart.Select(h => h.Letter).Distinct().Count());
        Assert.Equal((14, 5, 10, 11), (
            HangulChart.Chart.Count(h => h.Group == "consonant"),
            HangulChart.Chart.Count(h => h.Group == "double"),
            HangulChart.Chart.Count(h => h.Group == "vowel"),
            HangulChart.Chart.Count(h => h.Group == "compound")));
        Assert.Contains(HangulChart.Chart, h => h is { Letter: "ㄱ", Syllable: "가", SyllableRomanization: "ga" });
        Assert.Contains(HangulChart.Chart, h => h is { Letter: "ㅎ", Syllable: "하", SyllableRomanization: "ha" });
        Assert.Contains(HangulChart.Chart, h => h is { Letter: "ㅉ", Syllable: "짜", SyllableRomanization: "jja" });
        Assert.Contains(HangulChart.Chart, h => h is { Letter: "ㅓ", Syllable: "어", SyllableRomanization: "eo" });
        Assert.Contains(HangulChart.Chart, h => h is { Letter: "ㅢ", Syllable: "의", SyllableRomanization: "ui" });
    }

    [Fact]
    public void Every_vowel_example_reads_as_its_own_romanization() =>
        Assert.All(
            HangulChart.Chart.Where(h => h.Group is "vowel" or "compound"),
            h => Assert.Equal(h.Romanization, h.SyllableRomanization));

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
