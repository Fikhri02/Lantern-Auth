using System.Text;
using Lantern.Till.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

public sealed class TillRegistrationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lantern-till-store-" + Guid.NewGuid().ToString("N"));

    private TillRegistrationStore Store(string keysFolder = "keys") => new(
        DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, keysFolder)), b => b.SetApplicationName("lantern-till")),
        Options.Create(new TillOptions { DataDirectory = _root }));

    private static TillRegistration Sample(string device = "device-1") =>
        new(device, "outlet-bangsar-1", "BGS", "offline.refresh.token-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Saved_registration_reads_back()
    {
        var store = Store();
        var registration = Sample();

        await store.SaveAsync(registration);
        var found = await store.FindAsync("device-1");

        Assert.Equal(registration.Account, found!.Account);
        Assert.Equal(registration.OutletId, found.OutletId);
        Assert.Equal(registration.RefreshToken, found.RefreshToken);
    }

    [Fact]
    public async Task Refresh_token_is_not_stored_in_plain_text()
    {
        var store = Store();
        var registration = Sample();

        await store.SaveAsync(registration);
        SqliteConnection.ClearAllPools();

        var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(store.DatabasePath));
        Assert.DoesNotContain(registration.RefreshToken, raw);
        Assert.Contains(registration.Account, raw);
    }

    [Fact]
    public async Task Updating_the_refresh_token_replaces_it()
    {
        var store = Store();
        await store.SaveAsync(Sample());

        await store.UpdateRefreshTokenAsync("device-1", "rotated-token");

        Assert.Equal("rotated-token", (await store.FindAsync("device-1"))!.RefreshToken);
    }

    [Fact]
    public async Task Deleted_registration_is_gone()
    {
        var store = Store();
        await store.SaveAsync(Sample());

        await store.DeleteAsync("device-1");

        Assert.Null(await store.FindAsync("device-1"));
    }

    [Fact]
    public async Task Unknown_device_is_not_registered()
    {
        Assert.Null(await Store().FindAsync("nobody"));
    }

    [Fact]
    public async Task Registration_unreadable_after_key_loss_is_treated_as_not_registered()
    {
        await Store("keys-original").SaveAsync(Sample());

        var afterKeyLoss = Store("keys-recreated");

        Assert.Null(await afterKeyLoss.FindAsync("device-1"));
        Assert.Null(await Store("keys-original").FindAsync("device-1")); // the unreadable row was removed
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
