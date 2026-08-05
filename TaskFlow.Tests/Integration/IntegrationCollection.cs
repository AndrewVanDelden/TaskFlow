namespace TaskFlow.Tests.Integration;

/// <summary>
/// Shares ONE <see cref="TestWebAppFactory"/> (one app + one throwaway DB) across all integration
/// test classes and runs them sequentially. The factory sets process-global environment variables in
/// its constructor, so a single shared instance avoids a race between parallel test classes.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<TestWebAppFactory>;
