using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Repositories;

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

    // ── Sprint 4R: resume-context read-back, approve, and the cross-session guard ──────────────
    // No test-only "mark ReviewReady" endpoint exists. Getting a real ReviewReady application
    // through the actual HTTP surface: assemble (creates two Todo siblings via the real endpoint),
    // move both siblings to Review via the existing generic PATCH /api/Tasks/{id}/status endpoint,
    // then invoke the already-covered reconciliation sweep
    // (IJobApplicationRepository.PromotePendingReviewReadyApplicationsAsync, proven atomic and
    // correct by JobApplicationRepositoryPromotionTests) directly against the factory's DI
    // container - a real production code path, not raw SQL, and cheaper than standing up the
    // Claude-backed tailoring agents just to drive two tasks to Review.

    [Fact]
    public async Task Reading_resume_context_back_then_approving_moves_both_siblings_to_Done_and_the_application_to_Approved()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });

        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = "Great role", section = "Job Posting" }
            });
        var application = await assemble.Content.ReadFromJsonAsync<JobApplicationDto>();

        foreach (var task in application!.Tasks)
        {
            var move = await client.PatchAsJsonAsync($"/api/Tasks/{task.Id}/status", new { status = "Review" });
            move.EnsureSuccessStatusCode();
        }
        await PromoteToReviewReadyAsync();

        var getResumeContext = await client.GetAsync($"/api/JobApplications/{application.Id}/resume-context");
        getResumeContext.StatusCode.Should().Be(HttpStatusCode.OK);
        var baseResume = await getResumeContext.Content.ReadFromJsonAsync<string>();
        baseResume.Should().Be("Base resume text.");

        var approve = await client.PostAsync($"/api/JobApplications/{application.Id}/approve", null);

        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approve.Content.ReadFromJsonAsync<JobApplicationDto>();
        approved!.State.Should().Be("Approved");
        approved.Tasks.Should().HaveCount(2);
        approved.Tasks.Should().OnlyContain(t => t.Status == "Done");
    }

    [Fact]
    public async Task A_cross_session_approve_attempt_returns_404()
    {
        var owner = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        await owner.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        var assemble = await owner.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = "Great role", section = "Job Posting" }
            });
        var application = await assemble.Content.ReadFromJsonAsync<JobApplicationDto>();

        foreach (var task in application!.Tasks)
        {
            var move = await owner.PatchAsJsonAsync($"/api/Tasks/{task.Id}/status", new { status = "Review" });
            move.EnsureSuccessStatusCode();
        }
        await PromoteToReviewReadyAsync();

        var otherUser = await AuthedClientAsync();

        var approve = await otherUser.PostAsync($"/api/JobApplications/{application.Id}/approve", null);

        approve.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Copilot's automated review (PR #45): [Required] on RejectTaskDto.Reason lets a whitespace-
    // only reason through model validation. Proves the real HTTP-level 400, not just the service
    // unit test.
    [Fact]
    public async Task Rejecting_with_a_whitespace_only_reason_returns_400()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        var assemble = await client.PostAsJsonAsync("/api/JobApplications",
            new
            {
                ingestionSessionId = sessionId,
                posting = new { title = "Backend Engineer", description = "Great role", section = "Job Posting" }
            });
        var application = await assemble.Content.ReadFromJsonAsync<JobApplicationDto>();

        foreach (var task in application!.Tasks)
        {
            var move = await client.PatchAsJsonAsync($"/api/Tasks/{task.Id}/status", new { status = "Review" });
            move.EnsureSuccessStatusCode();
        }
        await PromoteToReviewReadyAsync();

        var reject = await client.PostAsJsonAsync($"/api/JobApplications/{application.Id}/reject", new { reason = "   " });

        reject.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task PromoteToReviewReadyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var jobApplications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();
        await jobApplications.PromotePendingReviewReadyApplicationsAsync();
    }

    // Local shapes: Kind as a plain string so the test's default deserializer does not choke.
    private sealed record DraftDto(string Title, string? Description, string Kind, string? Section);
    private sealed record TaskSummaryDto(int Id, string Title, string Kind, string Status);
    private sealed record JobApplicationDto(int Id, string State, List<TaskSummaryDto> Tasks);
}
