using FluentAssertions;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class TieredIngestionParserTests
{
    private const string Structured = "# Heading\n- [ ] an item\n";
    private const string Unstructured = "just a paragraph with no headings or checklist items";

    [Fact]
    public async Task Structured_input_uses_the_free_parser_and_never_calls_the_paid_one()
    {
        var paid = new Mock<IIngestionParser>();
        var tiered = new TieredIngestionParser(new SpecDocumentParser(), paid.Object);

        var result = await tiered.ParseAsync(Structured);

        result.Value!.Should().NotBeEmpty();
        paid.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unstructured_input_escalates_to_the_paid_parser()
    {
        var paid = new Mock<IIngestionParser>();
        paid.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(
                new[] { new TaskDraft("From Claude", null, TaskKind.Generic, string.Empty) }));
        var tiered = new TieredIngestionParser(new SpecDocumentParser(), paid.Object);

        var result = await tiered.ParseAsync(Unstructured);

        result.Value!.Should().ContainSingle(d => d.Title == "From Claude");
        paid.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
