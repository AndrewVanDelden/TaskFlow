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

    // PR #40 review (round 2, both manual and Copilot): JobPostingSummaryDto had no MaxLength
    // caps matching TaskItem's own persistence limits (Title 200, Description 2000,
    // SourceSection 200), so an oversized value would bypass model validation entirely. Proved
    // at the HTTP level, since that's where [ApiController]'s automatic model validation acts.
    [Fact]
    public async Task Assembling_with_an_oversized_posting_title_returns_400()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = new string('A', 201), section = "Job Posting" }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assembling_with_an_oversized_posting_description_returns_400()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = new string('A', 2001), section = "Job Posting" }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assembling_with_an_oversized_posting_section_returns_400()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", section = new string('A', 201) }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Round 4 (Copilot's automated review): Section is typed as a non-nullable string, but with no
    // [Required], a client sending an explicit JSON null passes model validation anyway and Section
    // ends up actually null at runtime - a real gap between the C# type and what the wire actually
    // allows. Sent as a real null (not omitted), matching what Copilot's finding described.
    [Fact]
    public async Task Assembling_with_an_explicit_null_posting_section_returns_400()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", section = (string?)null }
            });

        assemble.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Local shapes: Kind as a plain string so the test's default deserializer does not choke.
    private sealed record DraftDto(string Title, string? Description, string Kind, string? Section);
    private sealed record JobApplicationDto(int Id, string State, List<object> Tasks);
}
