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

    // ── Sprint 6: GetMostRecentForOwnerAsync — owner-only read, no session id ──────────────────
    // New query shape: "the caller's own most recently saved resume, from any session" (needed so
    // the intake UI can offer "reuse your last resume" instead of forcing a re-paste). Unlike
    // GetForOwnerAsync, there is no session-id dimension to scope on, so ownership scoping rests
    // entirely on the OwnerId filter — the second test below is the one that actually proves that.

    [Fact]
    public async Task GetMostRecentForOwnerAsync_returns_the_most_recently_updated_row_for_that_owner()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        await repo.AddAsync(new ResumeContext
        {
            IngestionSessionId = "session-A",
            OwnerId = 1,
            Content = "Older draft.",
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await repo.AddAsync(new ResumeContext
        {
            IngestionSessionId = "session-B",
            OwnerId = 1,
            Content = "Newer draft.",
            UpdatedAt = DateTime.UtcNow
        });
        await repo.SaveChangesAsync();

        var mostRecent = await repo.GetMostRecentForOwnerAsync(ownerId: 1);

        mostRecent.Should().NotBeNull();
        mostRecent!.Content.Should().Be("Newer draft.");
    }

    // This is the test that actually proves the ownership scoping, not just the ordering: owner
    // 2's row is newer, but a lookup for owner 1 must never return it.
    [Fact]
    public async Task GetMostRecentForOwnerAsync_never_returns_another_owners_row_even_if_it_is_newer()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        await repo.AddAsync(new ResumeContext
        {
            IngestionSessionId = "session-A",
            OwnerId = 1,
            Content = "Owner 1's older draft.",
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await repo.AddAsync(new ResumeContext
        {
            IngestionSessionId = "session-B",
            OwnerId = 2,
            Content = "Owner 2's newer draft.",
            UpdatedAt = DateTime.UtcNow
        });
        await repo.SaveChangesAsync();

        var mostRecentForOwner1 = await repo.GetMostRecentForOwnerAsync(ownerId: 1);

        mostRecentForOwner1.Should().NotBeNull();
        mostRecentForOwner1!.Content.Should().Be("Owner 1's older draft.");
    }

    [Fact]
    public async Task GetMostRecentForOwnerAsync_returns_null_when_the_owner_has_no_saved_resume()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new ResumeContextRepository(db.Context);

        var mostRecent = await repo.GetMostRecentForOwnerAsync(ownerId: 1);

        mostRecent.Should().BeNull();
    }
}
