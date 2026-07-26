using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class JwtServiceTests
{
    // Builds a JwtService backed by throwaway in-memory config — no appsettings,
    // no file system. This is "mock the dependency" in its simplest form.
    private static JwtService CreateSut(int expiryHours = 8)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-at-least-32-characters-long!!",
                ["Jwt:Issuer"] = "TaskFlowApi",
                ["Jwt:Audience"] = "TaskFlowClient",
                ["Jwt:ExpiryHours"] = expiryHours.ToString()
            })
            .Build();

        return new JwtService(config);
    }

    private static User SampleUser() => new()
    {
        Id = 42,
        Name = "Ada Lovelace",
        Email = "ada@taskflow.dev",
        PasswordHash = "irrelevant"
    };

    [Fact]
    public void GenerateToken_returns_a_nonempty_token()
    {
        var sut = CreateSut();

        var result = sut.GenerateToken(SampleUser());

        result.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateToken_embeds_the_user_id_and_email_as_claims()
    {
        var sut = CreateSut();

        var result = sut.GenerateToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "ada@taskflow.dev");
    }

    [Fact]
    public void GenerateToken_sets_expiry_to_configured_hours_from_now()
    {
        var sut = CreateSut(expiryHours: 8);
        var before = DateTime.UtcNow;

        var result = sut.GenerateToken(SampleUser());

        // Allow a minute of slack so the test is not flaky on a slow machine.
        result.ExpiresAt.Should().BeCloseTo(before.AddHours(8), TimeSpan.FromMinutes(1));
    }
}