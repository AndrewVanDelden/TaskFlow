using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

// Epic 3 Pre-Merge Code Review, finding 6.1: ClaudeClient's IsConfigured/null-API-key branch had
// no test coverage anywhere, despite every agent gating its cycle on it.
public class ClaudeClientTests
{
    private static IConfiguration ConfigWithKey(string? apiKey) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = apiKey
        }).Build();

    [Fact]
    public void IsConfigured_is_true_when_an_api_key_is_present()
    {
        var client = new ClaudeClient(ConfigWithKey("sk-test-key"));

        client.IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_is_false_when_the_api_key_is_missing_or_blank(string? apiKey)
    {
        var client = new ClaudeClient(ConfigWithKey(apiKey));

        client.IsConfigured.Should().BeFalse();
    }
}
