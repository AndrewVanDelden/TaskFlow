using FluentAssertions;
using TaskFlow.Api.Agents;
using Xunit;

namespace TaskFlow.Tests.Agents;

// User report (2026-08-24): pressing "Enable" on the executor toggle should run a cycle right away,
// not wait out however much of the interval remains. AgentRunner's per-agent loop now races the
// interval delay against the agent's own WaitForWakeSignalAsync (Task.WhenAny) instead of always
// waiting the full Interval; WaitForNextCycleAsync is that race, extracted so it can be driven
// directly here instead of through a real BackgroundService's wall-clock ExecuteAsync loop.
public class AgentRunnerTests
{
    private sealed class FakeAgent(TimeSpan interval, Func<CancellationToken, Task> wake) : ITaskFlowAgent
    {
        public string Name => "FakeAgent";
        public TimeSpan Interval { get; } = interval;
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WaitForWakeSignalAsync(CancellationToken cancellationToken) => wake(cancellationToken);
    }

    [Fact]
    public async Task Completes_immediately_when_the_agent_wakes_even_though_the_interval_is_far_off()
    {
        var agent = new FakeAgent(TimeSpan.FromMinutes(10), _ => Task.CompletedTask);

        var waitTask = AgentRunner.WaitForNextCycleAsync(agent, CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(waitTask, "an already-resolved wake signal must win the race, not the 10-minute interval");
    }

    [Fact]
    public async Task Falls_back_to_the_interval_when_the_agent_never_wakes()
    {
        var agent = new FakeAgent(TimeSpan.FromMilliseconds(10), ct => Task.Delay(Timeout.InfiniteTimeSpan, ct));

        var waitTask = AgentRunner.WaitForNextCycleAsync(agent, CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(waitTask, "a 10ms interval must still complete the wait even though the agent's own wake signal never resolves");
    }

    [Fact]
    public async Task Cancelling_the_stopping_token_ends_the_wait_promptly()
    {
        var agent = new FakeAgent(TimeSpan.FromMinutes(10), ct => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var cts = new CancellationTokenSource();

        var waitTask = AgentRunner.WaitForNextCycleAsync(agent, cts.Token);
        cts.Cancel();

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(waitTask, "cancelling the host's stopping token must interrupt the wait promptly, not leave it parked for the full interval");
    }
}
