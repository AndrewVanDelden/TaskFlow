using FluentAssertions;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryTests
{
    [Fact]
    public async Task AddAsync_then_GetByIdAsync_roundtrips_a_task()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var task = new TaskItem { Title = "Write tests" };
        await sut.AddAsync(task);
        await sut.SaveChangesAsync();

        var found = await sut.GetByIdAsync(task.Id);
        found.Should().NotBeNull();
        found!.Title.Should().Be("Write tests");
    }

    [Fact]
    public async Task GetStaleAsync_returns_only_open_tasks_older_than_cutoff()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var cutoff = DateTime.UtcNow.AddHours(-48);
        await sut.AddAsync(new TaskItem { Title = "fresh", UpdatedAt = DateTime.UtcNow });
        await sut.AddAsync(new TaskItem { Title = "stale", UpdatedAt = cutoff.AddHours(-1) });
        await sut.AddAsync(new TaskItem { Title = "done-stale", Status = WorkflowStatus.Done, UpdatedAt = cutoff.AddHours(-1) });
        await sut.SaveChangesAsync();

        var stale = await sut.GetStaleAsync(cutoff);

        stale.Should().ContainSingle(t => t.Title == "stale");
    }
}