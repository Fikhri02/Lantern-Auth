using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Playwright;

namespace Lantern.BrowserTests;

/// <summary>Completes Keycloak's sign-in pages in a real browser, including MFA set-up and challenge.</summary>
public static class KeycloakPages
{
    public static async Task SignInAsync(IPage page, string username, Totp? totp = null, string password = KeycloakAdmin.Password)
    {
        await page.Locator("#username").FillAsync(username);
        await page.Locator("#password").FillAsync(password);
        await page.Locator("#kc-login").ClickAsync();

        var setup = page.Locator("#kc-totp-settings-form");
        var challenge = page.Locator("#kc-otp-login-form");
        var app = page.Locator("[data-testid]");
        var deny = page.Locator(".instruction"); // Keycloak's error and deny pages
        await setup.Or(challenge).Or(app).Or(deny).First.WaitForAsync();

        if (await setup.IsVisibleAsync())
        {
            totp ??= new Totp();
            totp.Secret = await page.Locator("#totpSecret").InputValueAsync();
            await page.Locator("#totp").FillAsync(totp.NextCode());
            await page.Locator("#userLabel").FillAsync("e2e phone");
            await page.Locator("#saveTOTPBtn").ClickAsync();
            await app.Or(deny).First.WaitForAsync();
        }
        else if (await challenge.IsVisibleAsync())
        {
            if (totp?.Secret is null) throw new InvalidOperationException("Keycloak asked for a one-time code but the test has no TOTP secret.");
            await page.Locator("#otp").FillAsync(totp.NextCode());
            await page.Locator("#kc-login").ClickAsync();
            await app.Or(deny).First.WaitForAsync();
        }
    }
}
