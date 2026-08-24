namespace TaskFlow.Api.Agents;

/// <summary>
/// Background service that drives all registered agents.
/// Each agent runs on its own interval in a separate task.
/// </summary>
public class AgentRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunner> _logger;

    public AgentRunner(IServiceScopeFactory scopeFactory, ILogger<AgentRunner> logger)
    {
        // We use IServiceScopeFactory (not IServiceProvider directly) because
        // our agents need DbContext, which is scoped — not a singleton.
        // Creating a scope lets us resolve scoped services from a singleton host.
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentRunner started. Discovering agents...");

        // Resolve all registered agents and spin each one up on its own schedule
        using var scope = _scopeFactory.CreateScope();
        var agents = scope.ServiceProvider.GetServices<ITaskFlowAgent>().ToList();

        _logger.LogInformation("Found {Count} agent(s): {Names}",
            agents.Count,
            string.Join(", ", agents.Select(a => a.Name)));

        // Run each agent concurrently on its own independent timer
        var agentTasks = agents.Select(agent =>
            RunAgentLoopAsync(agent, stoppingToken));

        await Task.WhenAll(agentTasks);

        _logger.LogInformation("AgentRunner stopped.");
    }

    private async Task RunAgentLoopAsync(ITaskFlowAgent agent, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent [{Name}] starting. Interval: {Interval}",
            agent.Name, agent.Interval);

        // Run immediately on startup, then on the interval
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Agent [{Name}] running cycle...", agent.Name);

                // Create a fresh scope for each agent run so DbContext is fresh
                using var scope = _scopeFactory.CreateScope();

                // Resolve the agent from the new scope so it gets fresh dependencies
                var scopedAgents = scope.ServiceProvider.GetServices<ITaskFlowAgent>();
                var scopedAgent = scopedAgents.First(a => a.Name == agent.Name);

                await scopedAgent.RunAsync(stoppingToken);

                _logger.LogInformation("Agent [{Name}] cycle complete.", agent.Name);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — don't log as error
                break;
            }
            catch (Exception ex)
            {
                // Log the error but keep the agent alive — one bad cycle shouldn't kill the loop
                _logger.LogError(ex, "Agent [{Name}] encountered an error. Will retry after interval.", agent.Name);
            }

            // Wait for the agent's configured interval before next run - or wake immediately if the
            // agent signals it should (GenericExecutorAgent does when a human re-enables it), so
            // that does not sit out however much of the interval remains.
            await WaitForNextCycleAsync(agent, stoppingToken);
        }
    }

    // Extracted for direct unit testing (AgentRunnerTests): a BackgroundService's real ExecuteAsync
    // loop runs on wall-clock time indefinitely, which is not something to drive from a fast,
    // deterministic test. internal + InternalsVisibleTo (TaskFlow.Tests) lets the interval-vs-wake
    // race itself be tested with a controllable fake ITaskFlowAgent instead.
    internal static Task WaitForNextCycleAsync(ITaskFlowAgent agent, CancellationToken stoppingToken) =>
        Task.WhenAny(
            Task.Delay(agent.Interval, stoppingToken),
            agent.WaitForWakeSignalAsync(stoppingToken));
}