using System.Text.Json;
using Rose.Domain.Lingo;
using Rose.Infrastructure.Lingo.Translation;

namespace Rose.UnitTests.Infrastructure;

public sealed class NllbVocabularyTests
{
    internal static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nllb");

    public static TheoryData<string, string, long[], string> Cases()
    {
        // expected.json holds what Python's sentencepiece + Hugging Face's id rules give for the same model.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")));
        var data = new TheoryData<string, string, long[], string>();
        foreach (var c in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(
                c.GetProperty("text").GetString()!,
                c.GetProperty("tag").GetString()!,
                c.GetProperty("ids").EnumerateArray().Select(i => i.GetInt64()).ToArray(),
                c.GetProperty("decoded").GetString()!);
        }
        return data;
    }

    private static readonly Dictionary<string, string> CodeByTag = new()
    {
        ["eng_Latn"] = Languages.English,
        ["tgl_Latn"] = Languages.Tagalog,
        ["deu_Latn"] = Languages.German,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Encoding_matches_python_sentencepiece_with_the_fairseq_shift(string text, string tag, long[] expected, string decoded)
    {
        var vocabulary = NllbVocabulary.Load(Path.Combine(Fixtures, "sentencepiece.bpe.model"));

        Assert.Equal(expected, vocabulary.EncodeSource(text, CodeByTag[tag]));
        // Unknown characters (id 3) cannot come back, so only clean text round-trips exactly.
        if (!decoded.Contains('\u2047')) Assert.Equal(text, vocabulary.Decode(expected));
    }

    [Theory]
    [InlineData(Languages.English, 256047)]
    [InlineData(Languages.Tagalog, 256174)]
    [InlineData(Languages.Japanese, 256079)]
    [InlineData(Languages.Korean, 256098)]
    [InlineData(Languages.ChineseSimplified, 256200)]
    [InlineData(Languages.ChineseTraditional, 256201)]
    [InlineData(Languages.German, 256042)]
    public void Language_tags_have_the_real_nllb_ids(string language, long id) =>
        Assert.Equal(id, NllbVocabulary.LanguageId(256_000, language));

    [Fact]
    public void Every_language_has_an_nllb_tag() =>
        Assert.All(Languages.All, l => Assert.True(NllbVocabulary.HasTag(l.Code), l.Code));

    [Fact]
    public void Decoding_skips_special_tokens_and_language_tags()
    {
        var vocabulary = NllbVocabulary.Load(Path.Combine(Fixtures, "sentencepiece.bpe.model"));
        var hello = vocabulary.EncodeSource("Hello", Languages.English);

        var noisy = new List<long> { NllbVocabulary.Bos, vocabulary.LanguageId(Languages.Tagalog) };
        noisy.AddRange(hello);
        noisy.Add(NllbVocabulary.Pad);

        Assert.Equal("Hello", vocabulary.Decode(noisy));
        Assert.Equal([NllbVocabulary.Eos, vocabulary.LanguageId(Languages.Japanese)], vocabulary.DecoderPrefix(Languages.Japanese));
    }
}
