using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Services;
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

    // PR #70 review finding (Antigravity/Gemini, independently confirmed by a second manual review):
    // Task.WhenAny does not cancel its loser. When the interval wins (the ordinary case - a human
    // clicking Enable is rare), the wake side's SemaphoreSlim.WaitAsync() call stays registered in
    // the semaphore's FIFO wait queue forever (nothing ever cancels it). Every later cycle that also
    // loses the race to the interval leaves one more such abandoned waiter ahead of it. Since
    // SemaphoreSlim.Release() satisfies waiters strictly in arrival order, a later Enable() call can
    // end up completing a stale, already-abandoned waiter from an earlier cycle instead of the
    // *current* live cycle's wait - silently reproducing the exact "sits idle" bug this PR exists to
    // fix. Reproduced here with a real ExecutorSwitch (the actual SemaphoreSlim-backed wake source),
    // not the FakeAgent's plain Func, since the bug is specifically about the semaphore's queue.
    [Fact]
    public async Task A_wake_after_an_earlier_cycle_already_lost_the_interval_race_still_wakes_the_current_cycle()
    {
        var sw = new ExecutorSwitch(new ConfigurationBuilder().Build());

        // Cycle 1: a short interval wins well before anyone calls Enable() - this is the call whose
        // wake-wait is left dangling in the semaphore's queue if the loser isn't torn down.
        var firstCycleAgent = new FakeAgent(TimeSpan.FromMilliseconds(10), sw.WaitForWakeAsync);
        await AgentRunner.WaitForNextCycleAsync(firstCycleAgent, CancellationToken.None);

        // Cycle 2: a fresh wait racing a 10-minute interval - it can only complete quickly via the
        // wake signal below, not by outrunning the interval the way cycle 1 did.
        var secondCycleAgent = new FakeAgent(TimeSpan.FromMinutes(10), sw.WaitForWakeAsync);
        var second = AgentRunner.WaitForNextCycleAsync(secondCycleAgent, CancellationToken.None);
        sw.Enable();

        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(second, "Enable() must wake the live current-cycle wait, not an earlier cycle's abandoned waiter");
    }
}
