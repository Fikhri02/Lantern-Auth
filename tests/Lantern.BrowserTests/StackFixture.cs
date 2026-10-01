using Microsoft.Playwright;

namespace Lantern.BrowserTests;

/// <summary>The running compose stack, one Chromium, and an admin helper for setting up test data.</summary>
public sealed class StackFixture : IAsyncLifetime
{
    public const string BackOffice = "http://localhost:5200";
    public const string OutletAdmin = "http://localhost:5300";
    public const string Till = "http://localhost:5400";
    public const string Keycloak = "http://localhost:8080";

    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public KeycloakAdmin Admin { get; } = new(Keycloak);

    public async Task InitializeAsync()
    {
        await EnsureStackIsUpAsync();

        var install = Environment.GetEnvironmentVariable("CI") == "true"
            ? new[] { "install", "--with-deps", "chromium" }
            : ["install", "chromium"];
        if (Microsoft.Playwright.Program.Main(install) != 0)
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("HEADED") != "1"
        });
    }

    public static Task EnsureStackIsUpAsync() =>
        EnsureUpAsync([$"{Keycloak}/realms/lantern/.well-known/openid-configuration", $"{Till}/"]);

    /// <summary>Fails in seconds, with the fix, when the stack isn't running (Review Focus 4).</summary>
    public static async Task EnsureUpAsync(IEnumerable<string> urls)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var url in urls)
        {
            try
            {
                using var response = await http.GetAsync(url);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException(
                    $"The Lantern stack isn't running ({url} did not answer). Start it with: docker compose up -d --build --wait", e);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        try
        {
            await Admin.DeleteCreatedUsersAsync();
        }
        finally
        {
            Admin.Dispose();
        }
    }
}
