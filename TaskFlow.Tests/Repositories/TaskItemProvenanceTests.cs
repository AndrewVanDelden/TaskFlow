using FluentAssertions;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskItemProvenanceTests
{
    [Fact]
    public async Task Round_trips_kind_provenance_and_owner()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new TaskRepository(db.Context);

        var task = new TaskItem
        {
            Title = "Ingested task",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.Generic,
            SourceName = "spec.md",
            SourceSection = "Backend",
            ClaimedBy = "GenericExecutor"
        };
        await repo.AddAsync(task);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(task.Id);

        loaded!.Kind.Should().Be(TaskKind.Generic);
        loaded.SourceName.Should().Be("spec.md");
        loaded.SourceSection.Should().Be("Backend");
        loaded.ClaimedBy.Should().Be("GenericExecutor");
    }
}
