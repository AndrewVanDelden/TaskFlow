using FluentAssertions;
using Xunit;

namespace TaskFlow.Tests.TestSupport;

// TEMPORARY: proves the runner, xUnit, and FluentAssertions are wired up.
// Deleted in Slice B1 once the first real test exists.
public class HarnessSmokeTest
{
    [Fact]
    public void Harness_is_working()
    {
        var sum = 2 + 2;
        sum.Should().Be(4);
    }
}