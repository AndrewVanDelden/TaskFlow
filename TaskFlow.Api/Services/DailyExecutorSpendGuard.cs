using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Services;

/// <summary>
/// Spend cap v1: lets the executor run until it has claimed <c>Agents:DailyExecutorTaskCap</c> tasks
/// in the current UTC day (one claim is roughly one Claude conversation). It counts existing
/// <c>AgentLog</c> rows, so there is no extra state and no migration. Token-based accounting (from
/// <c>MessageResponse.Usage</c>) can replace this behind <see cref="ISpendGuard"/> later.
/// </summary>
public class DailyExecutorSpendGuard : ISpendGuard
{
    private readonly IAgentLogRepository _logs;
    private readonly IConfiguration _config;

    public DailyExecutorSpendGuard(IAgentLogRepository logs, IConfiguration config)
    {
        _logs = logs;
        _config = config;
    }

    public async Task<bool> CanRunAsync(CancellationToken ct = default)
    {
        var cap = _config.GetValue("Agents:DailyExecutorTaskCap", 25);
        var since = DateTime.UtcNow.Date;   // start of the current UTC day
        var used = await _logs.CountByAgentActionSinceAsync(
            AgentNames.GenericExecutor, AgentActions.Claimed, since, ct);
        return used < cap;
    }
}
