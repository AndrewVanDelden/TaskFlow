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
/// sweep is the code path left to notice and correct it — mirrors
/// <see cref="StaleClaimReaperService"/>'s shape exactly.
///
/// Runs an immediate sweep on startup, then again on
/// <c>Agents:PromotionSweepIntervalMinutes</c> (default 5).
/// </summary>
public class JobApplicationPromotionReconcilerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<JobApplicationPromotionReconcilerService> _logger;

    public JobApplicationPromotionReconcilerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<JobApplicationPromotionReconcilerService> logger)
    {
        // IServiceScopeFactory (not IServiceProvider directly): this service lives for the app's
        // lifetime but IJobApplicationRepository/AppDbContext are scoped, so each sweep needs its
        // own scope (same reason AgentRunner and StaleClaimReaperService do this).
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("Agents:PromotionSweepIntervalMinutes", 5)));

        _logger.LogInformation("JobApplicationPromotionReconcilerService started. Interval: {Interval}", interval);

        // Run immediately on startup, then on the interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — don't log as error.
                break;
            }
            catch (Exception ex)
            {
                // Log the error but keep sweeping — one bad sweep shouldn't kill the loop.
                _logger.LogError(ex, "JobApplicationPromotionReconcilerService sweep failed. Will retry after interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("JobApplicationPromotionReconcilerService stopped.");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobApplications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();

        var promoted = await jobApplications.PromotePendingReviewReadyApplicationsAsync(ct);

        if (promoted > 0)
            _logger.LogWarning(
                "JobApplicationPromotionReconcilerService promoted {Count} application(s) stuck at Building with both siblings Review.",
                promoted);
        else
            _logger.LogInformation("JobApplicationPromotionReconcilerService sweep complete. Nothing to promote.");
    }
}
