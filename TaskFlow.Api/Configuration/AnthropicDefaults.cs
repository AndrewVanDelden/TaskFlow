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

    /// <summary>
    /// Token ceiling for agents that generate full documents (resume/cover-letter tailoring), used
    /// when <c>Anthropic:TailoringMaxTokens</c> is not configured. The shared 1024-token
    /// <see cref="MaxTokens"/> default is tuned for short, structured outputs (e.g.
    /// TaskPrioritizerAgent's tool arguments) and is too tight for a full tailored resume or cover
    /// letter — found live (2026-08-14) when a tailoring cycle ran out of output budget mid-response
    /// and ended its turn without ever calling the save tool, then looped forever on retry (no
    /// backoff/retry-limit mechanism exists). Scoped to tailoring agents only, not raised globally,
    /// so agents that don't need it (TaskPrioritizer, StaleTaskDetector, GenericExecutor) don't cost
    /// more per call than they already do.
    /// </summary>
    public const int TailoringMaxTokens = 4096;
}
