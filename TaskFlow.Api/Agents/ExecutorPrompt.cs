using System.Text;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Builds the generic executor's prompt for a task, folding in any outstanding review feedback so a
/// rejection reason is not lost across reworks. Pure and static, so it is unit-testable on its own.
/// </summary>
public static class ExecutorPrompt
{
    public static string Build(TaskItem task, IReadOnlyList<string> rejectionReasons)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous task-execution agent for a software team.");
        sb.AppendLine("You cannot write files or change the codebase yourself. Your job is to reason about the");
        sb.AppendLine("task, record a brief plan and any progress, then hand the task to a human for review.");
        sb.AppendLine();
        sb.AppendLine($"Task {task.Id}: {task.Title}");
        if (!string.IsNullOrWhiteSpace(task.Description))
            sb.AppendLine($"Description: {task.Description}");

        if (rejectionReasons.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("This task was sent back in review. You MUST address ALL of this feedback before");
            sb.AppendLine("requesting review again:");
            for (var i = 0; i < rejectionReasons.Count; i++)
                sb.AppendLine($"  {i + 1}. {rejectionReasons[i]}");
        }

        sb.AppendLine();
        sb.AppendLine("How to proceed:");
        sb.AppendLine("  1. Think through what completing this task requires.");
        sb.AppendLine("  2. Call record_progress with a short note on your plan (one or two sentences; do NOT paste code or file contents).");
        sb.AppendLine("  3. Call request_review with a one-paragraph summary. This hands the task to a human.");
        sb.AppendLine("Finish by calling request_review exactly once. Keep messages short and do not output code or file contents.");
        return sb.ToString();
    }
}
