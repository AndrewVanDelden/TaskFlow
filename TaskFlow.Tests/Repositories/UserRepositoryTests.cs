using FluentAssertions;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

// Epic 3 Pre-Merge Code Review, finding 6.1: the only repository in the codebase with no matching
// test file at all - every sibling repository (Task, JobApplication, ResumeContext, AgentLog) has one.
public class UserRepositoryTests
{
    private static User NewUser(string email) => new()
    {
        Name = "Test User",
        Email = email,
        PasswordHash = "hashed"
    };

    [Fact]
    public async Task ExistsAsync_is_true_for_a_persisted_user()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new UserRepository(db.Context);
        var user = NewUser("exists@example.com");
        await repo.AddAsync(user);
        await repo.SaveChangesAsync();

        (await repo.ExistsAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_is_false_for_an_unknown_id()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new UserRepository(db.Context);

        (await repo.ExistsAsync(999_999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetByEmailAsync_returns_the_matching_user()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new UserRepository(db.Context);
        var user = NewUser("findme@example.com");
        await repo.AddAsync(user);
        await repo.SaveChangesAsync();

        var found = await repo.GetByEmailAsync("findme@example.com");

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_returns_null_when_no_user_has_that_email()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new UserRepository(db.Context);

        var found = await repo.GetByEmailAsync("nobody@example.com");

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_includes_a_newly_added_user_alongside_the_seed_data()
    {
        using var db = new SqliteInMemoryContext();
        var repo = new UserRepository(db.Context);
        var user = NewUser("roster@example.com");
        await repo.AddAsync(user);
        await repo.SaveChangesAsync();

        var all = await repo.GetAllAsync();

        all.Should().Contain(u => u.Email == "roster@example.com");
    }
}
