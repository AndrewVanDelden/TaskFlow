using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Common;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Export;
using TaskFlow.Api.Ingestion;
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

    private Task<HttpClient> AuthedClientAsync() => AuthedClientAsync(_factory);

    private async Task<HttpClient> AuthedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
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

    // ── Epic 3.2 Sprint 1 (S1.6): POST /api/JobApplications/parse-url ──────────────────────────
    // IJobPostingUrlFetcher is swapped for a fake at the test boundary (same pattern as
    // WithFakeTypstCompiler below) so these tests never make a real HTTP/DNS/socket call - the
    // fetcher's own SSRF mitigations are already covered by UrlValidationTests and
    // JobPostingUrlFetcherTests. This endpoint does not exist yet, so both tests are expected to
    // fail with a 404 until the controller action + DI wiring are added.

    [Fact]
    public async Task ParseUrl_returns_200_with_a_draft_for_a_url_that_resolves_to_a_heading_bearing_posting()
    {
        var fakeFactory = WithFakeJobPostingUrlFetcher(_factory,
            Result<string>.Ok("# Backend Engineer\n## Acme Corp\nBuild things."));
        var client = await AuthedClientAsync(fakeFactory);

        var parse = await client.PostAsJsonAsync("/api/JobApplications/parse-url",
            new { url = "https://example.com/job-posting" });

        parse.StatusCode.Should().Be(HttpStatusCode.OK);
        var drafts = await parse.Content.ReadFromJsonAsync<List<DraftDto>>();
        drafts.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseUrl_with_a_fetcher_rejection_returns_400_not_500()
    {
        var fakeFactory = WithFakeJobPostingUrlFetcher(_factory,
            Result<string>.Invalid("URL rejected: scheme not allowed."));
        var client = await AuthedClientAsync(fakeFactory);

        var parse = await client.PostAsJsonAsync("/api/JobApplications/parse-url",
            new { url = "https://example.com/job-posting" });

        parse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    // ── Sprint 6: GET /resume-context/latest — "reuse your last resume" for the intake UI ────────
    // Route ordering note: "resume-context" as a literal first path segment is never matched by
    // the existing [HttpGet("{id:int}/resume-context")] action's {id:int} constraint, so this new
    // route disambiguates cleanly without any special ordering.

    [Fact]
    public async Task GetMostRecentResumeContext_returns_404_when_the_caller_has_never_saved_a_resume()
    {
        var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/JobApplications/resume-context/latest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMostRecentResumeContext_returns_the_callers_own_most_recent_resume()
    {
        var client = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        var save = await client.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/JobApplications/resume-context/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<ResumeContextSummaryDto>();
        summary.Should().NotBeNull();
        summary!.Content.Should().Be("Base resume text.");
    }

    [Fact]
    public async Task GetMostRecentResumeContext_returns_401_without_a_bearer_token()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/JobApplications/resume-context/latest");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task PromoteToReviewReadyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var jobApplications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();
        await jobApplications.PromotePendingReviewReadyApplicationsAsync();
    }

    // ── Sprint 5: artifact export (T5.2) ────────────────────────────────────────
    // No HTTP endpoint writes TailoredContent - only the Claude-backed tailoring agents do, and
    // standing those up for a test is unnecessary per this file's existing precedent
    // (PromoteToReviewReadyAsync above reaches into the DI container directly rather than driving
    // the reconciliation sweep through HTTP). Same pattern here: a scoped AppDbContext sets
    // TailoredContent directly on both sibling TaskItems.
    private async Task SetTailoredContentAsync(int taskId, string content)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.FirstAsync(t => t.Id == taskId);
        task.TailoredContent = content;
        await db.SaveChangesAsync();
    }

    // Assembles a real application, sets TailoredContent on both siblings, and drives them to
    // ReviewReady via the real HTTP status-update endpoint plus the existing reconciliation-sweep
    // repository call - stops short of Approved so wrong-state tests can use it directly.
    private async Task<(HttpClient client, int applicationId)> ReviewReadyApplicationAsync(
        WebApplicationFactory<Program> factory,
        string resumeContent = "Tailored resume body.",
        string coverLetterContent = "Tailored cover letter body.")
    {
        var client = await AuthedClientAsync(factory);
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
            var content = task.Kind == "ResumeTailoring" ? resumeContent : coverLetterContent;
            await SetTailoredContentAsync(task.Id, content);

            var move = await client.PatchAsJsonAsync($"/api/Tasks/{task.Id}/status", new { status = "Review" });
            move.EnsureSuccessStatusCode();
        }
        await PromoteToReviewReadyAsync();

        return (client, application.Id);
    }

    private async Task<(HttpClient client, int applicationId)> ApprovedApplicationAsync(
        WebApplicationFactory<Program> factory,
        string resumeContent = "Tailored resume body.",
        string coverLetterContent = "Tailored cover letter body.")
    {
        var (client, applicationId) = await ReviewReadyApplicationAsync(factory, resumeContent, coverLetterContent);

        var approve = await client.PostAsync($"/api/JobApplications/{applicationId}/approve", null);
        approve.EnsureSuccessStatusCode();

        return (client, applicationId);
    }

    // Derived factory with a fake ITypstCompiler for the pdf-format cases: the real
    // ProcessTypstCompiler shells out to the `typst` binary, which is not installed on this
    // machine, per Sprint 5's testing-strategy decision. Markdown-format cases never reach the
    // compiler at all, so they use the plain _factory - proving, structurally, that the real
    // (missing) binary is never invoked for that path.
    private static WebApplicationFactory<Program> WithFakeTypstCompiler(TestWebAppFactory factory) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<ITypstCompiler, FakeTypstCompiler>()));

    // Epic 3.2 Sprint 1 (S1.6): swaps the real IJobPostingUrlFetcher for a fake whose canned
    // Result<string> drives the test outcome directly, matching WithFakeTypstCompiler's exact
    // shape above.
    private static WebApplicationFactory<Program> WithFakeJobPostingUrlFetcher(TestWebAppFactory factory, Result<string> fetchResult) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IJobPostingUrlFetcher>(_ => new FakeJobPostingUrlFetcher(fetchResult))));

    [Theory]
    [InlineData("resume", "Resume.pdf")]
    [InlineData("cover-letter", "Cover_Letter.pdf")]
    public async Task Export_pdf_returns_200_with_correct_headers_for_an_owned_Approved_application(string route, string expectedFileName)
    {
        var fakeFactory = WithFakeTypstCompiler(_factory);
        var (client, applicationId) = await ApprovedApplicationAsync(fakeFactory);

        var response = await client.GetAsync($"/api/JobApplications/{applicationId}/export/{route}?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain(expectedFileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("resume", "Resume.md")]
    [InlineData("cover-letter", "Cover_Letter.md")]
    public async Task Export_markdown_returns_200_with_correct_headers_for_an_owned_Approved_application(string route, string expectedFileName)
    {
        var (client, applicationId) = await ApprovedApplicationAsync(_factory,
            resumeContent: "Resume markdown body.", coverLetterContent: "Cover letter markdown body.");

        var response = await client.GetAsync($"/api/JobApplications/{applicationId}/export/{route}?format=markdown");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/markdown");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain(expectedFileName);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(route == "resume" ? "Resume markdown body." : "Cover letter markdown body.");
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("cover-letter")]
    public async Task Export_returns_404_for_a_different_owner(string route)
    {
        var (_, applicationId) = await ApprovedApplicationAsync(_factory);
        var otherUser = await AuthedClientAsync();

        var response = await otherUser.GetAsync($"/api/JobApplications/{applicationId}/export/{route}?format=markdown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // User report (2026-08-22): a reviewer needs the real file output to judge it before deciding
    // to approve or reject - export is now allowed for ReviewReady, not just Approved (see
    // ExportServiceTests for the unit-level coverage of the state guard itself; this proves it end
    // to end through real routing/auth, matching this file's own convention for the sibling
    // ReviewReady/Approved export tests below).
    [Theory]
    [InlineData("resume")]
    [InlineData("cover-letter")]
    public async Task Export_returns_200_for_a_ReviewReady_application(string route)
    {
        var (client, applicationId) = await ReviewReadyApplicationAsync(_factory);

        var response = await client.GetAsync($"/api/JobApplications/{applicationId}/export/{route}?format=markdown");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("cover-letter")]
    public async Task Export_returns_401_for_a_missing_auth_token(string route)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/JobApplications/1/export/{route}?format=markdown");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("cover-letter")]
    public async Task Export_returns_400_for_an_invalid_format_value(string route)
    {
        var (client, applicationId) = await ApprovedApplicationAsync(_factory);

        var response = await client.GetAsync($"/api/JobApplications/{applicationId}/export/{route}?format=docx");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Copilot review finding (PR #48): browsers hide response headers from JS on a cross-origin
    // response unless the server explicitly lists them via Access-Control-Expose-Headers - without
    // it, every download in the supported cross-origin VITE_API_BASE_URL deployment mode would
    // silently lose Content-Disposition and fall back to the extensionless filename "download".
    // Masked locally by the Vite dev proxy (same-origin), so this can only be caught by actually
    // sending a cross-origin request (an Origin header) and reading the real CORS response header.
    [Fact]
    public async Task Export_response_exposes_ContentDisposition_via_CORS_for_cross_origin_downloads()
    {
        var (client, applicationId) = await ApprovedApplicationAsync(_factory);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/JobApplications/{applicationId}/export/resume?format=markdown");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposedHeaders).Should().BeTrue();
        string.Join(",", exposedHeaders!).Should().Contain("Content-Disposition");
    }

    // Fixed, non-empty bytes stand in for a real PDF - these tests exercise routing/headers/status
    // (T5.2), not PDF validity, which is covered separately by ExportServiceTests and the
    // trait-gated ProcessTypstCompilerTests.
    private sealed class FakeTypstCompiler : ITypstCompiler
    {
        public Task<Result<byte[]>> CompilePdfAsync(string typstSource, CancellationToken ct = default) =>
            Task.FromResult(Result<byte[]>.Ok(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }));
    }

    // Epic 3.2 Sprint 1 (S1.6): stands in for the real fetcher's entire HTTP/DNS/SSRF-validation
    // chain, already fully unit-tested by UrlValidationTests and JobPostingUrlFetcherTests. This
    // fake only proves the controller wires FetchAsync's Result into the right HTTP outcome.
    private sealed class FakeJobPostingUrlFetcher : IJobPostingUrlFetcher
    {
        private readonly Result<string> _result;
        public FakeJobPostingUrlFetcher(Result<string> result) => _result = result;
        public Task<Result<string>> FetchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    // Local shapes: Kind as a plain string so the test's default deserializer does not choke.
    private sealed record DraftDto(string Title, string? Description, string Kind, string? Section);
    private sealed record TaskSummaryDto(int Id, string Title, string Kind, string Status);
    private sealed record JobApplicationDto(int Id, string State, List<TaskSummaryDto> Tasks);
}
