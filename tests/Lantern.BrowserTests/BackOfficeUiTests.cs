using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lantern.BrowserTests;

[Collection(StackCollection.Name)]
public sealed class BackOfficeUiTests(StackFixture stack) : BrowserTest(stack)
{
    private async Task<IPage> SignedInAsync(string username, Totp? totp = null)
    {
        var page = await NewPageAsync();
        await page.GotoAsync(StackFixture.BackOffice);
        await KeycloakPages.SignInAsync(page, username, totp);
        return page;
    }

    [Fact]
    public async Task Admin_adds_a_staff_member_who_appears_in_the_list()
    {
        var (_, admin) = await Stack.Admin.CreateUserAsync("/HQ/Admin");
        var page = await SignedInAsync(admin, new Totp());
        await page.GotoAsync($"{StackFixture.BackOffice}/staff");
        var newUser = $"new.{Guid.NewGuid():N}"[..14];

        await page.GetByTestId("new-username").FillAsync(newUser);
        await page.GetByTestId("new-first").FillAsync("Nadia");
        await page.GetByTestId("new-last").FillAsync("Rahim");
        await page.GetByTestId("new-email").FillAsync($"{newUser}@lantern.test");
        await page.GetByTestId("new-department").SelectOptionAsync("Procurement");
        await page.GetByTestId("create-staff-submit").ClickAsync();

        await Expect(page.GetByTestId("staff-message")).ToContainTextAsync($"Added {newUser}");
        await Expect(page.GetByTestId($"staff-{newUser}")).ToContainTextAsync("procurement");
    }

    [Fact]
    public async Task Deactivating_someone_signs_them_out_of_their_open_back_office()
    {
        var (_, admin) = await Stack.Admin.CreateUserAsync("/HQ/Admin");
        var (_, victim) = await Stack.Admin.CreateUserAsync("/HQ/Marketing");
        var victimPage = await SignedInAsync(victim);
        var adminPage = await SignedInAsync(admin, new Totp());
        await adminPage.GotoAsync($"{StackFixture.BackOffice}/staff");

        await adminPage.GetByTestId($"deactivate-{victim}").ClickAsync();

        await Expect(adminPage.GetByTestId($"staff-{victim}")).ToContainTextAsync("Deactivated");
        await victimPage.WaitForURLAsync(url => url.StartsWith(StackFixture.Keycloak), new() { Timeout = 45_000 });
    }

    [Fact]
    public async Task Trying_a_marketing_action_anyway_shows_the_apis_403()
    {
        var page = await SignedInAsync("chloe.staff");
        await page.GotoAsync($"{StackFixture.BackOffice}/promotions");

        await page.GetByTestId("try-anyway").ClickAsync();

        await Expect(page.GetByTestId("promotion-message")).ToContainTextAsync("403 Forbidden by policy \"Marketing\"");
    }

    [Fact]
    public async Task Marketing_creates_a_promotion()
    {
        var page = await SignedInAsync("dina.marketing");
        await page.GotoAsync($"{StackFixture.BackOffice}/promotions");
        var name = $"Flash sale {Guid.NewGuid():N}"[..18];

        await page.GetByTestId("promotion-name").FillAsync(name);
        await page.GetByTestId("promotion-submit").ClickAsync();

        await Expect(page.GetByTestId("promotions")).ToContainTextAsync(name);
    }

    [Fact]
    public async Task Outlet_manager_is_turned_away_by_keycloak()
    {
        var page = await NewPageAsync();
        await page.GotoAsync(StackFixture.BackOffice);

        await KeycloakPages.SignInAsync(page, "mgr.bangsar");

        await Expect(page.GetByText("Your account doesn't have access to Back Office.")).ToBeVisibleAsync();
    }
}
