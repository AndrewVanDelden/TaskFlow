using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Rewrites the candidate's base resume to align with one job posting. Claims
/// <see cref="TaskKind.ResumeTailoring"/> tasks; the shared claim/rollback/promote flow lives in
/// <see cref="TailoringAgentBase"/>.
/// </summary>
public class ResumeTailoringAgent : TailoringAgentBase
{
    public ResumeTailoringAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<ResumeTailoringAgent> logger)
        : base(claude, tasks, resumeContexts, jobApplications, logs, notifier, config, logger)
    {
    }

    public override string Name => AgentNames.ResumeTailoring;

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:ResumeTailoringIntervalMinutes", 5));

    protected override TaskKind Kind => TaskKind.ResumeTailoring;

    protected override string SaveToolName => "save_tailored_resume";

    protected override string SaveToolDescription =>
        "Save the final tailored resume as markdown. Call this exactly once when you are done.";

    protected override string BuildInstructions() =>
        "Rewrite the professional summary and experience bullets to align with the job posting " +
        "above, using only what is present in the base resume you fetch via read_base_context - do " +
        "not invent experience. Call read_base_context first, then call save_tailored_resume with " +
        "the final markdown.";
}
