using Rose.Domain.Common;
using Rose.Domain.Lingo;

namespace Rose.UnitTests.Domain;

public sealed class LanguagesTests
{
    [Theory]
    [InlineData(" JA ", "ja")]
    [InlineData("tl", "tl")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("ZH-HANT", "zh-Hant")]
    public void Codes_are_matched_without_case_and_returned_in_their_canonical_form(string input, string expected) =>
        Assert.Equal(expected, Languages.Require(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zh")]
    [InlineData("fr")]
    public void Unknown_codes_are_a_validation_error(string? input) =>
        Assert.Throws<ValidationException>(() => Languages.Require(input));

    [Fact]
    public void No_code_is_longer_than_the_database_column() =>
        Assert.All(Languages.All, l => Assert.InRange(l.Code.Length, 2, Languages.MaxCodeLength));

    [Fact]
    public void Every_language_but_english_has_a_starter_deck() =>
        Assert.Equal(
            Languages.All.Select(l => l.Code).Where(c => c != Languages.English).Order(StringComparer.Ordinal),
            StarterDecks.All.Keys.Order(StringComparer.Ordinal));
}
