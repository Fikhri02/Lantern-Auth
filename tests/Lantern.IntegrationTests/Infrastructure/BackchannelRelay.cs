using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// Keycloak runs in a container and can't reach the in-memory apps. It posts back-channel logout calls here
/// (host.docker.internal:{Port}/{clientId}); the relay forwards them to whichever in-memory app routed itself.
/// </summary>
public sealed class BackchannelRelay : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, HttpMessageHandler> _routes = new();
    private WebApplication? _app;

    public int Port { get; private set; }

    public void Route(string clientId, HttpMessageHandler handler) => _routes[clientId] = handler;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://0.0.0.0:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.MapPost("/{clientId}", async (string clientId, HttpRequest request) =>
        {
            if (!_routes.TryGetValue(clientId, out var handler)) return Results.StatusCode(503);
            var form = await request.ReadFormAsync();
            using var forward = new HttpRequestMessage(HttpMethod.Post, "http://relayed/bff/backchannel-logout")
            {
                Content = new FormUrlEncodedContent(form.ToDictionary(f => f.Key, f => f.Value.ToString()))
            };
            using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
            using var response = await invoker.SendAsync(forward, CancellationToken.None);
            return Results.StatusCode((int)response.StatusCode);
        });
        await _app.StartAsync();
        Port = new Uri(_app.Urls.First()).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }
}
