using FluentAssertions;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Models;

// Epic 3 Pre-Merge Code Review, finding 1.1: SignalR broadcasts must be scoped to the owning
// user for Epic 3 sibling tasks. OwnerId is the single place that derives "who owns this task"
// so every broadcast call site uses the same rule instead of re-deriving it.
public class TaskItemOwnerIdTests
{
    [Fact]
    public void OwnerId_is_null_for_a_generic_task_with_no_ApplicationId()
    {
        var task = new TaskItem { ApplicationId = null };

        task.OwnerId.Should().BeNull();
    }

    [Fact]
    public void OwnerId_is_the_applications_owner_when_the_navigation_is_loaded()
    {
        var task = new TaskItem
        {
            ApplicationId = 7,
            Application = new JobApplication { Id = 7, OwnerId = 42 }
        };

        task.OwnerId.Should().Be(42);
    }

    // Fail closed, not open: if a caller forgot to Include(Application) for a task that has an
    // ApplicationId, silently returning null would broadcast a personal Epic 3 event to everyone
    // instead of just the owner - the exact bug this property exists to prevent.
    [Fact]
    public void OwnerId_throws_when_ApplicationId_is_set_but_Application_navigation_was_not_loaded()
    {
        var task = new TaskItem { ApplicationId = 7, Application = null };

        var act = () => task.OwnerId;

        act.Should().Throw<InvalidOperationException>();
    }
}
