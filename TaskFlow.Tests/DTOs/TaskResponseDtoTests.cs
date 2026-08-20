using FluentAssertions;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.DTOs;

// Epic 3.1, U3.1: Company now lives on JobApplication instead of being smuggled through
// TaskItem.SourceSection. TaskResponseDto is what actually reaches the frontend's Board cards, so
// this confirms FromEntity carries Company through from the task's parent Application, mirroring
// the existing task.Application?.State.ToString() -> ApplicationState pattern one line away.
public class TaskResponseDtoTests
{
    [Fact]
    public void FromEntity_carries_Company_through_from_the_tasks_parent_application()
    {
        var task = new TaskItem
        {
            Title = "Tailor resume",
            ApplicationId = 7,
            Application = new JobApplication { Id = 7, Company = "Acme Corp" }
        };

        var dto = TaskResponseDto.FromEntity(task);

        dto.Company.Should().Be("Acme Corp");
    }

    [Fact]
    public void FromEntity_leaves_Company_null_when_the_task_has_no_application()
    {
        var task = new TaskItem
        {
            Title = "Ordinary task",
            ApplicationId = null,
            Application = null
        };

        var dto = TaskResponseDto.FromEntity(task);

        dto.Company.Should().BeNull();
    }
}
