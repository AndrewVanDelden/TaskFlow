using System.Text.Json;
using Anthropic.SDK.Messaging;
using TaskFlow.Api.Common;
using TaskFlow.Api.Configuration;
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
public sealed class ClaudeJobPostingParser : IIngestionParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IClaudeClient _claude;
    private readonly IConfiguration _config;

    public ClaudeJobPostingParser(IClaudeClient claude, IConfiguration config)
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

        var json = ExtractJsonObject(text);
        if (json is null)
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response did not contain a JSON object.");

        JobPostingJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<JobPostingJson>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response was not valid job posting JSON.");
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Title))
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response did not include a job title.");

        var description = parsed.Requirements is { Count: > 0 }
            ? string.Join(", ", parsed.Requirements)
            : null;

        var draft = new TaskDraft(parsed.Title, description, TaskKind.ResumeTailoring, parsed.Company ?? string.Empty);
        return Result<IReadOnlyList<TaskDraft>>.Ok(new[] { draft });
    }

    private static string BuildPrompt(string documentText) =>
        "Extract the job title, company, and the five most important technical skills or " +
        "requirements from the job posting below. Reply with ONLY a JSON object of the shape " +
        "{\"title\": \"...\", \"company\": \"...\", \"requirements\": [\"...\", \"...\"]}. No prose, " +
        "no code fences.\n\n" + PromptSafety.WrapUntrusted(documentText);

    // Tolerates a stray fence or surrounding prose by taking the outermost { ... }.
    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private sealed record JobPostingJson(string? Title, string? Company, List<string>? Requirements);
}
