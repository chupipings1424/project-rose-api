using Rose.Api.Desktop;

namespace Rose.Api.IntegrationTests;

public sealed class DesktopAppTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5175", "http://127.0.0.1:5175/")]
    [InlineData("http://0.0.0.0:5175;https://0.0.0.0:5176", "http://localhost:5175/")]
    [InlineData("http://*:8080/", "http://localhost:8080/")]
    [InlineData("http://+:8080", "http://localhost:8080/")]
    [InlineData(null, "http://127.0.0.1:5000/")]
    public void BrowserUrl_opens_the_first_address_in_a_browsable_form(string? urls, string expected) =>
        Assert.Equal(expected, DesktopApp.BrowserUrl(urls));

    [Fact]
    public void Normal_builds_do_not_carry_the_ui() => Assert.False(DesktopApp.HasEmbeddedUi);
}