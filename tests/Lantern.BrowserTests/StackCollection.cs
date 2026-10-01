namespace Lantern.BrowserTests;

/// <summary>One collection: browser tests share one stack, so they run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<StackFixture>
{
    public const string Name = "stack";
}
