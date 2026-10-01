using System.Runtime.CompilerServices;
using Microsoft.Playwright;

namespace Lantern.BrowserTests;

/// <summary>Gives each test fresh browser contexts (separate cookies, like separate browsers), traced to TestResults.</summary>
public abstract class BrowserTest(StackFixture stack) : IAsyncLifetime
{
    private readonly List<(IBrowserContext Context, string Name)> _contexts = [];

    protected StackFixture Stack { get; } = stack;

    /// <summary>A new page in a new context: a separate browser as far as cookies go.</summary>
    protected async Task<IPage> NewPageAsync([CallerMemberName] string test = "")
    {
        var context = await Stack.Browser.NewContextAsync(new BrowserNewContextOptions { Locale = "en-GB" });
        context.SetDefaultTimeout(15_000);
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        _contexts.Add((context, $"{GetType().Name}.{test}.{_contexts.Count}"));
        return await context.NewPageAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var (context, name) in _contexts)
        {
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine("TestResults", "playwright", $"{name}.zip")
            });
            await context.DisposeAsync();
        }
    }
}
