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

    [Fact]
    public async Task SaveAsync_defaults_ContentFormat_to_text_when_null()
    {
        var result = await CreateSut().SaveAsync("session-A", 1, "Base resume text.", null);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.AddAsync(
            It.Is<ResumeContext>(c => c.ContentFormat == "text"),
            It.IsAny<CancellationToken>()), Times.Once);
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
