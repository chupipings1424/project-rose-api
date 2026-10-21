using Microsoft.ML.Tokenizers;
using Rose.Domain.Lingo;

namespace Rose.Infrastructure.Lingo.Translation;

/// <summary>
/// Text to NLLB token ids and back. NLLB was trained with fairseq, whose ids are the
/// SentencePiece ids shifted by one: fairseq puts &lt;s&gt; &lt;pad&gt; &lt;/s&gt; &lt;unk&gt; at 0-3, then
/// SentencePiece piece n at n + 1. The 202 language tags ("eng_Latn"...) come right after the pieces.
/// This mirrors Hugging Face's NllbTokenizer.
/// </summary>
internal sealed class NllbVocabulary
{
    public const long Bos = 0;
    public const long Pad = 1;
    public const long Eos = 2;
    public const long Unk = 3;
    private const int FairseqOffset = 1;

    // Each language's position in fairseq's FAIRSEQ_LANGUAGE_CODES list (202 codes, alphabetical by group).
    private static readonly Dictionary<string, (string Tag, int Index)> Tags = new(StringComparer.Ordinal)
    {
        [Languages.German] = ("deu_Latn", 41),
        [Languages.English] = ("eng_Latn", 46),
        [Languages.Japanese] = ("jpn_Jpan", 78),
        [Languages.Korean] = ("kor_Hang", 97),
        [Languages.Tagalog] = ("tgl_Latn", 173),
        [Languages.ChineseSimplified] = ("zho_Hans", 199),
        [Languages.ChineseTraditional] = ("zho_Hant", 200),
    };

    private readonly SentencePieceTokenizer _pieces;
    private readonly int _pieceCount;

    private NllbVocabulary(SentencePieceTokenizer pieces)
    {
        _pieces = pieces;
        _pieceCount = pieces.Vocabulary.Count;
    }

    public static NllbVocabulary Load(string sentencePieceModel)
    {
        using var stream = File.OpenRead(sentencePieceModel);
        return new NllbVocabulary(SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false));
    }

    /// <summary>"eng_Latn" for "en": the tag NLLB uses.</summary>
    public static string Tag(string language) => Tags[language].Tag;

    /// <summary>The id of a language tag, e.g. 256047 for English with the real 256,000-piece model.</summary>
    public long LanguageId(string language) => LanguageId(_pieceCount, language);

    internal static long LanguageId(int pieceCount, string language) => pieceCount + FairseqOffset + Tags[language].Index;

    internal static bool HasTag(string language) => Tags.ContainsKey(language);

    /// <summary>The encoder input: [source tag] pieces... [&lt;/s&gt;].</summary>
    public List<long> EncodeSource(string text, string language)
    {
        var ids = new List<long> { LanguageId(language) };
        foreach (var piece in _pieces.EncodeToIds(text, addBeginningOfSentence: false, addEndOfSentence: false))
        {
            var id = ToModelId(piece);
            // SentencePiece merges a run of unknown characters into one <unk>; Microsoft.ML.Tokenizers doesn't.
            if (id == Unk && ids[^1] == Unk)
                continue;
            ids.Add(id);
        }
        ids.Add(Eos);
        return ids;
    }

    /// <summary>What the decoder is given first: &lt;/s&gt; then the target tag (forced first token).</summary>
    public List<long> DecoderPrefix(string language) => [Eos, LanguageId(language)];

    /// <summary>Model ids back to text; special tokens and language tags are skipped.</summary>
    public string Decode(IEnumerable<long> ids) =>
        _pieces.Decode(ids.Where(IsPiece).Select(id => (int)(id - FairseqOffset)), considerSpecialTokens: false) ?? "";

    internal static long ToModelId(int piece) => piece == 0 ? Unk : piece + FairseqOffset;

    private bool IsPiece(long id) => id > Unk && id - FairseqOffset < _pieceCount;
}
