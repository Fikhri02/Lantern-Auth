using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lantern.BrowserTests;

[Collection(StackCollection.Name)]
public sealed class TillUiTests(StackFixture stack) : BrowserTest(stack)
{
    /// <summary>A brand-new till account in Bangsar, signed in on a fresh browser: the till's one-time setup.</summary>
    private async Task<(IPage Page, string Account, string AccountId)> RegisteredTillAsync([CallerMemberName] string test = "")
    {
        var (id, account) = await Stack.Admin.CreateUserAsync("/Outlets/Bangsar", "outlet-device");
        var page = await NewPageAsync(test);
        await page.GotoAsync(StackFixture.Till);
        await page.GetByTestId("register-link").ClickAsync();
        await KeycloakPages.SignInAsync(page, account);
        await Expect(page.GetByTestId("pin-pad")).ToBeVisibleAsync();
        return (page, account, id);
    }

    private static async Task SignInCashierAsync(IPage page, string code, string pin)
    {
        await page.GetByTestId("cashier-code").FillAsync(code);
        await page.GetByTestId("pin").FillAsync(pin);
        await page.GetByTestId("pin-submit").ClickAsync();
    }

    [Fact]
    public async Task Cashier_rings_a_sale_and_the_receipt_names_them_and_the_till()
    {
        var (page, account, _) = await RegisteredTillAsync();

        await SignInCashierAsync(page, "c-1001", "1111");
        await Expect(page.GetByTestId("cashier")).ToHaveTextAsync("Siti Aminah (c-1001)");
        await page.GetByTestId("add-SKU-100").ClickAsync();
        await page.GetByTestId("add-SKU-100").ClickAsync();
        await page.GetByTestId("charge").ClickAsync();

        var receipt = page.GetByTestId("receipt");
        await Expect(receipt).ToContainTextAsync("Served by: Siti Aminah (c-1001)");
        await Expect(receipt).ToContainTextAsync(account);
        await Expect(receipt).ToContainTextAsync("90.00");
    }

    [Fact]
    public async Task Wrong_pin_shows_how_many_tries_are_left()
    {
        var (page, _, _) = await RegisteredTillAsync();
        var (cashierId, cashier) = await Stack.Admin.CreateUserAsync("/Outlets/Bangsar", "cashier");
        await Stack.Admin.SetPinAsync(cashierId, "2468", temporary: false);

        await SignInCashierAsync(page, cashier, "0000");

        await Expect(page.GetByTestId("pin-message")).ToHaveTextAsync("Wrong PIN. 4 tries left.");
    }

    [Fact]
    public async Task Cashier_with_a_temporary_pin_chooses_their_own()
    {
        var (page, _, _) = await RegisteredTillAsync();
        var (cashierId, cashier) = await Stack.Admin.CreateUserAsync("/Outlets/Bangsar", "cashier");
        await Stack.Admin.SetPinAsync(cashierId, "864213", temporary: true);

        await SignInCashierAsync(page, cashier, "864213");
        await Expect(page.GetByTestId("new-pin")).ToBeVisibleAsync();
        await page.GetByTestId("new-pin").FillAsync("2580");
        await page.GetByTestId("pin-submit").ClickAsync();

        await Expect(page.GetByTestId("sale")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_second_till_with_the_same_account_is_refused()
    {
        var (_, account, _) = await RegisteredTillAsync();
        var secondTill = await NewPageAsync();
        await secondTill.GotoAsync(StackFixture.Till);
        await secondTill.GetByTestId("register-link").ClickAsync();

        await KeycloakPages.SignInAsync(secondTill, account);

        await Expect(secondTill.GetByText("already signed in on another till")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Till_stays_signed_in_after_a_reload_and_signs_out_on_request()
    {
        var (page, _, _) = await RegisteredTillAsync();

        await page.ReloadAsync();
        await Expect(page.GetByTestId("pin-pad")).ToBeVisibleAsync();
        await page.GetByTestId("sign-out-till").ClickAsync();

        await Expect(page.GetByTestId("not-registered")).ToBeVisibleAsync();
    }
}
