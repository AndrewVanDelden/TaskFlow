using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Security;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Agent-backed parser for pasted job postings: hands the posting text to Claude and turns its
/// JSON reply into a single task draft carrying the job title, company, and a rolled-up
/// requirements summary. Used only for postings the free <see cref="JobPostingParser"/> could not
/// structure (see <see cref="TieredIngestionParser"/>). The posting text is untrusted user input,
/// so it is wrapped with <see cref="PromptSafety.WrapUntrusted(string, string)"/> before it goes
/// anywhere near the prompt sent to Claude. When no API key is configured it returns an empty
/// result rather than throwing, so the app still works offline.
/// </summary>
public sealed class ClaudeJobPostingParser : ClaudeJsonExtractionParserBase<ClaudeJobPostingParser.JobPostingJson>
{
    public ClaudeJobPostingParser(IClaudeClient claude, IConfiguration config) : base(claude, config)
    {
    }

    protected override char JsonOpenDelimiter => '{';
    protected override char JsonCloseDelimiter => '}';

    protected override string BuildPrompt(string documentText) =>
        "Extract the job title, company, and the five most important technical skills or " +
        "requirements from the job posting below. Reply with ONLY a JSON object of the shape " +
        "{\"title\": \"...\", \"company\": \"...\", \"requirements\": [\"...\", \"...\"]}. No prose, " +
        "no code fences.\n\n" + PromptSafety.WrapUntrusted(documentText);

    protected override Result<IReadOnlyList<TaskDraft>> MapToDrafts(JobPostingJson parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.Title))
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response did not include a job title.");

        var description = parsed.Requirements is { Count: > 0 }
            ? string.Join(", ", parsed.Requirements)
            : null;

        var draft = new TaskDraft(parsed.Title, description, TaskKind.ResumeTailoring, parsed.Company ?? string.Empty);
        return Result<IReadOnlyList<TaskDraft>>.Ok(new[] { draft });
    }

    public sealed record JobPostingJson(string? Title, string? Company, List<string>? Requirements);
}
