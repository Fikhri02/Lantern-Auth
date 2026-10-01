using Lantern.Till.Services;

namespace Lantern.IntegrationTests;

public sealed class TillMessagesTests
{
    [Theory]
    [InlineData("pin_invalid:4", "Wrong PIN. 4 tries left.")]
    [InlineData("pin_invalid:1", "Wrong PIN. 1 try left.")]
    [InlineData("pin_invalid:0", "Wrong PIN. No tries left.")]
    [InlineData("pin_locked", "Too many wrong PINs. Try again in 15 minutes or ask a manager.")]
    [InlineData("pin_missing", "Enter your PIN.")]
    [InlineData("pin_change_required", "Choose a new PIN to replace your temporary one.")]
    [InlineData("pin_rule_format", "Use 4 to 6 digits.")]
    [InlineData("pin_rule_same_as_temporary", "Your new PIN can't be the temporary one.")]
    [InlineData("pin_rule_repeated_digit", "Don't use the same digit throughout.")]
    [InlineData("pin_rule_sequence", "Don't use a simple sequence like 1234.")]
    [InlineData("cashier_not_found", "That cashier code isn't recognised.")]
    [InlineData("cashier_wrong_outlet", "This cashier isn't assigned to this outlet.")]
    [InlineData("cashier_disabled", "Your account has been deactivated.")]
    [InlineData("outlet_session_invalid", "This till has been signed out. Sign in with the till account again.")]
    [InlineData("keycloak_unavailable", "Sign-in is temporarily unavailable.")]
    [InlineData("something_new", "Sign-in failed. Try again.")]
    [InlineData("pin_invalid:x", "Sign-in failed. Try again.")]
    public void Codes_map_to_plain_messages(string code, string expected) =>
        Assert.Equal(expected, TillMessages.For(code));
}
