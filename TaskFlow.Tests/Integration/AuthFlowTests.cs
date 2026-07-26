using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Tests.Integration;

/// <summary>
/// HTTP-level integration tests: they boot the real app (routing, auth middleware, DI wiring,
/// JSON, the Result -> status mapping) and talk to it over HttpClient. One shared factory (and
/// one throwaway DB) is reused across the class via IClassFixture.
/// </summary>
public class AuthFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public AuthFlowTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Protected_endpoint_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Tasks");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_then_login_then_call_protected_endpoint_succeeds()
    {
        var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.dev";

        var register = await client.PostAsJsonAsync("/api/Auth/register",
            new { name = "Ada", email, password = "password1" });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/Auth/login",
            new { email, password = "password1" });
        login.EnsureSuccessStatusCode();

        var auth = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        auth.Should().NotBeNull();
        auth!.Token.Should().NotBeNullOrWhiteSpace();

        // The token issued by the app must validate against the app's own JWT middleware.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var tasks = await client.GetAsync("/api/Tasks");

        tasks.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_with_a_short_password_returns_400()
    {
        var client = _factory.CreateClient();

        // Password below the 8-char minimum: [ApiController] model validation rejects it
        // before the action runs, so no user is created.
        var response = await client.PostAsJsonAsync("/api/Auth/register",
            new { name = "Ada", email = "shortpw@example.dev", password = "short" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
