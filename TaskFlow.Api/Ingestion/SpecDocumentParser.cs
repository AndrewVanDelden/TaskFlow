using System.Text.RegularExpressions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Rules-based, deterministic parser: one draft per markdown heading and per top-level
/// checklist item. A pure function of the input text - no Claude, no I/O - so it is free and
/// fully unit-testable. Each checklist item is filed under the most recent heading (its
/// provenance). Async only to satisfy the seam; the work itself is synchronous.
/// </summary>
public sealed class SpecDocumentParser : IIngestionParser
{
    private static readonly Regex Heading =
        new(@"^\s*#+\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    private static readonly Regex ChecklistItem =
        new(@"^\s*[-*]\s*\[[ xX]\]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    public Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default)
    {
        var drafts = new List<TaskDraft>();
        var currentHeading = string.Empty;

        foreach (var rawLine in documentText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                currentHeading = heading.Groups["text"].Value;
                drafts.Add(new TaskDraft(currentHeading, null, TaskKind.Generic, currentHeading));
                continue;
            }

            var item = ChecklistItem.Match(line);
            if (item.Success)
            {
                drafts.Add(new TaskDraft(item.Groups["text"].Value, null, TaskKind.Generic, currentHeading));
            }
        }

        return Task.FromResult(Result<IReadOnlyList<TaskDraft>>.Ok(drafts));
    }
}
