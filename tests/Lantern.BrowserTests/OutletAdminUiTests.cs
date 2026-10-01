using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lantern.BrowserTests;

[Collection(StackCollection.Name)]
public sealed class OutletAdminUiTests(StackFixture stack) : BrowserTest(stack)
{
    [Fact]
    public async Task Manager_sees_only_their_outlets_stock()
    {
        var page = await NewPageAsync();
        await page.GotoAsync(StackFixture.OutletAdmin);
        await KeycloakPages.SignInAsync(page, "mgr.bangsar");

        await Expect(page.GetByTestId("outlet")).ToHaveTextAsync("BGS");
        await Expect(page.GetByTestId("stock-SKU-100")).ToContainTextAsync("42");
        await page.GotoAsync($"{StackFixture.OutletAdmin}/?outlet=KLC");
        await Expect(page.GetByTestId("outlet")).ToHaveTextAsync("BGS");
    }

    [Fact]
    public async Task Releasing_a_till_stops_it_from_ringing_the_next_sale()
    {
        // A cashier is mid-sale on a till...
        var (_, account) = await Stack.Admin.CreateUserAsync("/Outlets/Bangsar", "outlet-device");
        var till = await NewPageAsync();
        await till.GotoAsync(StackFixture.Till);
        await till.GetByTestId("register-link").ClickAsync();
        await KeycloakPages.SignInAsync(till, account);
        await till.GetByTestId("cashier-code").FillAsync("c-1002");
        await till.GetByTestId("pin").FillAsync("2222");
        await till.GetByTestId("pin-submit").ClickAsync();
        await till.GetByTestId("add-SKU-200").ClickAsync();

        // ...when the manager releases that till account...
        var manager = await NewPageAsync();
        await manager.GotoAsync($"{StackFixture.OutletAdmin}/tills");
        await KeycloakPages.SignInAsync(manager, "mgr.bangsar");
        await manager.GetByTestId($"release-{account}").ClickAsync();
        await Expect(manager.GetByTestId($"till-{account}")).ToContainTextAsync("Not signed in");

        // ...so the charge doesn't go through and the till asks to be signed in again.
        await till.GetByTestId("charge").ClickAsync();
        await Expect(till.GetByTestId("not-registered")).ToBeVisibleAsync();
    }
}
