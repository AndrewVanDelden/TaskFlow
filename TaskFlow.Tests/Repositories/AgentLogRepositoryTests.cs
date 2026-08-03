using FluentAssertions;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class AgentLogRepositoryTests
{
    private static AgentLog Log(int taskId, string action, string details, DateTime at) => new()
    {
        AgentName = "Reviewer",
        Action = action,
        TaskId = taskId,
        Details = details,
        Success = false,
        CreatedAt = at,
    };

    [Fact]
    public async Task GetByTaskAndAction_returns_matching_logs_newest_first()
    {
        using var db = new SqliteInMemoryContext();
        db.Context.AgentLogs.AddRange(
            Log(1, "Rejected", "first", new DateTime(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc)),
            Log(1, "Rejected", "second", new DateTime(2026, 7, 28, 11, 0, 0, DateTimeKind.Utc)),
            Log(2, "Rejected", "other task", new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc)),
            Log(1, "Claimed", "not a rejection", new DateTime(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc)));
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var result = await repo.GetByTaskAndActionAsync(1, "Rejected", 10);

        result.Select(l => l.Details).Should().Equal("second", "first"); // task 1, Rejected only, newest-first
    }
}
