using System.Text.Json;
using Anthropic.SDK.Messaging;
using TaskFlow.Api.Common;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Agent-backed parser: hands the document to Claude and turns its reply into task drafts.
/// Used only for content the free rules parser cannot handle (see <see cref="TieredIngestionParser"/>).
/// When no API key is configured it returns an empty result rather than throwing, so the app
/// still works offline. The live-Claude specifics (prompt, JSON shape) are confirmed against a
/// real key at runtime; the test drives it with a StubClaude canned response.
/// </summary>
public sealed class ClaudeIngestionParser : IIngestionParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IClaudeClient _claude;
    private readonly IConfiguration _config;

    public ClaudeIngestionParser(IClaudeClient claude, IConfiguration config)
    {
        _claude = claude;
        _config = config;
    }

    public async Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default)
    {
        if (!_claude.IsConfigured)
            return Result<IReadOnlyList<TaskDraft>>.Ok(Array.Empty<TaskDraft>());

        var model = _config["Anthropic:Model"] ?? AnthropicDefaults.Model;
        var maxTokens = _config.GetValue("Anthropic:MaxTokens", AnthropicDefaults.MaxTokens);

        var response = await _claude.SendAsync(new MessageParameters
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = new List<Message> { new(RoleType.User, BuildPrompt(documentText)) }
        }, cancellationToken);

        var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude returned no content to parse.");

        var json = ExtractJsonArray(text);
        if (json is null)
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response did not contain a JSON array.");

        List<DraftJson>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<DraftJson>>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response was not valid draft JSON.");
        }

        var drafts = (parsed ?? new List<DraftJson>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Title))
            .Select(d => new TaskDraft(d.Title!, d.Description, TaskKind.Generic, d.Section ?? string.Empty))
            .ToList();

        return Result<IReadOnlyList<TaskDraft>>.Ok(drafts);
    }

    private static string BuildPrompt(string documentText) =>
        "Split the following document into discrete work items. Reply with ONLY a JSON array; each " +
        "element has \"title\" (a short imperative), \"description\" (one sentence or null), and " +
        "\"section\" (the heading or area it came from). No prose, no code fences.\n\n" + documentText;

    // Tolerates a stray fence or surrounding prose by taking the outermost [ ... ].
    private static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private sealed record DraftJson(string? Title, string? Description, string? Section);
}
