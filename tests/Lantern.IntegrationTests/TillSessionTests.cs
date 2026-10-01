using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.2 and §6.5, from the till's side: cashier PIN sign-in, idle lock, sign out.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillSessionTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir, s => s.AddSingleton<TimeProvider>(_time));
        return Task.CompletedTask;
    }

    private async Task<(string DeviceId, string AccountId, string Account)> RegisterAsync(string outletGroup = "/Outlets/Bangsar")
    {
        var (accountId, account) = await kc.CreateTempTillAsync(outletGroup);
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        return (browser.DeviceId!, accountId, account);
    }

    [Fact]
    public async Task Unknown_device_is_not_registered()
    {
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync("no-such-device")).Mode);
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(null)).Mode);
    }

    [Fact]
    public async Task Registered_till_waits_on_the_pin_pad()
    {
        var (device, _, account) = await RegisterAsync();

        var status = await _till.Session.GetStatusAsync(device);

        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(account, status.TillAccount);
        Assert.Equal("BGS", status.OutletId);
    }

    [Fact]
    public async Task Cashier_signs_in_and_the_till_shows_who_is_serving()
    {
        var (device, _, _) = await RegisterAsync();

        var result = await _till.Session.SignInCashierAsync(device, new PinAttempt("c-1001", "1111", null));

        Assert.True(result.Succeeded);
        var status = await _till.Session.GetStatusAsync(device);
        Assert.Equal(TillMode.Active, status.Mode);
        Assert.Equal(new ActiveCashier("c-1001", "Siti Aminah"), status.Cashier);
    }

    [Fact]
    public async Task Cashier_code_with_spaces_and_capitals_still_works()
    {
        var (device, _, _) = await RegisterAsync();
        Assert.True((await _till.Session.SignInCashierAsync(device, new PinAttempt("  C-1002 ", "2222", null))).Succeeded);
    }

    [Fact]
    public async Task Wrong_pin_reports_tries_left_in_plain_words()
    {
        var (device, _, _) = await RegisterAsync();
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var result = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "0000", null));

        Assert.False(result.Succeeded);
        Assert.Equal("Wrong PIN. 4 tries left.", result.Message);
    }

    [Fact]
    public async Task Temporary_pin_asks_for_a_new_one_then_signs_in()
    {
        var (device, _, _) = await RegisterAsync();
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        var first = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "864213", null));
        var changed = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "864213", "2580"));

        Assert.True(first.ChangePinRequired);
        Assert.True(changed.Succeeded);
    }

    [Fact]
    public async Task Next_cashier_replaces_the_current_one_and_ends_their_session()
    {
        var (device, _, _) = await RegisterAsync();
        var (firstId, first) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        var (_, second) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "1357");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(first, "2468", null));

        await _till.Session.SignInCashierAsync(device, new PinAttempt(second, "1357", null));

        Assert.Equal(second, (await _till.Session.GetStatusAsync(device)).Cashier!.Code);
        Assert.Equal(0, await kc.UserSessionCountAsync(firstId));
    }

    [Fact]
    public async Task Two_pin_submissions_at_once_leave_exactly_one_cashier()
    {
        var (device, _, _) = await RegisterAsync();
        var (aId, a) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        var (bId, b) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "1357");

        await Task.WhenAll(
            _till.Session.SignInCashierAsync(device, new PinAttempt(a, "2468", null)),
            _till.Session.SignInCashierAsync(device, new PinAttempt(b, "1357", null)));

        var active = (await _till.Session.GetStatusAsync(device)).Cashier!.Code;
        var otherId = active == a ? bId : aId;
        Assert.Contains(active, new[] { a, b });
        Assert.Equal(0, await kc.UserSessionCountAsync(otherId));
    }

    [Fact]
    public async Task Idle_cashier_is_locked_out_after_ten_minutes()
    {
        var (device, _, _) = await RegisterAsync();
        var (cashierId, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "2468", null));

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var status = await _till.Session.GetStatusAsync(device);

        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(0, await kc.UserSessionCountAsync(cashierId));
    }

    [Fact]
    public async Task Lock_ends_the_cashier_session()
    {
        var (device, _, _) = await RegisterAsync();
        var (cashierId, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "2468", null));

        await _till.Session.LockAsync(device);

        Assert.Equal(TillMode.Locked, (await _till.Session.GetStatusAsync(device)).Mode);
        Assert.Equal(0, await kc.UserSessionCountAsync(cashierId));
    }

    [Fact]
    public async Task Released_till_shows_as_signed_out_and_forgets_its_registration()
    {
        var (device, accountId, _) = await RegisterAsync();

        await kc.RevokeTillLoginAsync(accountId);
        _time.Advance(TimeSpan.FromMinutes(6)); // outlet access token now expired, forcing a refresh

        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(device)).Mode);
        Assert.Null(await _till.Store.FindAsync(device));
    }

    [Fact]
    public async Task Sign_out_this_till_frees_the_account_for_another_till()
    {
        var (device, _, account) = await RegisterAsync();

        await _till.Session.SignOutTillAsync(device);

        Assert.Null(await _till.Store.FindAsync(device));
        await kc.OutletLoginAsync(account); // would throw if the account were still taken
    }

    [Fact]
    public async Task Unreachable_keycloak_keeps_the_registration_and_says_so()
    {
        var (device, _, account) = await RegisterAsync();
        await using var offline = new TillFactory(kc, _dataDir, s =>
            s.AddHttpClient("keycloak").ConfigurePrimaryHttpMessageHandler(() => new UnreachableHandler()));

        var status = await offline.Session.GetStatusAsync(device);

        Assert.True(status.KeycloakUnavailable);
        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(account, (await offline.Store.FindAsync(device))!.Account);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
