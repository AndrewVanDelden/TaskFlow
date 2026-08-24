using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class ExecutorSwitchTests
{
    private static IConfiguration Config(bool? enabled = null)
    {
        var dict = new Dictionary<string, string?>();
        if (enabled.HasValue) dict["Agents:ExecutorEnabled"] = enabled.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Defaults_to_disabled_when_config_is_absent()
    {
        var sut = new ExecutorSwitch(Config());
        sut.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Seeds_from_config_and_toggles()
    {
        var sut = new ExecutorSwitch(Config(enabled: true));
        sut.IsEnabled.Should().BeTrue();

        sut.Disable();
        sut.IsEnabled.Should().BeFalse();

        sut.Enable();
        sut.IsEnabled.Should().BeTrue();
    }

    // User report (2026-08-24): pressing "Enable" should run a cycle immediately instead of waiting
    // out however much of the executor's interval remains. WaitForWakeAsync is the signal
    // AgentRunner's loop races against the interval delay for exactly that.
    [Fact]
    public async Task Enable_completes_a_pending_WaitForWakeAsync_call()
    {
        var sut = new ExecutorSwitch(Config());
        var wait = sut.WaitForWakeAsync(CancellationToken.None);

        wait.IsCompleted.Should().BeFalse();

        sut.Enable();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForWakeAsync_completes_immediately_if_a_wake_is_already_pending()
    {
        var sut = new ExecutorSwitch(Config());
        sut.Enable();

        var wait = sut.WaitForWakeAsync(CancellationToken.None);

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    // Enable() can be called (e.g. re-toggled) before anyone has consumed the previous wake; this
    // must not throw SemaphoreFullException, only ever promise "at least one wake is pending".
    [Fact]
    public void Calling_Enable_repeatedly_before_the_wake_is_consumed_does_not_throw()
    {
        var sut = new ExecutorSwitch(Config());

        var act = () =>
        {
            sut.Enable();
            sut.Enable();
            sut.Enable();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task WaitForWakeAsync_honors_cancellation_when_no_wake_is_pending()
    {
        var sut = new ExecutorSwitch(Config());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.WaitForWakeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
