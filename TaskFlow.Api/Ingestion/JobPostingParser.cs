using System.Text.RegularExpressions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Rules-based, deterministic parser for pasted job postings: the first level-1 heading is the
/// job title, the first level-2 heading (if any) is the company. A pure function of the input
/// text - no Claude, no I/O - so it is free and fully unit-testable. Each heading level is found
/// independently of the other's position, since real postings vary in layout (a company heading
/// can appear before the title heading, or not at all). Returns an empty list when no level-1
/// heading is found anywhere; this is what makes <see cref="TieredIngestionParser"/> escalate to
/// the Claude-backed parser, so it is deliberate, not an oversight. Async only to satisfy the
/// seam; the work itself is synchronous.
/// </summary>
public sealed class JobPostingParser : IIngestionParser
{
    // Exactly one leading '#' - the negative lookahead rejects '##'/'###' so H2/H3 never match here.
    private static readonly Regex H1 =
        new(@"^#(?!#)\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    // Exactly two leading '#' - the negative lookahead rejects '###' so H3 never matches here.
    private static readonly Regex H2 =
        new(@"^##(?!#)\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    public Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default)
    {
        string? title = null;
        string? company = null;

        foreach (var rawLine in documentText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (title is null)
            {
                var h1 = H1.Match(line);
                if (h1.Success)
                {
                    title = h1.Groups["text"].Value;
                    continue;
                }
            }

            if (company is null)
            {
                var h2 = H2.Match(line);
                if (h2.Success)
                    company = h2.Groups["text"].Value;
            }
        }

        if (title is null)
            return Task.FromResult(Result<IReadOnlyList<TaskDraft>>.Ok(Array.Empty<TaskDraft>()));

        var draft = new TaskDraft(title, null, TaskKind.ResumeTailoring, string.Empty, company);
        return Task.FromResult(Result<IReadOnlyList<TaskDraft>>.Ok(new[] { draft }));
    }
}
