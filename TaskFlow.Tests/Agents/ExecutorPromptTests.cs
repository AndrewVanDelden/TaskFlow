using FluentAssertions;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Agents;

public class ExecutorPromptTests
{
    private static TaskItem SampleTask() => new()
    {
        Id = 1,
        Title = "Write a haiku",
        Description = "About autumn.",
    };

    [Fact]
    public void Includes_the_task_and_omits_the_feedback_section_when_there_are_no_rejections()
    {
        var prompt = ExecutorPrompt.Build(SampleTask(), new List<string>());

        prompt.Should().Contain("Write a haiku");
        prompt.Should().NotContain("sent back in review");
    }

    [Fact]
    public void Folds_in_all_rejection_reasons_so_they_are_not_lost()
    {
        var prompt = ExecutorPrompt.Build(
            SampleTask(),
            new List<string> { "Must mention frost.", "Fix the syllable count." });

        prompt.Should().Contain("sent back in review");
        prompt.Should().Contain("Must mention frost.");
        prompt.Should().Contain("Fix the syllable count.");
    }
}
