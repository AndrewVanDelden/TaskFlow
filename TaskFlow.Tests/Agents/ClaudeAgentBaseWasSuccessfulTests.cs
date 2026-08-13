using Anthropic.SDK.Messaging;
using FluentAssertions;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Security;
using Xunit;

namespace TaskFlow.Tests.Agents;

// Epic 3 Pre-Merge Code Review, finding 2.1: WasSuccessful classified a tool result as failed via
// an unbounded substring search for "not found" / "does not exist" - but TailoringAgentBase's
// read_base_context tool returns the user's own resume text as a tool result, and ordinary resume
// prose containing either phrase (e.g. "a tool that does not exist anymore") would misclassify a
// genuinely successful read as failed. `internal` visibility (TaskFlow.Api.csproj's new
// InternalsVisibleTo) lets this test call the heuristic directly instead of only indirectly
// through an agent's logging side effect, which is the only place its result was ever observable.
public class ClaudeAgentBaseWasSuccessfulTests
{
    private static ToolResultContent Result(string text) => new()
    {
        ToolUseId = "tool_1",
        Content = new List<ContentBase> { new TextContent { Text = text } }
    };

    [Theory]
    [InlineData("Error: unknown tool foo")]
    [InlineData("Task 999 not found.")]
    [InlineData("Application 5 does not exist.")]
    public void Returns_false_for_a_short_code_generated_error_message(string text)
    {
        ClaudeAgentBase.WasSuccessful(Result(text)).Should().BeFalse();
    }

    [Fact]
    public void Returns_true_for_an_ordinary_success_message()
    {
        var text = "Escalated Task 1 ('Write a haiku') from Medium to High.";

        ClaudeAgentBase.WasSuccessful(Result(text)).Should().BeTrue();
    }

    // The actual bug: a genuinely successful read_base_context call whose payload happens to
    // contain the trigger phrases must not be miscounted as failed.
    [Fact]
    public void Returns_true_when_wrapped_untrusted_content_merely_contains_the_trigger_phrases()
    {
        var resume =
            "Experience: maintained a legacy build tool that does not exist anymore. " +
            "Also mention a certification not found in my current records, but still relevant.";
        var wrapped = PromptSafety.WrapUntrusted(resume, "base_resume");

        ClaudeAgentBase.WasSuccessful(Result(wrapped)).Should().BeTrue();
    }

    // Copilot's automated review (PR #50): the first fix (a 256-character scan window) still
    // failed this exact case - WrapUntrusted's framing + tag is shorter than 256 characters, so a
    // resume whose very first words are a trigger phrase is still inside the window and still
    // misclassified as a failed tool call.
    [Fact]
    public void Returns_true_when_the_trigger_phrase_is_the_first_thing_in_the_wrapped_content()
    {
        var resume = "Does not exist yet: I am still pursuing this certification.";
        var wrapped = PromptSafety.WrapUntrusted(resume, "base_resume");

        ClaudeAgentBase.WasSuccessful(Result(wrapped)).Should().BeTrue();
    }

    [Fact]
    public void Returns_true_when_the_tool_result_has_no_text_content()
    {
        var result = new ToolResultContent { ToolUseId = "tool_1", Content = new List<ContentBase>() };

        // No text at all is treated as an empty string, which contains none of the trigger
        // phrases and doesn't start with "Error" - so it counts as successful. Documented here so
        // a future change to this edge case is a deliberate decision, not an accident.
        ClaudeAgentBase.WasSuccessful(result).Should().BeTrue();
    }
}
