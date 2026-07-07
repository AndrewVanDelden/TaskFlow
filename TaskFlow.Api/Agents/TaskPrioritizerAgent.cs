using TaskFlow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Autonomously re-prioritizes tasks using Claude based on due dates and workload.
/// Full implementation in Sprint 6.
/// </summary>
public class TaskPrioritizerAgent : ITaskFlowAgent
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<TaskPrioritizerAgent> _logger;

    public string Name => "TaskPrioritizer";

    public TimeSpan Interval =>
        TimeSpan.FromMinutes(_config.GetValue<int>("Agents:PrioritizerIntervalMinutes", 30));

    public TaskPrioritizerAgent(
        AppDbContext db,
        IConfiguration config,
        ILogger<TaskPrioritizerAgent> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // STUB — Sprint 6 will replace this with real Claude calls

        var openTaskCount = await _db.Tasks
            .Where(t => t.Status != Models.TaskStatus.Done)
            .CountAsync(cancellationToken);

        _logger.LogInformation(
            "[{Agent}] Found {Count} open task(s) to analyze. " +
            "Claude integration coming in Sprint 6.",
            Name, openTaskCount);

        // Simulate a small delay so the log timing looks realistic
        await Task.Delay(500, cancellationToken);
    }
}