using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.2–6.4: cashier PIN sign-in at a till with a live outlet login.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class CashierPinFlowTests(KeycloakFixture kc)
{
    [Fact]
    public async Task Seeded_cashier_signs_in_and_token_names_cashier_outlet_and_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var (status, body) = await kc.CashierPinAsync(till.AccessToken, "c-1001", "1111");

        Assert.Equal(HttpStatusCode.OK, status);
        var claims = Jwt.Payload((string)body["access_token"]!);
        Assert.Equal("c-1001", (string?)claims["preferred_username"]);
        Assert.Equal("Siti Aminah", (string?)claims["name"]);
        Assert.Equal("BGS", (string?)claims["outlet_id"]);
        Assert.Equal(till.Account, (string?)claims["till_account"]);
        Assert.Contains("cashier", claims["roles"]!.AsArray().Select(r => (string?)r));
    }

    [Fact]
    public async Task Wrong_pin_reports_remaining_attempts()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var first = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");
        var second = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal(HttpStatusCode.BadRequest, first.Status);
        Assert.Equal("pin_invalid:4", KeycloakFixture.ErrorCode(first.Body));
        Assert.Equal("pin_invalid:3", KeycloakFixture.ErrorCode(second.Body));
    }

    [Fact]
    public async Task Five_wrong_pins_on_one_till_lock_the_cashier_on_another_till()
    {
        var tillA = await kc.OpenTillAsync("/Outlets/Bangsar");
        var tillB = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        for (var i = 0; i < 5; i++) await kc.CashierPinAsync(tillA.AccessToken, cashier, "0000");

        var (status, body) = await kc.CashierPinAsync(tillB.AccessToken, cashier, "2468");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("pin_locked", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Correct_pin_resets_the_failure_count()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        for (var i = 0; i < 4; i++) await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal(HttpStatusCode.OK, (await kc.CashierPinAsync(till.AccessToken, cashier, "2468")).Status);
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal("pin_invalid:4", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Wrong_passwords_on_other_login_pages_do_not_lock_the_cashiers_pin()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        for (var i = 0; i < 6; i++)
        {
            using var attempt = await kc.Http.PostAsync($"{kc.Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = cashier, ["password"] = "guess" + i
                }));
        }
        await Task.Delay(1000); // Keycloak counts password failures asynchronously

        var (status, body) = await kc.CashierPinAsync(till.AccessToken, cashier, "2468");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Empty_pin_is_missing_and_not_counted()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var empty = await kc.CashierPinAsync(till.AccessToken, cashier, "");
        var wrong = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal("pin_missing", KeycloakFixture.ErrorCode(empty.Body));
        Assert.Equal("pin_invalid:4", KeycloakFixture.ErrorCode(wrong.Body));
    }

    [Fact]
    public async Task Cashier_from_another_outlet_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-2001", "3333");
        Assert.Equal("cashier_wrong_outlet", KeycloakFixture.ErrorCode(body));
    }

    [Theory]
    [InlineData("c-9999")]
    [InlineData("eric.procure")]
    [InlineData("")]
    public async Task Unknown_or_non_cashier_user_is_not_found(string username)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, username, "1111");
        Assert.Equal("cashier_not_found", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Disabled_cashier_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (id, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await kc.SetUserEnabledAsync(id, false);

        var (_, body) = await kc.CashierPinAsync(till.AccessToken, cashier, "2468");

        Assert.Equal("cashier_disabled", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Pin_without_a_valid_outlet_login_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var hqToken = await kc.GetUserTokenAsync("eric.procure");

        var missing = await kc.CashierPinAsync(null, "c-1001", "1111");
        var garbage = await kc.CashierPinAsync("not-a-token", "c-1001", "1111");
        var refreshTokenInstead = await kc.CashierPinAsync(till.OfflineRefreshToken, "c-1001", "1111");
        var notADevice = await kc.CashierPinAsync(hqToken, "c-1001", "1111");

        Assert.All(new[] { missing, garbage, refreshTokenInstead, notADevice }, r =>
            Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(r.Body)));
    }

    [Fact]
    public async Task Released_till_cannot_sign_cashiers_in_with_its_unexpired_token()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        Assert.Equal(HttpStatusCode.OK, (await kc.CashierPinAsync(till.AccessToken, "c-1002", "2222")).Status);

        await kc.RevokeTillLoginAsync(till.AccountId);
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-1002", "2222");

        Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Till_client_does_not_accept_plain_passwords()
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = OidcBrowser.TillClientId,
            ["client_secret"] = OidcBrowser.TillClientSecret, ["username"] = "eric.procure",
            ["password"] = KeycloakFixture.DemoPassword
        };
        using var response = await kc.Http.PostAsync($"{kc.Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Temporary_pin_must_be_replaced_by_an_acceptable_pin()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213")).Body));
        Assert.Equal("pin_rule_sequence", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "1234")).Body));
        Assert.Equal("pin_rule_same_as_temporary", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "864213")).Body));

        var changed = await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "2580");
        Assert.Equal(HttpStatusCode.OK, changed.Status);

        Assert.StartsWith("pin_invalid", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213")).Body));
        Assert.Equal(HttpStatusCode.OK, (await kc.CashierPinAsync(till.AccessToken, cashier, "2580")).Status);
    }

    [Fact]
    public async Task Seeded_temporary_cashier_is_asked_to_change_pin()
    {
        var till = await kc.OpenTillAsync("/Outlets/PJ");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-3001", "5555");
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode(body));
    }
}
