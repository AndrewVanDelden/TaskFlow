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
}
