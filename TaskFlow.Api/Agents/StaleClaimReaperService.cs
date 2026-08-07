using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Plain background sweep (NOT an <see cref="ITaskFlowAgent"/> — it does no reasoning, never calls
/// Claude, and is never discovered/scheduled by <see cref="AgentRunner"/>) that recovers tasks
/// orphaned in InProgress by a process crash or kill mid-cycle. An executor's own try/catch
/// (<c>GenericExecutorAgent.RollBackAsync</c>) only fires from within the same running process, so it
/// cannot help when the process itself dies mid-cycle; this sweep is the code path left to notice.
///
/// Runs an immediate sweep on startup, then again on <c>Agents:StaleClaimSweepIntervalMinutes</c>
/// (default 5), reclaiming anything InProgress for longer than
/// <c>Agents:StaleClaimThresholdMinutes</c> (default 30).
/// </summary>
public class StaleClaimReaperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<StaleClaimReaperService> _logger;

    public StaleClaimReaperService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<StaleClaimReaperService> logger)
    {
        // IServiceScopeFactory (not IServiceProvider directly): this service lives for the app's
        // lifetime but ITaskRepository/AppDbContext are scoped, so each sweep needs its own scope
        // (same reason AgentRunner does this).
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("Agents:StaleClaimSweepIntervalMinutes", 5)));

        _logger.LogInformation("StaleClaimReaperService started. Interval: {Interval}", interval);

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
                _logger.LogError(ex, "StaleClaimReaperService sweep failed. Will retry after interval.");
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

        _logger.LogInformation("StaleClaimReaperService stopped.");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var thresholdMinutes = Math.Max(1, _config.GetValue("Agents:StaleClaimThresholdMinutes", 30));
        var staleAfter = TimeSpan.FromMinutes(thresholdMinutes);

        using var scope = _scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskRepository>();

        var recovered = await tasks.RecoverStaleInProgressAsync(staleAfter, ct);

        if (recovered > 0)
            _logger.LogWarning(
                "StaleClaimReaperService recovered {Count} task(s) stuck InProgress for longer than {Threshold}m.",
                recovered, thresholdMinutes);
        else
            _logger.LogInformation("StaleClaimReaperService sweep complete. No stale claims found.");
    }
}
