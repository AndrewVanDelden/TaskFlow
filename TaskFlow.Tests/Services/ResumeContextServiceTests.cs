using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Services;

public class ResumeContextServiceTests
{
    private readonly Mock<IResumeContextRepository> _repo = new();

    private ResumeContextService CreateSut() => new(_repo.Object);

    // A second save for the same (session, owner) must update the existing row rather than
    // insert a duplicate - otherwise GetForOwnerAsync's FirstOrDefaultAsync could return either
    // row, and a later save could silently stop being the one an agent reads. Exercised against a
    // real repository + SQLite, not a mock, since the bug is specifically about what ends up
    // persisted.
    [Fact]
    public async Task SaveAsync_called_twice_for_the_same_session_and_owner_updates_instead_of_duplicating()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new ResumeContextService(new ResumeContextRepository(db.Context));

        var first = await sut.SaveAsync("session-A", 1, "First draft.", "text");
        var second = await sut.SaveAsync("session-A", 1, "Second draft.", "text");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        var rows = await db.Context.ResumeContexts
            .Where(c => c.IngestionSessionId == "session-A" && c.OwnerId == 1)
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Content.Should().Be("Second draft.");
    }

    // Review (round 2, both manual and Copilot) found the check-then-act upsert isn't race-safe:
    // the unique index (added to fix the original duplicate-row bug) means a losing concurrent
    // insert now throws DbUpdateException instead of silently duplicating - but SaveAsync didn't
    // catch it, so it would surface as an unhandled 500. It should return a clean Result instead.
    // GetForOwnerAsync is called twice here: once before the insert (finds nothing, so we try to
    // insert), once inside the catch to confirm the failure really was a race (finds the winner's
    // row this time) before reporting Conflict.
    [Fact]
    public async Task SaveAsync_returns_Conflict_when_a_concurrent_insert_wins_the_unique_index_race()
    {
        var getForOwnerCallCount = 0;
        _repo.Setup(r => r.GetForOwnerAsync("session-A", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => getForOwnerCallCount++ == 0
                ? null
                : new ResumeContext { IngestionSessionId = "session-A", OwnerId = 1, Content = "Winner's content." });
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("UNIQUE constraint failed"));

        var result = await CreateSut().SaveAsync("session-A", 1, "Base resume text.", "text");

        result.Status.Should().Be(ResultStatus.Conflict);
    }

    // Round 3 (Copilot's automated review): catching DbUpdateException unconditionally and always
    // reporting Conflict would misreport an unrelated persistence failure (DB unavailable, some
    // other constraint) as a concurrency race, hiding the real error. If a re-check finds no row
    // for this exact (session, owner) pair, it wasn't a race - the original exception must
    // propagate, not get swallowed into a misleading Conflict.
    [Fact]
    public async Task SaveAsync_rethrows_when_the_insert_failure_is_not_actually_a_concurrent_row_for_this_pair()
    {
        _repo.Setup(r => r.GetForOwnerAsync("session-A", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResumeContext?)null);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("some unrelated persistence failure"));

        var act = () => CreateSut().SaveAsync("session-A", 1, "Base resume text.", "text");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveAsync_persists_content_and_returns_Ok_true()
    {
        var result = await CreateSut().SaveAsync("session-A", 1, "Base resume text.", "text");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _repo.Verify(r => r.AddAsync(
            It.Is<ResumeContext>(c =>
                c.IngestionSessionId == "session-A" &&
                c.OwnerId == 1 &&
                c.Content == "Base resume text." &&
                c.ContentFormat == "text"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Round 4 (Copilot's automated review): contentFormat ?? "text" only defaults a null value -
    // an empty or whitespace-only ContentFormat (still a valid string, so it passes null-coalescing
    // unchanged) would be persisted as-is, polluting what's meant to be an enum-like discriminator
    // ("text"/"markdown") with a meaningless value. Covers both the insert and the update path.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_defaults_ContentFormat_to_text_when_null_empty_or_whitespace_on_insert(string? contentFormat)
    {
        var result = await CreateSut().SaveAsync("session-A", 1, "Base resume text.", contentFormat);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.AddAsync(
            It.Is<ResumeContext>(c => c.ContentFormat == "text"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_defaults_ContentFormat_to_text_when_null_empty_or_whitespace_on_update(string? contentFormat)
    {
        using var db = new SqliteInMemoryContext();
        var sut = new ResumeContextService(new ResumeContextRepository(db.Context));
        await sut.SaveAsync("session-A", 1, "First draft.", "text");

        var result = await sut.SaveAsync("session-A", 1, "Second draft.", contentFormat);

        result.IsSuccess.Should().BeTrue();
        var row = await db.Context.ResumeContexts
            .SingleAsync(c => c.IngestionSessionId == "session-A" && c.OwnerId == 1);
        row.ContentFormat.Should().Be("text");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_rejects_null_or_blank_session_id(string? sessionId)
    {
        var result = await CreateSut().SaveAsync(sessionId!, 1, "Base resume text.", "text");

        result.Status.Should().Be(ResultStatus.Validation);
        _repo.Verify(r => r.AddAsync(It.IsAny<ResumeContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_rejects_null_or_blank_content(string? content)
    {
        var result = await CreateSut().SaveAsync("session-A", 1, content!, "text");

        result.Status.Should().Be(ResultStatus.Validation);
        _repo.Verify(r => r.AddAsync(It.IsAny<ResumeContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_rejects_content_over_20000_characters()
    {
        var tooLong = new string('a', 20001);

        var result = await CreateSut().SaveAsync("session-A", 1, tooLong, "text");

        result.Status.Should().Be(ResultStatus.Validation);
        _repo.Verify(r => r.AddAsync(It.IsAny<ResumeContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
