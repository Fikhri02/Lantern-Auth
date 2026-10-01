using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>
/// Per-device till state: the outlet access token and the current cashier, held in memory (spec §6.2, §6.5).
/// Every operation for a device runs under that device's lock, so double taps and second tabs are serialized.
/// </summary>
public sealed class TillSessionService(
    TillRegistrationStore store,
    KeycloakTillClient keycloak,
    IOptions<TillOptions> options,
    TimeProvider time)
{
    private enum OutletCheck { Ok, Unavailable, SignedOut }

    private sealed class CashierSession
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string AccessToken { get; set; }
        public required string RefreshToken { get; set; }
        public DateTimeOffset AccessExpiresAt { get; set; }
        public DateTimeOffset LastActivity { get; set; }
    }

    private sealed class DeviceState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? OutletAccessToken { get; set; }
        public DateTimeOffset OutletExpiresAt { get; set; }
        public CashierSession? Cashier { get; set; }
    }

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new();

    public async Task<TillStatus> GetStatusAsync(string? deviceId, CancellationToken ct = default)
    {
        if (deviceId is null) return TillStatus.NotRegistered;
        return await WithDeviceAsync(deviceId, async (state, registration) =>
        {
            var outlet = await EnsureOutletAsync(registration, state, force: false, ct);
            if (outlet == OutletCheck.SignedOut) return TillStatus.NotRegistered;
            await ExpireIdleCashierAsync(state, ct);
            return Status(registration, state, outlet == OutletCheck.Unavailable);
        }, TillStatus.NotRegistered, ct);
    }

    public async Task<PinResult> SignInCashierAsync(string deviceId, PinAttempt attempt, CancellationToken ct = default) =>
        await WithDeviceAsync(deviceId, async (state, registration) =>
        {
            var outlet = await EnsureOutletAsync(registration, state, force: false, ct);
            if (outlet == OutletCheck.SignedOut) return SignedOut();
            if (outlet == OutletCheck.Unavailable) return Failed(TillMessages.Unavailable);

            var code = attempt.Code.Trim().ToLowerInvariant();
            var (tokens, error) = await keycloak.CashierPinAsync(state.OutletAccessToken!, code, attempt.Pin, attempt.NewPin, ct);
            if (tokens is null)
            {
                if (error == "outlet_session_invalid")
                {
                    await ForgetRegistrationAsync(registration, state, ct);
                    return SignedOut();
                }
                return Failed(error);
            }

            await EndCashierAsync(state, ct);
            var claims = JwtPayload.Read(tokens.AccessToken);
            var now = time.GetUtcNow();
            state.Cashier = new CashierSession
            {
                Code = claims.GetProperty("preferred_username").GetString()!,
                Name = claims.TryGetProperty("name", out var name) ? name.GetString()! : code,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken ?? "",
                AccessExpiresAt = now.AddSeconds(tokens.ExpiresIn),
                LastActivity = now
            };
            return new PinResult(true, null, "", false, false);
        }, SignedOut(), ct);

    public Task LockAsync(string deviceId, CancellationToken ct = default) =>
        WithDeviceAsync(deviceId, async (state, _) =>
        {
            await EndCashierAsync(state, ct);
            return true;
        }, false, ct);

    /// <summary>Spec §6.1: revoke the offline login and delete the registration, freeing the account.</summary>
    public Task SignOutTillAsync(string deviceId, CancellationToken ct = default) =>
        WithDeviceAsync(deviceId, async (state, registration) =>
        {
            await keycloak.RevokeAsync(registration.RefreshToken, ct);
            await ForgetRegistrationAsync(registration, state, ct);
            return true;
        }, false, ct);

    private async Task<T> WithDeviceAsync<T>(string deviceId, Func<DeviceState, TillRegistration, Task<T>> action,
        T whenNotRegistered, CancellationToken ct)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        await state.Gate.WaitAsync(ct);
        try
        {
            var registration = await store.FindAsync(deviceId, ct);
            if (registration is null)
            {
                await EndCashierAsync(state, ct);
                state.OutletAccessToken = null;
                return whenNotRegistered;
            }
            return await action(state, registration);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>Spec §6.5: a failed outlet refresh signs the till out and drops the cashier; unreachable keeps both.</summary>
    private async Task<OutletCheck> EnsureOutletAsync(TillRegistration registration, DeviceState state, bool force, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!force && state.OutletAccessToken is not null && state.OutletExpiresAt - now > TimeSpan.FromSeconds(60))
            return OutletCheck.Ok;

        var (tokens, error) = await keycloak.RefreshAsync(registration.RefreshToken, ct);
        if (tokens is not null)
        {
            state.OutletAccessToken = tokens.AccessToken;
            state.OutletExpiresAt = now.AddSeconds(tokens.ExpiresIn);
            if (tokens.RefreshToken is { } rotated && rotated != registration.RefreshToken)
                await store.UpdateRefreshTokenAsync(registration.DeviceId, rotated, ct);
            return OutletCheck.Ok;
        }

        if (error == TillMessages.Unavailable) return OutletCheck.Unavailable;
        await ForgetRegistrationAsync(registration, state, ct);
        return OutletCheck.SignedOut;
    }

    private async Task ExpireIdleCashierAsync(DeviceState state, CancellationToken ct)
    {
        if (state.Cashier is { } cashier &&
            time.GetUtcNow() - cashier.LastActivity >= TimeSpan.FromMinutes(options.Value.IdleMinutes))
            await EndCashierAsync(state, ct);
    }

    private async Task EndCashierAsync(DeviceState state, CancellationToken ct)
    {
        if (state.Cashier is not { } cashier) return;
        state.Cashier = null;
        if (cashier.RefreshToken.Length > 0) await keycloak.LogoutAsync(cashier.RefreshToken, ct);
    }

    private async Task ForgetRegistrationAsync(TillRegistration registration, DeviceState state, CancellationToken ct)
    {
        await EndCashierAsync(state, ct);
        state.OutletAccessToken = null;
        await store.DeleteAsync(registration.DeviceId, ct);
    }

    private static TillStatus Status(TillRegistration registration, DeviceState state, bool keycloakUnavailable) =>
        new(state.Cashier is null ? TillMode.Locked : TillMode.Active, registration.OutletId, registration.Account,
            state.Cashier is { } c ? new ActiveCashier(c.Code, c.Name) : null, keycloakUnavailable);

    private static PinResult SignedOut() =>
        new(false, "outlet_session_invalid", TillMessages.For("outlet_session_invalid"), false, true);

    private static PinResult Failed(string? error) =>
        new(false, error, TillMessages.For(error), error == "pin_change_required", false);
}
