using TaskFlow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Detects tasks that have not been updated recently and takes corrective action via Claude.
/// Full implementation in Sprint 7.
/// </summary>
public class StaleTaskAgent : ITaskFlowAgent
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StaleTaskAgent> _logger;

    public string Name => "StaleTaskDetector";

    public TimeSpan Interval =>
        TimeSpan.FromMinutes(_config.GetValue<int>("Agents:StaleTaskIntervalMinutes", 60));

    public StaleTaskAgent(
        AppDbContext db,
        IConfiguration config,
        ILogger<StaleTaskAgent> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // STUB — Sprint 7 will replace this with real Claude calls

        var thresholdHours = _config.GetValue<int>("Agents:StaleTaskThresholdHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);

        var staleCount = await _db.Tasks
            .Where(t => t.Status != Models.TaskStatus.Done && t.UpdatedAt < cutoff)
            .CountAsync(cancellationToken);

        _logger.LogInformation(
            "[{Agent}] Found {Count} task(s) stale for more than {Hours}h. " +
            "Claude integration coming in Sprint 7.",
            Name, staleCount, thresholdHours);

        await Task.Delay(300, cancellationToken);
    }
}