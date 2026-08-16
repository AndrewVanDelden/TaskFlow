using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Plain background sweep (NOT an <see cref="ITaskFlowAgent"/> - same class of infra as
/// <see cref="JobApplicationPromotionReconcilerService"/>, one stage later in the pipeline) that
/// recovers JobApplications left stuck below <c>Approved</c> when both sibling tasks are actually
/// <c>Done</c>. Board bug (found 2026-08-14, reproduced against real data): before
/// <c>TaskService</c>'s Approve/Reject/UpdateStatus guard existed, an Epic-3 sibling task moved to
/// Done individually - via the Approve button, Reject, or a plain drag-and-drop, none of which had
/// any awareness of the paired-application invariant - permanently stranded its JobApplication
/// below Approved, since only <c>TryApprovePairAsync</c> ever promotes it. That silently hid the
/// Board's export-download controls for real, already-generated resume/cover-letter content the
/// user could never retrieve. The guard stops this from happening again; this sweep is what repairs
/// applications already stuck in that state, including any created before the guard existed.
///
/// Runs an immediate sweep on startup, then again on
/// <c>Agents:ApprovalSweepIntervalMinutes</c> (default 5).
/// </summary>
public class JobApplicationApprovalReconcilerService : PeriodicSweepBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public JobApplicationApprovalReconcilerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<JobApplicationApprovalReconcilerService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
    }

    protected override string Name => nameof(JobApplicationApprovalReconcilerService);

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("Agents:ApprovalSweepIntervalMinutes", 5)));

    protected override async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobApplications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();

        var promoted = await jobApplications.PromotePendingApprovedApplicationsAsync(ct);

        if (promoted > 0)
            Logger.LogWarning(
                "JobApplicationApprovalReconcilerService promoted {Count} application(s) stuck below Approved with both siblings Done.",
                promoted);
        else
            Logger.LogInformation("JobApplicationApprovalReconcilerService sweep complete. Nothing to promote.");
    }
}
