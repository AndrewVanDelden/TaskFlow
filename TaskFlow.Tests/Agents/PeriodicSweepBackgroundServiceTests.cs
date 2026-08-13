using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFlow.Api.Agents;
using Xunit;

namespace TaskFlow.Tests.Agents;

// Epic 3 Pre-Merge Code Review, finding 3.6: StaleClaimReaperService and
// JobApplicationPromotionReconcilerService duplicated this exact sweep loop verbatim, and neither
// had any test coverage (finding 6.1). These tests exercise the extracted shared base directly, so
// both concrete services inherit locked-in behavior instead of each needing their own copy.
public class PeriodicSweepBackgroundServiceTests
{
    private sealed class TestSweepService : PeriodicSweepBackgroundService
    {
        private readonly Func<CancellationToken, Task> _sweep;

        public TestSweepService(TimeSpan interval, Func<CancellationToken, Task> sweep)
            : base(NullLogger.Instance)
        {
            Interval = interval;
            _sweep = sweep;
        }

        protected override string Name => "TestSweep";
        protected override TimeSpan Interval { get; }
        protected override Task SweepAsync(CancellationToken ct) => _sweep(ct);
    }

    [Fact]
    public async Task Sweeps_immediately_on_start_without_waiting_for_the_interval()
    {
        var firstSweep = new TaskCompletionSource();
        var service = new TestSweepService(TimeSpan.FromMinutes(10), ct =>
        {
            firstSweep.TrySetResult();
            return Task.CompletedTask;
        });

        await service.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(firstSweep.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(firstSweep.Task, "the first sweep must run immediately, not after the interval");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Keeps_sweeping_on_the_interval_after_a_sweep_throws()
    {
        var count = 0;
        var thirdSweep = new TaskCompletionSource();
        var service = new TestSweepService(TimeSpan.FromMilliseconds(10), ct =>
        {
            var call = Interlocked.Increment(ref count);
            if (call == 1)
                throw new InvalidOperationException("sweep failed (test)");
            if (call >= 3)
                thirdSweep.TrySetResult();
            return Task.CompletedTask;
        });

        await service.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(thirdSweep.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await service.StopAsync(CancellationToken.None);

        completed.Should().Be(thirdSweep.Task, "a failed sweep must not stop the loop from retrying on the interval");
    }

    [Fact]
    public async Task Stops_promptly_when_a_sweep_reports_cancellation()
    {
        var service = new TestSweepService(
            TimeSpan.FromMinutes(10),
            ct => throw new OperationCanceledException());

        await service.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(service.ExecuteTask, "an OperationCanceledException from the sweep itself must end the loop, not just log and retry");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stops_promptly_when_the_host_requests_shutdown_while_waiting_for_the_next_interval()
    {
        var sweeping = new TaskCompletionSource();
        var service = new TestSweepService(TimeSpan.FromMinutes(10), ct =>
        {
            sweeping.TrySetResult();
            return Task.CompletedTask;
        });

        await service.StartAsync(CancellationToken.None);
        await sweeping.Task; // let the immediate sweep complete so the loop is parked in Task.Delay

        await service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(service.ExecuteTask, "StopAsync's cancellation must interrupt the interval delay promptly");
    }
}
