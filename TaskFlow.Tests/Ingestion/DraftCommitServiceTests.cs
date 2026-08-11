using FluentAssertions;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class DraftCommitServiceTests
{
    [Fact]
    public async Task Commits_each_draft_as_a_Todo_task_with_provenance()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new TaskRepository(db.Context);
        var service = new DraftCommitService(repo);

        var drafts = new[]
        {
            new TaskDraft("Wire auth", "JWT", TaskKind.Generic, "Backend"),
            new TaskDraft("Build board", null, TaskKind.Generic, "Frontend"),
        };

        var result = await service.CommitAsync("spec.md", drafts);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        // The context is seeded with other tasks, so assert on the ones we just committed.
        var committed = (await repo.GetAllAsync(null, null, callerId: 1))
            .Where(t => t.SourceName == "spec.md")
            .ToList();

        committed.Should().HaveCount(2);
        committed.Should().OnlyContain(t => t.Status == WorkflowStatus.Todo);
        committed.Should().Contain(t => t.Title == "Wire auth" && t.SourceSection == "Backend");
    }
}
