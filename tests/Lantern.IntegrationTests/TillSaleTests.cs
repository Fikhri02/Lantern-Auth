using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class TillSaleTests(KeycloakFixture kc) : IAsyncLifetime
{
    private static readonly SaleItem[] TwoCoffees = [new("SKU-100", 2)];
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir, s => s.AddSingleton<TimeProvider>(_time));
        return Task.CompletedTask;
    }

    private async Task<(string DeviceId, string AccountId, string Account)> RegisterWithCashierAsync(string code = "c-1001", string pin = "1111")
    {
        var (accountId, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        Assert.True((await _till.Session.SignInCashierAsync(browser.DeviceId!, new PinAttempt(code, pin, null))).Succeeded);
        return (browser.DeviceId!, accountId, account);
    }

    [Fact]
    public async Task Sale_receipt_names_the_cashier_and_this_till()
    {
        var (device, _, account) = await RegisterWithCashierAsync();

        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Ok, result.Outcome);
        Assert.Equal("Siti Aminah (c-1001)", result.Receipt!.ServedBy);
        Assert.Equal(account, result.Receipt.TillAccount);
        Assert.Equal(90.00m, result.Receipt.Total);
    }

    [Fact]
    public async Task Released_till_drops_the_cashier_before_the_next_sale()
    {
        var (device, accountId, _) = await RegisterWithCashierAsync("c-1002", "2222");

        await kc.RevokeTillLoginAsync(accountId); // the outlet access token is still unexpired

        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);
        Assert.Equal(SaleOutcome.TillSignedOut, result.Outcome);
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(device)).Mode);
    }

    [Fact]
    public async Task Sale_after_the_idle_window_is_refused_even_without_a_poll()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Locked, result.Outcome);
    }

    [Fact]
    public async Task Sale_on_a_locked_till_is_refused()
    {
        var (device, _, _) = await RegisterWithCashierAsync();
        await _till.Session.LockAsync(device);

        Assert.Equal(SaleOutcome.Locked, (await _till.Session.RingSaleAsync(device, TwoCoffees)).Outcome);
    }

    [Fact]
    public async Task Sales_keep_working_past_the_cashier_access_token_lifetime()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        _time.Advance(TimeSpan.FromMinutes(6)); // cashier access token (5 min) expired, but within the idle window
        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Invalid_sale_is_rejected_with_a_message()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        var result = await _till.Session.RingSaleAsync(device, [new SaleItem("SKU-999", 1)]);

        Assert.Equal(SaleOutcome.Rejected, result.Outcome);
        Assert.Equal("That sale couldn't be recorded. Check the items and try again.", result.Message);
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
