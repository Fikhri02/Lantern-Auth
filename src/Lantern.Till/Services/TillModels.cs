namespace Lantern.Till.Services;

/// <summary>A till signed in as an outlet account (spec §6.1). RefreshToken is the offline token.</summary>
public sealed record TillRegistration(string DeviceId, string Account, string OutletId, string RefreshToken, DateTimeOffset RegisteredAt);

public enum TillMode { NotRegistered, Locked, Active }

public sealed record ActiveCashier(string Code, string Name);

public sealed record TillStatus(TillMode Mode, string? OutletId, string? TillAccount, ActiveCashier? Cashier, bool KeycloakUnavailable)
{
    public static readonly TillStatus NotRegistered = new(TillMode.NotRegistered, null, null, null, false);
}

public sealed record PinAttempt(string Code, string Pin, string? NewPin);

public sealed record PinResult(bool Succeeded, string? ErrorCode, string Message, bool ChangePinRequired, bool TillSignedOut);

public sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);

public sealed record SaleItem(string Sku, int Quantity);

public sealed record ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Amount);

public sealed record Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierCode, string CashierName,
    string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At);

public enum SaleOutcome { Ok, Locked, TillSignedOut, Rejected }

public sealed record SaleResult(SaleOutcome Outcome, Receipt? Receipt, string Message);
