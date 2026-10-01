namespace Lantern.Till.Services;

/// <summary>Maps Keycloak's till error codes to what the cashier reads (spec §7). Never shows a raw code.</summary>
public static class TillMessages
{
    public const string Unavailable = "keycloak_unavailable";

    public static string For(string? code) => code switch
    {
        null or "" => "",
        "pin_missing" => "Enter your PIN.",
        "pin_locked" => "Too many wrong PINs. Try again in 15 minutes or ask a manager.",
        "pin_change_required" => "Choose a new PIN to replace your temporary one.",
        "pin_rule_format" => "Use 4 to 6 digits.",
        "pin_rule_same_as_temporary" => "Your new PIN can't be the temporary one.",
        "pin_rule_repeated_digit" => "Don't use the same digit throughout.",
        "pin_rule_sequence" => "Don't use a simple sequence like 1234.",
        "cashier_not_found" => "That cashier code isn't recognised.",
        "cashier_wrong_outlet" => "This cashier isn't assigned to this outlet.",
        "cashier_disabled" => "Your account has been deactivated.",
        "outlet_session_invalid" => "This till has been signed out. Sign in with the till account again.",
        Unavailable => "Sign-in is temporarily unavailable.",
        _ when code.StartsWith("pin_invalid:", StringComparison.Ordinal)
               && int.TryParse(code["pin_invalid:".Length..], out var left) => left switch
        {
            0 => "Wrong PIN. No tries left.",
            1 => "Wrong PIN. 1 try left.",
            _ => $"Wrong PIN. {left} tries left."
        },
        _ => "Sign-in failed. Try again."
    };
}
