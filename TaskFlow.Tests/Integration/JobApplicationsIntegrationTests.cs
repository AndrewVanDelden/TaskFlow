using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Tests.Integration;

/// <summary>
/// HTTP-level tests for the new JobApplications endpoints (parse, resume-context, assemble)
/// through the real routing + auth + Result-to-status mapping. Same shared factory/DB pattern as
/// TaskWorkflowIntegrationTests.
/// </summary>
[Collection("Integration")]
public class JobApplicationsIntegrationTests
{
    private readonly TestWebAppFactory _factory;
    public JobApplicationsIntegrationTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<HttpClient> AuthedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.dev";
        await client.PostAsJsonAsync("/api/Auth/register", new { name = "User", email, password = "password1" });
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { email, password = "password1" });
        var auth = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    [Fact]
    public async Task Parse_returns_200_with_a_draft_for_a_heading_bearing_posting()
    {
        var client = await AuthedClientAsync();

        var parse = await client.PostAsJsonAsync("/api/JobApplications/parse",
            new { content = "# Backend Engineer\n## Acme Corp\nBuild things." });

        parse.StatusCode.Should().Be(HttpStatusCode.OK);
        var drafts = await parse.Content.ReadFromJsonAsync<List<DraftDto>>();
        drafts.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Saving_resume_context_then_assembling_creates_an_application_with_two_tasks()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        var saveResumeContext = await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        saveResumeContext.StatusCode.Should().Be(HttpStatusCode.OK);

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = "Great role", section = "Job Posting" }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var application = await assemble.Content.ReadFromJsonAsync<JobApplicationDto>();
        application.Should().NotBeNull();
        application!.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task Assembling_without_a_saved_resume_context_returns_404()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = "Great role", section = "Job Posting" }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Local shapes: Kind as a plain string so the test's default deserializer does not choke.
    private sealed record DraftDto(string Title, string? Description, string Kind, string? Section);
    private sealed record JobApplicationDto(int Id, string State, List<object> Tasks);
}
