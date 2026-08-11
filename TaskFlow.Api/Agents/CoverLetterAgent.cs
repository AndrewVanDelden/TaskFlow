using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Writes a cover letter mapping the candidate's experience to one job posting. Claims
/// <see cref="TaskKind.CoverLetterTailoring"/> tasks; the shared claim/rollback/promote flow lives
/// in <see cref="TailoringAgentBase"/>.
/// </summary>
public class CoverLetterAgent : TailoringAgentBase
{
    public CoverLetterAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<CoverLetterAgent> logger)
        : base(claude, tasks, resumeContexts, jobApplications, logs, notifier, config, logger)
    {
    }

    public override string Name => AgentNames.CoverLetter;

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:CoverLetterIntervalMinutes", 5));

    protected override TaskKind Kind => TaskKind.CoverLetterTailoring;

    protected override string SaveToolName => "save_cover_letter";

    protected override string SaveToolDescription =>
        "Save the final cover letter as markdown. Call this exactly once when you are done.";

    protected override string BuildInstructions() =>
        "Write a concise cover letter mapping the candidate's experience, drawn only from the base " +
        "resume you fetch via read_base_context, to the role described in the job posting above. " +
        "Call read_base_context first, then call save_cover_letter with the final markdown.";
}
