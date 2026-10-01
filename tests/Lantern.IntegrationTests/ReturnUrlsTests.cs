using Lantern.Auth;

namespace Lantern.IntegrationTests;

public sealed class ReturnUrlsTests
{
    [Theory]
    [InlineData("/staff", "/staff")]
    [InlineData("/promotions?x=1", "/promotions?x=1")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/\t/evil.example", "/")]
    [InlineData("/\n/evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/ok\\..\\evil", "/")]
    public void Only_plain_local_paths_survive(string? returnUrl, string expected) =>
        Assert.Equal(expected, ReturnUrls.LocalOnly(returnUrl));
}
