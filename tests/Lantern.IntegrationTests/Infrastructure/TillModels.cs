namespace Lantern.IntegrationTests.Infrastructure;

public sealed record TokenSet(string AccessToken, string RefreshToken, string IdToken);

public sealed record LoginOutcome(TokenSet? Tokens, int Status, string? Html)
{
    public bool Succeeded => Tokens is not null;
    public static LoginOutcome Success(TokenSet tokens) => new(tokens, 200, null);
    public static LoginOutcome Refused(int status, string html) => new(null, status, html);
}
