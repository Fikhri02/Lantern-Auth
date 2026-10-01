using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lantern.IntegrationTests;

public sealed class HealthTests
{
    [Fact]
    public async Task Health_returns_ok_without_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/health");

        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
