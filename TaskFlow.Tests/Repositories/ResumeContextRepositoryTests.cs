using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class ResumeContextRepositoryTests
{
    // The (IngestionSessionId, OwnerId) uniqueness is enforced structurally at the DB level, not
    // just by ResumeContextService's upsert - a concurrent request racing the service's
    // check-then-act could still land two inserts without this. Written directly against the
    // repository, bypassing the service, so it proves the schema itself refuses the duplicate.
    [Fact]
    public async Task Adding_a_second_row_for_the_same_session_and_owner_violates_the_unique_index()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        await repo.AddAsync(new ResumeContext { IngestionSessionId = "session-A", OwnerId = 1, Content = "First." });
        await repo.SaveChangesAsync();

        await repo.AddAsync(new ResumeContext { IngestionSessionId = "session-A", OwnerId = 1, Content = "Second." });
        var act = () => repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Persisted_resume_context_round_trips_via_GetForOwnerAsync()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        var context = new ResumeContext
        {
            IngestionSessionId = "session-A",
            OwnerId = 1,
            Content = "Base resume text goes here."
        };
        await repo.AddAsync(context);
        await repo.SaveChangesAsync();

        var fetched = await repo.GetForOwnerAsync("session-A", 1);

        fetched.Should().NotBeNull();
        fetched!.Content.Should().Be("Base resume text goes here.");
    }

    [Fact]
    public async Task Deleting_a_resume_context_makes_it_unreadable_afterward()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        var context = new ResumeContext
        {
            IngestionSessionId = "session-A",
            OwnerId = 1,
            Content = "Base resume text goes here."
        };
        await repo.AddAsync(context);
        await repo.SaveChangesAsync();

        var deleted = await repo.DeleteForOwnerAsync("session-A", 1);
        var fetchedAfterDelete = await repo.GetForOwnerAsync("session-A", 1);

        deleted.Should().BeTrue();
        fetchedAfterDelete.Should().BeNull();
    }

    [Fact]
    public async Task Wrong_owner_cannot_read_or_delete_another_owners_session()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        var context = new ResumeContext
        {
            IngestionSessionId = "session-A",
            OwnerId = 1,
            Content = "Base resume text goes here."
        };
        await repo.AddAsync(context);
        await repo.SaveChangesAsync();

        var readAsWrongOwner = await repo.GetForOwnerAsync("session-A", ownerId: 2);
        var deletedAsWrongOwner = await repo.DeleteForOwnerAsync("session-A", ownerId: 2);
        var stillReadableByRealOwner = await repo.GetForOwnerAsync("session-A", ownerId: 1);

        readAsWrongOwner.Should().BeNull();
        deletedAsWrongOwner.Should().BeFalse();
        stillReadableByRealOwner.Should().NotBeNull();
        stillReadableByRealOwner!.Content.Should().Be("Base resume text goes here.");
    }
}
