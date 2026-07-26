using FluentAssertions;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class SpecDocumentParserTests
{
    // Two headings + three checklist items = five drafts.
    private const string Doc =
        "# Set up auth\n" +
        "Add JWT login and registration.\n" +
        "\n" +
        "- [ ] Create the login endpoint\n" +
        "- [ ] Protect the task routes\n" +
        "\n" +
        "# Build the board\n" +
        "- [ ] Render the columns\n";

    [Fact]
    public void Parses_one_draft_per_heading_and_per_checklist_item()
    {
        var result = new SpecDocumentParser().Parse(Doc);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(5);
    }

    [Fact]
    public void Heading_becomes_a_draft_titled_and_sectioned_by_the_heading()
    {
        var drafts = new SpecDocumentParser().Parse(Doc).Value!;

        drafts.Should().Contain(d => d.Title == "Set up auth" && d.Section == "Set up auth");
    }

    [Fact]
    public void Checklist_item_becomes_a_draft_under_its_parent_heading()
    {
        var drafts = new SpecDocumentParser().Parse(Doc).Value!;

        drafts.Should().Contain(d =>
            d.Title == "Create the login endpoint" && d.Section == "Set up auth");
    }

    [Fact]
    public void Every_draft_is_kind_Generic()
    {
        var drafts = new SpecDocumentParser().Parse(Doc).Value!;

        drafts.Should().OnlyContain(d => d.Kind == TaskKind.Generic);
    }
}
