namespace TaskFlow.Api.Agents;

/// <summary>
/// Shared shape for a plain (non-agent) background sweep: run once immediately, then again on a
/// fixed interval; a sweep that throws is logged and retried on the next interval rather than
/// killing the loop; a sweep that reports cancellation (as opposed to an ordinary failure) ends the
/// loop instead of retrying. Extracted from StaleClaimReaperService and
/// JobApplicationPromotionReconcilerService, which had this loop duplicated verbatim (Epic 3
/// Pre-Merge Code Review, finding 3.6) — the same move already made for
/// ClaudeAgentBase/TailoringAgentBase.
/// </summary>
public abstract class PeriodicSweepBackgroundService : BackgroundService
{
    protected ILogger Logger { get; }

    protected PeriodicSweepBackgroundService(ILogger logger) => Logger = logger;

    /// <summary>Display name used in the loop's own start/stop/failure log messages.</summary>
    protected abstract string Name { get; }

    /// <summary>How often to sweep, after the immediate startup sweep.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>Runs one sweep. Exceptions are caught and logged by the loop; an
    /// <see cref="OperationCanceledException"/> ends the loop instead of being retried.</summary>
    protected abstract Task SweepAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Interval;
        Logger.LogInformation("{Service} started. Interval: {Interval}", Name, interval);

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
                Logger.LogError(ex, "{Service} sweep failed. Will retry after interval.", Name);
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

        Logger.LogInformation("{Service} stopped.", Name);
    }
}
