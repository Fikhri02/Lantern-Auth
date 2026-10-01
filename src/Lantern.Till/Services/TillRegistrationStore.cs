using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>
/// Till registrations in SQLite on the data volume. The offline refresh token is encrypted with Data
/// Protection; if the keys are lost the row can't be read, and the till asks to be signed in again.
/// </summary>
public sealed class TillRegistrationStore
{
    private readonly IDataProtector _protector;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public TillRegistrationStore(IDataProtectionProvider dataProtection, IOptions<TillOptions> options)
    {
        _protector = dataProtection.CreateProtector("Lantern.Till.Registration.v1");
        Directory.CreateDirectory(options.Value.DataDirectory);
        DatabasePath = Path.Combine(options.Value.DataDirectory, "till.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

    public async Task SaveAsync(TillRegistration registration, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO registrations (device_id, account, outlet_id, refresh_token, registered_at)
            VALUES ($device, $account, $outlet, $token, $at)
            """;
        cmd.Parameters.AddWithValue("$device", registration.DeviceId);
        cmd.Parameters.AddWithValue("$account", registration.Account);
        cmd.Parameters.AddWithValue("$outlet", registration.OutletId);
        cmd.Parameters.AddWithValue("$token", _protector.Protect(registration.RefreshToken));
        cmd.Parameters.AddWithValue("$at", registration.RegisteredAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TillRegistration?> FindAsync(string deviceId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT account, outlet_id, refresh_token, registered_at FROM registrations WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$device", deviceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var (account, outletId, protectedToken, registeredAt) =
            (reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)));
        await reader.DisposeAsync();
        try
        {
            return new TillRegistration(deviceId, account, outletId, _protector.Unprotect(protectedToken), registeredAt);
        }
        catch (CryptographicException)
        {
            await DeleteAsync(deviceId, ct);
            return null;
        }
    }

    public async Task UpdateRefreshTokenAsync(string deviceId, string refreshToken, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE registrations SET refresh_token = $token WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$token", _protector.Protect(refreshToken));
        cmd.Parameters.AddWithValue("$device", deviceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string deviceId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM registrations WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$device", deviceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        if (_schemaReady) return db;

        await _schemaLock.WaitAsync(ct);
        try
        {
            var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS registrations (
                    device_id     TEXT PRIMARY KEY,
                    account       TEXT NOT NULL,
                    outlet_id     TEXT NOT NULL,
                    refresh_token TEXT NOT NULL,
                    registered_at TEXT NOT NULL)
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
        return db;
    }
}
