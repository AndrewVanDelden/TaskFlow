namespace TaskFlow.Api.Configuration;

/// <summary>Defaults used when the Anthropic appsettings keys are not set.</summary>
public static class AnthropicDefaults
{
    public const string Model = "claude-sonnet-4-6";
    public const int MaxTokens = 1024;
}
