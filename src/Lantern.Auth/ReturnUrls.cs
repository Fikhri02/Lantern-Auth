namespace Lantern.Auth;

public static class ReturnUrls
{
    /// <summary>
    /// A same-site path, or "/". Rejects protocol-relative ("//", "/\") forms, backslashes anywhere, and
    /// control characters, which browsers strip ("/\t/evil" becomes "//evil").
    /// </summary>
    public static string LocalOnly(string? returnUrl) =>
        returnUrl is { Length: > 0 }
        && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || returnUrl[1] != '/')
        && !returnUrl.Contains('\\')
        && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";
}
