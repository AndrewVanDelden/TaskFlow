using FluentAssertions;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class JobPostingParserTests
{
    private readonly JobPostingParser _parser = new();

    [Fact]
    public async Task H1_and_H2_present_extracts_title_and_company()
    {
        const string text = "# Senior Backend Engineer\n## Acme Corp\nSome body text.";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(d =>
            d.Title == "Senior Backend Engineer" &&
            d.Section == "Acme Corp" &&
            d.Kind == TaskKind.ResumeTailoring &&
            d.Description == null);
    }

    [Fact]
    public async Task H1_present_without_H2_leaves_Section_as_empty_string_not_null()
    {
        const string text = "# Senior Backend Engineer\nSome body text, no company heading.";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!.Single();
        draft.Title.Should().Be("Senior Backend Engineer");
        draft.Section.Should().Be(string.Empty);
        draft.Section.Should().NotBeNull();
    }

    [Fact]
    public async Task No_heading_at_all_returns_success_with_an_empty_list_to_trigger_escalation()
    {
        const string text = "Just a plain paragraph describing a job, no markdown headings anywhere.";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task H2_appearing_before_the_H1_is_still_found_independently_of_order()
    {
        const string text = "## Acme Corp\nSome intro text.\n# Senior Backend Engineer\nMore body text.";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!.Single();
        draft.Title.Should().Be("Senior Backend Engineer");
        draft.Section.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task H3_heading_is_not_mistaken_for_H1_or_H2()
    {
        const string text = "### Not a title\n# Actual Title\n### Not a company either\n## Actual Company";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!.Single();
        draft.Title.Should().Be("Actual Title");
        draft.Section.Should().Be("Actual Company");
    }

    [Fact]
    public async Task H3_alone_with_no_H1_returns_empty_list()
    {
        const string text = "### Just a subheading, no real title anywhere.";

        var result = await _parser.ParseAsync(text);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }
}
