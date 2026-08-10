using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Security;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Agent-backed parser: hands the document to Claude and turns its reply into task drafts.
/// Used only for content the free rules parser cannot handle (see <see cref="TieredIngestionParser"/>).
/// When no API key is configured it returns an empty result rather than throwing, so the app
/// still works offline. The live-Claude specifics (prompt, JSON shape) are confirmed against a
/// real key at runtime; the test drives it with a StubClaude canned response. The document text is
/// untrusted user input, so it is wrapped with <see cref="PromptSafety.WrapUntrusted(string, string)"/>
/// before it reaches the prompt — the same protection <see cref="ClaudeJobPostingParser"/> already
/// has (PR #40 review finding: this parser was the one sibling that didn't).
/// </summary>
public sealed class ClaudeIngestionParser : ClaudeJsonExtractionParserBase<List<ClaudeIngestionParser.DraftJson>>
{
    public ClaudeIngestionParser(IClaudeClient claude, IConfiguration config) : base(claude, config)
    {
    }

    protected override char JsonOpenDelimiter => '[';
    protected override char JsonCloseDelimiter => ']';

    protected override string BuildPrompt(string documentText) =>
        "Split the following document into discrete work items. Reply with ONLY a JSON array; each " +
        "element has \"title\" (a short imperative), \"description\" (one sentence or null), and " +
        "\"section\" (the heading or area it came from). No prose, no code fences.\n\n" +
        PromptSafety.WrapUntrusted(documentText);

    protected override Result<IReadOnlyList<TaskDraft>> MapToDrafts(List<DraftJson> parsed)
    {
        var drafts = parsed
            .Where(d => !string.IsNullOrWhiteSpace(d.Title))
            .Select(d => new TaskDraft(d.Title!, d.Description, TaskKind.Generic, d.Section ?? string.Empty))
            .ToList();

        return Result<IReadOnlyList<TaskDraft>>.Ok(drafts);
    }

    public sealed record DraftJson(string? Title, string? Description, string? Section);
}
