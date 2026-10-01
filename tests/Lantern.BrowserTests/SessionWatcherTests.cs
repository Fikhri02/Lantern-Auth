using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lantern.BrowserTests;

/// <summary>Spec §5.2 step 4: an open tab shows the sign-in page within ~30 s of signing out elsewhere.</summary>
[Collection(StackCollection.Name)]
public sealed class SessionWatcherTests(StackFixture stack) : BrowserTest(stack)
{
    [Fact]
    public async Task Outlet_admin_tab_leaves_for_sign_in_after_signing_out_of_back_office()
    {
        var (_, admin) = await Stack.Admin.CreateUserAsync("/HQ/Admin");
        var backOffice = await NewPageAsync();
        await backOffice.GotoAsync(StackFixture.BackOffice);
        await KeycloakPages.SignInAsync(backOffice, admin, new Totp());
        var outletAdmin = await backOffice.Context.NewPageAsync(); // same browser, second tab
        await outletAdmin.GotoAsync(StackFixture.OutletAdmin);
        await Expect(outletAdmin.GetByTestId("role-hq-admin")).ToBeVisibleAsync();

        await backOffice.GetByTestId("sign-out").ClickAsync();

        // Nobody touches the Outlet Admin tab: revalidation (30 s) plus the watcher must move it.
        await outletAdmin.WaitForURLAsync(url => url.StartsWith(StackFixture.Keycloak), new() { Timeout = 45_000 });
    }
}
