namespace TaskFlow.Api.Configuration;

/// <summary>
/// Default values for Anthropic/Claude calls, used when the corresponding
/// <c>appsettings</c> keys are not set. Centralized here so every caller
/// (agents and diagnostics) shares one source of truth instead of each
/// hard-coding its own model string or token limit.
/// </summary>
public static class AnthropicDefaults
{
    /// <summary>Model used when <c>Anthropic:Model</c> is not configured.</summary>
    public const string Model = "claude-sonnet-4-6";

    /// <summary>Token ceiling used when <c>Anthropic:MaxTokens</c> is not configured.</summary>
    public const int MaxTokens = 1024;
}
