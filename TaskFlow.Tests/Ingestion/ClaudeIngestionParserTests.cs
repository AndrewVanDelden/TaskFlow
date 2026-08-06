using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class ClaudeIngestionParserTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    [Fact]
    public async Task Turns_the_JSON_Claude_returns_into_drafts()
    {
        const string json = "[{\"title\":\"Wire auth\",\"description\":\"JWT login\",\"section\":\"Backend\"}]";
        var claude = StubClaude.ThatReturnsText(json);

        var result = await new ClaudeIngestionParser(claude, Config()).ParseAsync("unstructured document text");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(d =>
            d.Title == "Wire auth" && d.Section == "Backend" && d.Kind == TaskKind.Generic);
    }
}
