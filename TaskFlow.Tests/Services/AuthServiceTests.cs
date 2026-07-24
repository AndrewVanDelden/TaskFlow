using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();

    // JwtService has no interface and its token generation is pure, so use a real one
    // backed by throwaway in-memory config rather than trying to mock a concrete class.
    private static JwtService CreateJwt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-at-least-32-characters-long!!",
                ["Jwt:Issuer"] = "TaskFlowApi",
                ["Jwt:Audience"] = "TaskFlowClient",
                ["Jwt:ExpiryHours"] = "8"
            }).Build();
        return new JwtService(config);
    }

    private AuthService CreateSut() => new(_users.Object, CreateJwt());

    // ── Register ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Register_returns_Conflict_when_email_is_taken()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new User { Email = "ada@x.dev" });

        var result = await CreateSut().RegisterAsync(new RegisterDto
        {
            Name = "Ada", Email = "ADA@x.dev", Password = "password1"
        });

        result.Status.Should().Be(ResultStatus.Conflict);
        _users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_creates_the_user_and_returns_a_token()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var result = await CreateSut().RegisterAsync(new RegisterDto
        {
            Name = "Ada", Email = "ada@x.dev", Password = "password1"
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().NotBeNullOrWhiteSpace();
        _users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Login ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_is_Unauthorized_when_the_email_is_unknown()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        var result = await CreateSut().LoginAsync(new LoginDto { Email = "nobody@x.dev", Password = "pw" });

        result.Status.Should().Be(ResultStatus.Unauthorized);
    }

    [Fact]
    public async Task Login_is_Unauthorized_when_the_password_is_wrong()
    {
        var user = new User
        {
            Email = "ada@x.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password", workFactor: 12)
        };
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await CreateSut().LoginAsync(new LoginDto { Email = "ada@x.dev", Password = "wrong-password" });

        result.Status.Should().Be(ResultStatus.Unauthorized);
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var user = new User
        {
            Name = "Ada",
            Email = "ada@x.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password", workFactor: 12)
        };
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        var result = await CreateSut().LoginAsync(new LoginDto { Email = "ada@x.dev", Password = "correct-password" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().NotBeNullOrWhiteSpace();
    }
}
