using Rose.Domain.Common;

namespace Rose.UnitTests.Domain;

public class CoerceTests
{
    [Theory]
    [InlineData("1,250.50", 1250.5)]
    [InlineData("$99", 99)]
    [InlineData(" 12.5 ", 12.5)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("NaN", 0)]
    [InlineData("Infinity", 0)]
    public void ToDouble_reads_loose_text(string? input, double expected) =>
        Assert.Equal(expected, Coerce.ToDouble(input));

    [Theory]
    [InlineData("2024-01-05", "2024-01-05")]
    [InlineData("2024-1-5", "2024-01-05")]
    [InlineData("1/31/2024", "2024-01-31")]
    [InlineData("31/1/2024", "2024-01-31")]
    [InlineData("Wednesday, January 31, 2024", "2024-01-31")]
    [InlineData("not a date", "")]
    [InlineData("", "")]
    public void Iso_accepts_the_formats_people_type(string input, string expected) =>
        Assert.Equal(expected, Coerce.Iso(input));

    [Theory]
    [InlineData("09:05", "9:05am")]
    [InlineData("13:30", "1:30pm")]
    [InlineData("00:00", "12:00am")]
    [InlineData("12:15", "12:15pm")]
    [InlineData("1:30 PM", "1:30pm")]
    [InlineData("whenever", "whenever")]
    public void ToTimeText_formats_like_python(string input, string expected) =>
        Assert.Equal(expected, Coerce.ToTimeText(input));

    [Fact]
    public void ToTimeText_reads_excel_time_values() =>
        Assert.Equal("6:45pm", Coerce.ToTimeText(new TimeSpan(18, 45, 0)));

    [Theory]
    [InlineData(3.0, "3")]
    [InlineData(2.5, "2.5")]
    [InlineData(true, "True")]
    public void ToText_matches_python_str(object input, string expected) =>
        Assert.Equal(expected, Coerce.ToText(input));

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12.9", 12)]
    [InlineData("x", -1)]
    [InlineData("", -1)]
    public void ToInt_truncates_or_falls_back(string input, int expected) =>
        Assert.Equal(expected, Coerce.ToInt(input, fallback: -1));

    [Theory]
    [InlineData(0.125, 0.12)]
    [InlineData(0.135, 0.14)]
    public void Round_uses_bankers_rounding_like_python(double input, double expected) =>
        Assert.Equal(expected, Coerce.Round(input));

    [Fact]
    public void TitleCase_matches_python_title() =>
        Assert.Equal("Car Loan 2Nd Plan", Coerce.TitleCase("car LOAN 2nd plan"));
}