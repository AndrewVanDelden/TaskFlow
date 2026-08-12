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
    private readonly Mock<IJobApplicationRepository> _applications = new();

    private ResumeContextService CreateSut() => new(_repo.Object, _applications.Object);

    // A second save for the same (session, owner) must update the existing row rather than
    // insert a duplicate - otherwise GetForOwnerAsync's FirstOrDefaultAsync could return either
    // row, and a later save could silently stop being the one an agent reads. Exercised against a
    // real repository + SQLite, not a mock, since the bug is specifically about what ends up
    // persisted.
    [Fact]
    public async Task SaveAsync_called_twice_for_the_same_session_and_owner_updates_instead_of_duplicating()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new ResumeContextService(new ResumeContextRepository(db.Context), Mock.Of<IJobApplicationRepository>());

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
        var sut = new ResumeContextService(new ResumeContextRepository(db.Context), Mock.Of<IJobApplicationRepository>());
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

    // ── GetForApplicationAsync (Sprint 4R: reads a base resume back for the paired review) ──────
    // ResumeContextService gains a dependency on IJobApplicationRepository to resolve
    // applicationId -> (IngestionSessionId, OwnerId) before reading the existing
    // ownership-scoped IResumeContextRepository.GetForOwnerAsync lookup.

    [Fact]
    public async Task GetForApplicationAsync_returns_the_saved_base_resume_content()
    {
        _applications.Setup(a => a.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobApplication { Id = 5, OwnerId = 1, IngestionSessionId = "session-A" });
        _repo.Setup(r => r.GetForOwnerAsync("session-A", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeContext { IngestionSessionId = "session-A", OwnerId = 1, Content = "Base resume text." });

        var result = await CreateSut().GetForApplicationAsync(5, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Base resume text.");
    }

    [Fact]
    public async Task GetForApplicationAsync_returns_NotFound_when_the_application_does_not_exist()
    {
        _applications.Setup(a => a.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync((JobApplication?)null);

        var result = await CreateSut().GetForApplicationAsync(5, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // IDOR-safe convention: a cross-owner probe must be indistinguishable from a genuine 404.
    [Fact]
    public async Task GetForApplicationAsync_returns_NotFound_when_the_application_is_owned_by_someone_else()
    {
        _applications.Setup(a => a.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobApplication { Id = 5, OwnerId = 999, IngestionSessionId = "session-A" });

        var result = await CreateSut().GetForApplicationAsync(5, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _repo.Verify(r => r.GetForOwnerAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetForApplicationAsync_returns_NotFound_when_no_resume_context_has_been_saved_yet()
    {
        _applications.Setup(a => a.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobApplication { Id = 5, OwnerId = 1, IngestionSessionId = "session-A" });
        _repo.Setup(r => r.GetForOwnerAsync("session-A", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResumeContext?)null);

        var result = await CreateSut().GetForApplicationAsync(5, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ── GetMostRecentForCallerAsync (Sprint 6: "reuse your last resume" for the intake UI) ──────
    // Pure mapping logic over IResumeContextRepository.GetMostRecentForOwnerAsync, so mocked like
    // the rest of this file's non-persistence-behavior tests.

    [Fact]
    public async Task GetMostRecentForCallerAsync_returns_NotFound_when_the_repository_has_nothing()
    {
        _repo.Setup(r => r.GetMostRecentForOwnerAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResumeContext?)null);

        var result = await CreateSut().GetMostRecentForCallerAsync(callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetMostRecentForCallerAsync_maps_the_found_context_to_the_summary_dto()
    {
        var updatedAt = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        _repo.Setup(r => r.GetMostRecentForOwnerAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeContext
            {
                IngestionSessionId = "session-A",
                OwnerId = 1,
                Content = "Base resume text.",
                ContentFormat = "markdown",
                UpdatedAt = updatedAt
            });

        var result = await CreateSut().GetMostRecentForCallerAsync(callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Base resume text.");
        result.Value.ContentFormat.Should().Be("markdown");
        result.Value.UpdatedAt.Should().Be(updatedAt);
    }
}
