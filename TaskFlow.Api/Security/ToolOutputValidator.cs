using TaskFlow.Api.Common;

namespace TaskFlow.Api.Security;

/// <summary>
/// Shared guardrail for Claude tools that save generated content (e.g. a tailored resume) to
/// storage. Every such save-tool must run its content through <see cref="Validate"/> before
/// touching storage, so oversized or empty output is rejected up front with a clear failure result.
/// </summary>
public static class ToolOutputValidator
{
    /// <summary>
    /// Rejects null, empty, or whitespace-only content, and content longer than
    /// <paramref name="maxLength"/>. Returns the content unchanged on success.
    /// </summary>
    /// <param name="content">The generated content to validate before it is persisted.</param>
    /// <param name="maxLength">The maximum allowed length of <paramref name="content"/>.</param>
    public static Result<string> Validate(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result<string>.Invalid("Content must not be null, empty, or whitespace-only.");
        }

        if (content.Length > maxLength)
        {
            return Result<string>.Invalid($"Content length {content.Length} exceeds the maximum of {maxLength}.");
        }

        return Result<string>.Ok(content);
    }
}
