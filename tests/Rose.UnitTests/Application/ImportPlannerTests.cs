using Rose.Application.Importing;

namespace Rose.UnitTests.Application;

public class ImportPlannerTests
{
    private static IEnumerable<DateOnly> Every(int days, int count) =>
        Enumerable.Range(0, count).Select(i => new DateOnly(2024, 1, 15).AddDays(i * days));

    [Fact]
    public void GuessFrequency_recognises_regular_gaps()
    {
        Assert.Equal("Weekly", ImportPlanner.GuessFrequency(Every(7, 5)));
        Assert.Equal("Bi-weekly", ImportPlanner.GuessFrequency(Every(14, 5)));
        Assert.Equal("Monthly", ImportPlanner.GuessFrequency(Enumerable.Range(0, 6).Select(i => new DateOnly(2024, 1, 15).AddMonths(i))));
    }

    [Theory]
    [InlineData("  Due_Date ", "due date")]
    [InlineData("RENT", "rent")]
    public void Key_normalises_headers_like_python(string name, string expected) =>
        Assert.Equal(expected, ImportPlanner.Key(name));

    [Theory]
    [InlineData("replace", "replace")]
    [InlineData("skip", "skip")]
    [InlineData("whatever", "skip")]
    [InlineData(null, "skip")]
    public void NormaliseMode_defaults_to_skip(string? mode, string expected) =>
        Assert.Equal(expected, ImportPlanner.NormaliseMode(mode));
}