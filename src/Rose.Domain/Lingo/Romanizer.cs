namespace Rose.Domain.Lingo;

/// <summary>Latin-letter readings for cards: romaji for Japanese kana, Revised Romanization for Korean.</summary>
public static class Romanizer
{
    /// <summary>The reading, or "" when the language has none or the text cannot be fully converted (e.g. kanji).</summary>
    public static string Romanize(string language, string text) => language switch
    {
        Languages.Japanese => Kana.ToRomaji(text) ?? "",
        Languages.Korean => Hangul.ToRomanization(text) ?? "",
        _ => "",
    };
}
