using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Services;

public class DailyExecutorSpendGuardTests
{
    private static IConfiguration Config(int cap) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agents:DailyExecutorTaskCap"] = cap.ToString()
        }).Build();

    private static async Task SeedClaimsTodayAsync(SqliteInMemoryContext db, int count)
    {
        for (var i = 0; i < count; i++)
            db.Context.AgentLogs.Add(new AgentLog
            {
                AgentName = AgentNames.GenericExecutor,
                Action = AgentActions.Claimed,
                Success = true,
                CreatedAt = DateTime.UtcNow
            });
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Allows_running_below_the_cap()
    {
        using var db = new SqliteInMemoryContext();
        await SeedClaimsTodayAsync(db, 2);
        var sut = new DailyExecutorSpendGuard(new AgentLogRepository(db.Context), Config(cap: 3));

        (await sut.CanRunAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Blocks_running_at_the_cap()
    {
        using var db = new SqliteInMemoryContext();
        await SeedClaimsTodayAsync(db, 3);
        var sut = new DailyExecutorSpendGuard(new AgentLogRepository(db.Context), Config(cap: 3));

        (await sut.CanRunAsync()).Should().BeFalse();
    }
}
