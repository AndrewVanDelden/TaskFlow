using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Plain background sweep (NOT an <see cref="ITaskFlowAgent"/> — it does no reasoning, never calls
/// Claude, and is never discovered/scheduled by <see cref="AgentRunner"/>) that recovers
/// JobApplications left stuck at <c>Building</c> when both sibling tasks are actually
/// <c>Review</c>. <see cref="TailoringAgentBase"/>'s "try the atomic join right after saving" is a
/// best-effort trigger, not a guarantee — if the AgentLog write or the join call itself throws (a
/// transient DB write-lock under two agents' concurrent DbContexts is the realistic trigger)
/// between the save and the join, the trigger is lost and nothing else retries it (PR #43 review,
/// round 2: found independently by both a manual review and Copilot's automated review). This
/// sweep is the code path left to notice and correct it — shares its sweep-loop shape with
/// <see cref="StaleClaimReaperService"/> via <see cref="PeriodicSweepBackgroundService"/>.
///
/// Runs an immediate sweep on startup, then again on
/// <c>Agents:PromotionSweepIntervalMinutes</c> (default 5).
/// </summary>
public class JobApplicationPromotionReconcilerService : PeriodicSweepBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public JobApplicationPromotionReconcilerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<JobApplicationPromotionReconcilerService> logger)
        : base(logger)
    {
        // IServiceScopeFactory (not IServiceProvider directly): this service lives for the app's
        // lifetime but IJobApplicationRepository/AppDbContext are scoped, so each sweep needs its
        // own scope (same reason AgentRunner and StaleClaimReaperService do this).
        _scopeFactory = scopeFactory;
        _config = config;
    }

    protected override string Name => nameof(JobApplicationPromotionReconcilerService);

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("Agents:PromotionSweepIntervalMinutes", 5)));

    protected override async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobApplications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();

        var promoted = await jobApplications.PromotePendingReviewReadyApplicationsAsync(ct);

        if (promoted > 0)
            Logger.LogWarning(
                "JobApplicationPromotionReconcilerService promoted {Count} application(s) stuck at Building with both siblings Review.",
                promoted);
        else
            Logger.LogInformation("JobApplicationPromotionReconcilerService sweep complete. Nothing to promote.");
    }
}
