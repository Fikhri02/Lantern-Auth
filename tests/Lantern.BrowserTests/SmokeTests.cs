using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lantern.BrowserTests;

[Collection(StackCollection.Name)]
public sealed class SmokeTests(StackFixture stack) : BrowserTest(stack)
{
    [Fact]
    public async Task Back_office_sign_in_shows_the_greeting_from_the_api()
    {
        var page = await NewPageAsync();
        await page.GotoAsync(StackFixture.BackOffice);

        await KeycloakPages.SignInAsync(page, "chloe.staff");

        await Expect(page.GetByTestId("greeting")).ToHaveTextAsync("Hello, Chloe Wong");
    }

    [Fact]
    public async Task Each_app_shows_its_own_login_theme()
    {
        var page = await NewPageAsync();

        await page.GotoAsync(StackFixture.BackOffice);
        await Expect(page.Locator("html")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lantern-hq"));
        await page.GotoAsync(StackFixture.OutletAdmin);
        await Expect(page.Locator("html")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lantern-outlet"));
    }

    [Fact]
    public async Task Missing_stack_is_reported_quickly_with_how_to_start_it()
    {
        var started = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StackFixture.EnsureUpAsync(["http://localhost:5999/"]));

        Assert.Contains("docker compose up -d --build --wait", error.Message);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
