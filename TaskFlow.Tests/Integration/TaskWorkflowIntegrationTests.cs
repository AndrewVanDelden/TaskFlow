using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;

namespace TaskFlow.Tests.Integration;

/// <summary>
/// HTTP-level tests for the newer endpoints (approve, reject, executor switch, ingestion) through the
/// real routing + auth + Result-to-status mapping. One shared factory/DB per class via IClassFixture.
/// </summary>
[Collection("Integration")]
public class TaskWorkflowIntegrationTests
{
    private readonly TestWebAppFactory _factory;
    public TaskWorkflowIntegrationTests(TestWebAppFactory factory) => _factory = factory;

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

    private static async Task<int> CreateTaskAsync(HttpClient client, string title = "Integration task")
    {
        var create = await client.PostAsJsonAsync("/api/Tasks", new { title });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<TaskResponseDto>())!.Id;
    }

    private static async Task MoveToReviewAsync(HttpClient client, int id)
    {
        var patch = await client.PatchAsJsonAsync($"/api/Tasks/{id}/status", new { status = "Review" });
        patch.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Approve_moves_a_Review_task_to_Done()
    {
        var client = await AuthedClientAsync();
        var id = await CreateTaskAsync(client);
        await MoveToReviewAsync(client, id);

        var approve = await client.PostAsync($"/api/Tasks/{id}/approve", null);

        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await approve.Content.ReadFromJsonAsync<TaskResponseDto>())!;
        updated.Status.Should().Be(nameof(WorkflowStatus.Done));
    }

    [Fact]
    public async Task Approve_a_task_that_is_not_in_Review_returns_400()
    {
        var client = await AuthedClientAsync();
        var id = await CreateTaskAsync(client, "Still todo");

        var approve = await client.PostAsync($"/api/Tasks/{id}/approve", null);

        approve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_sends_a_Review_task_back_to_Todo()
    {
        var client = await AuthedClientAsync();
        var id = await CreateTaskAsync(client);
        await MoveToReviewAsync(client, id);

        var reject = await client.PostAsJsonAsync($"/api/Tasks/{id}/reject", new { reason = "Needs work" });

        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await reject.Content.ReadFromJsonAsync<TaskResponseDto>())!;
        updated.Status.Should().Be(nameof(WorkflowStatus.Todo));
    }

    [Fact]
    public async Task Reject_without_a_reason_returns_400()
    {
        var client = await AuthedClientAsync();
        var id = await CreateTaskAsync(client);
        await MoveToReviewAsync(client, id);

        var reject = await client.PostAsJsonAsync($"/api/Tasks/{id}/reject", new { reason = "" });

        reject.StatusCode.Should().Be(HttpStatusCode.BadRequest); // [Required] rejects an empty reason
    }

    [Fact]
    public async Task Executor_switch_can_be_read_and_enabled()
    {
        var client = await AuthedClientAsync();

        var initial = await client.GetFromJsonAsync<ExecutorState>("/api/agents/executor");
        initial!.Enabled.Should().BeFalse(); // default OFF

        var enable = await client.PostAsync("/api/agents/executor/enable", null);
        enable.EnsureSuccessStatusCode();
        var enabled = (await enable.Content.ReadFromJsonAsync<ExecutorState>())!;
        enabled.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Ingest_then_commit_creates_board_tasks()
    {
        var client = await AuthedClientAsync();

        var ingest = await client.PostAsJsonAsync("/api/Ingestion",
            new { content = "# First task\n# Second task" });
        ingest.EnsureSuccessStatusCode();
        var drafts = await ingest.Content.ReadFromJsonAsync<List<DraftDto>>();
        drafts.Should().NotBeNullOrEmpty();

        var commit = await client.PostAsJsonAsync("/api/Ingestion/commit",
            new { sourceName = "spec.md", drafts });
        commit.EnsureSuccessStatusCode();
        var count = await commit.Content.ReadFromJsonAsync<int>();
        count.Should().Be(drafts!.Count);
    }

    // PR #45 review finding: GET /api/Tasks was never scoped by owner, so TaskResponseDto's new
    // TailoredContent field (Sprint 4R) leaked every user's tailored resume/cover letter to every
    // other authenticated user through the shared board endpoint. Proves the real HTTP fix, not
    // just the repository-level unit test: a second user's Epic 3 sibling task is completely
    // absent from the response (not just redacted), while a generic task remains visible to all.
    [Fact]
    public async Task GetAll_hides_another_users_Epic3_sibling_task_but_shows_generic_tasks_to_everyone()
    {
        var owner = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await owner.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        var assemble = await owner.PostAsJsonAsync("/api/JobApplications",
            new { ingestionSessionId = sessionId, posting = new { title = "Backend Engineer", section = "Job Posting" } });
        assemble.EnsureSuccessStatusCode();

        var otherUser = await AuthedClientAsync();
        var genericId = await CreateTaskAsync(otherUser, "Shared generic task");

        var tasksAsOtherUser = await otherUser.GetFromJsonAsync<List<TaskResponseDto>>("/api/Tasks");

        tasksAsOtherUser!.Should().Contain(t => t.Id == genericId);
        tasksAsOtherUser.Should().NotContain(t => t.ApplicationId != null);
    }

    // T5.0: a fresh architecture review (Sprint 5) re-checked every endpoint touching the same rows
    // as the PR #45 GetAll fix and found the same gap still open on all six single-item actions -
    // none of them checked ownership at all, so any authenticated user could read or mutate another
    // user's Epic 3 sibling task (including its personal TailoredContent) just by guessing/knowing
    // its id. Proves the real HTTP fix: a non-owner gets the same 404 NotFound a caller with the
    // wrong id would get, never a distinguishable error that reveals the task exists.
    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("PATCH", "/status")]
    [InlineData("POST", "/approve")]
    [InlineData("POST", "/reject")]
    [InlineData("DELETE", "")]
    public async Task NonOwner_gets_404_on_every_single_item_route_for_another_users_Epic3_sibling_task(string method, string suffix)
    {
        var owner = await AuthedClientAsync();
        var sessionId = Guid.NewGuid().ToString("N");
        await owner.PostAsJsonAsync("/api/JobApplications/resume-context",
            new { ingestionSessionId = sessionId, content = "Base resume text." });
        var assemble = await owner.PostAsJsonAsync("/api/JobApplications",
            new { ingestionSessionId = sessionId, posting = new { title = "Backend Engineer", section = "Job Posting" } });
        assemble.EnsureSuccessStatusCode();
        var application = await assemble.Content.ReadFromJsonAsync<JobApplicationDto>();
        var taskId = application!.Tasks[0].Id;

        var otherUser = await AuthedClientAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/Tasks/{taskId}{suffix}");
        request.Content = suffix switch
        {
            "/status" => JsonContent.Create(new { status = "Done" }),
            "/reject" => JsonContent.Create(new { reason = "trying to peek" }),
            "" when method == "PUT" => JsonContent.Create(new { title = "Hacked title", status = "Todo", priority = "Medium" }),
            _ => null
        };

        var response = await otherUser.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The other half of the same fix: generic tasks (no ApplicationId) are the shared board by
    // design and must stay reachable by any authenticated user, not just their creator - the
    // ownership check above must never fire for them.
    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("PATCH", "/status")]
    [InlineData("DELETE", "")]
    public async Task Any_authenticated_user_can_still_reach_a_generic_task_on_every_single_item_route(string method, string suffix)
    {
        var creator = await AuthedClientAsync();
        var taskId = await CreateTaskAsync(creator, "Shared generic task");

        var otherUser = await AuthedClientAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/Tasks/{taskId}{suffix}");
        request.Content = suffix switch
        {
            "/status" => JsonContent.Create(new { status = "Review" }),
            "" when method == "PUT" => JsonContent.Create(new { title = "Edited title", status = "Todo", priority = "Medium" }),
            _ => null
        };

        var response = await otherUser.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Approve/Reject require Review status first, so they're proven separately (not folded into the
    // theory above, which asserts a flat 200): a non-owner must reach the *status* guard (400), not
    // the ownership guard (404), for a generic task.
    [Fact]
    public async Task Any_authenticated_user_can_still_approve_and_reject_a_generic_task()
    {
        var creator = await AuthedClientAsync();
        var otherUser = await AuthedClientAsync();

        var approveTaskId = await CreateTaskAsync(creator, "Generic task for approve");
        await MoveToReviewAsync(creator, approveTaskId);
        var approve = await otherUser.PostAsync($"/api/Tasks/{approveTaskId}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectTaskId = await CreateTaskAsync(creator, "Generic task for reject");
        await MoveToReviewAsync(creator, rejectTaskId);
        var reject = await otherUser.PostAsJsonAsync($"/api/Tasks/{rejectTaskId}/reject", new { reason = "Needs work" });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Local shapes: read the switch state, and read drafts/tasks with their enum fields as plain
    // strings so the test's default (non-enum-aware) deserializer does not choke - the server side
    // serializes enums as strings via JsonStringEnumConverter (Program.cs), but HttpContent's
    // ReadFromJsonAsync uses default JsonSerializerOptions with no such converter registered.
    private sealed record ExecutorState(bool Enabled);
    private sealed record DraftDto(string Title, string? Description, string Kind, string? Section);
    private sealed record JobApplicationDto(int Id, string State, List<TaskSummaryDto> Tasks);
    private sealed record TaskSummaryDto(int Id, string Title, string Kind, string Status);
}
