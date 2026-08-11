using System.Text.Json;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Common;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Shared skeleton for a Claude-backed parser that sends a fixed prompt, extracts one JSON value
/// from the reply, deserializes it, and maps the result to task drafts. The "not configured"
/// short-circuit, the API call, the reply-text extraction, the JSON-substring extraction, and the
/// deserialize-failure handling live here exactly once; a concrete parser supplies only its prompt,
/// its JSON shape's outer delimiters (array <c>[ ]</c> or object <c>{ }</c>), and how its
/// deserialized shape maps to drafts.
/// </summary>
/// <typeparam name="TJson">The shape Claude's JSON reply deserializes into.</typeparam>
public abstract class ClaudeJsonExtractionParserBase<TJson> : IIngestionParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IClaudeClient _claude;
    private readonly IConfiguration _config;

    protected ClaudeJsonExtractionParserBase(IClaudeClient claude, IConfiguration config)
    {
        _claude = claude;
        _config = config;
    }

    /// <summary>The character that opens the JSON value in Claude's reply (<c>[</c> or <c>{</c>).</summary>
    protected abstract char JsonOpenDelimiter { get; }

    /// <summary>The character that closes the JSON value in Claude's reply (<c>]</c> or <c>}</c>).</summary>
    protected abstract char JsonCloseDelimiter { get; }

    /// <summary>Builds the fixed prompt sent to Claude for the given document text.</summary>
    protected abstract string BuildPrompt(string documentText);

    /// <summary>Maps the deserialized JSON shape to the drafts this parser produces, or an Invalid
    /// result if the shape's own fields (not just the JSON syntax) don't hold enough to proceed.</summary>
    protected abstract Result<IReadOnlyList<TaskDraft>> MapToDrafts(TJson parsed);

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

        var json = ExtractJson(text);
        if (json is null)
            return Result<IReadOnlyList<TaskDraft>>.Invalid(
                $"Claude response did not contain a JSON {(JsonOpenDelimiter == '[' ? "array" : "object")}.");

        TJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TJson>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response was not valid JSON.");
        }

        if (parsed is null)
            return Result<IReadOnlyList<TaskDraft>>.Invalid("Claude response was not valid JSON.");

        return MapToDrafts(parsed);
    }

    // Tolerates a stray fence or surrounding prose by taking the outermost delimiter pair.
    private string? ExtractJson(string text)
    {
        var start = text.IndexOf(JsonOpenDelimiter);
        var end = text.LastIndexOf(JsonCloseDelimiter);
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
