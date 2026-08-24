using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Export;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobApplicationsController : ControllerBase
{
    private readonly IJobPostingIngestionParser _parser;
    private readonly IJobPostingUrlFetcher _urlFetcher;
    private readonly IResumeContextService _resumeContext;
    private readonly IJobApplicationAssemblyService _assembly;
    private readonly IJobApplicationService _jobApplicationService;
    private readonly IExportService _exportService;

    public JobApplicationsController(
        IJobPostingIngestionParser parser,
        IJobPostingUrlFetcher urlFetcher,
        IResumeContextService resumeContext,
        IJobApplicationAssemblyService assembly,
        IJobApplicationService jobApplicationService,
        IExportService exportService)
    {
        _parser = parser;
        _urlFetcher = urlFetcher;
        _resumeContext = resumeContext;
        _assembly = assembly;
        _jobApplicationService = jobApplicationService;
        _exportService = exportService;
    }

    // Parse a pasted job posting into title/company/requirements (no persistence).
    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] IngestDocumentDto dto) =>
        (await _parser.ParseAsync(dto.Content)).ToActionResult();

    // Fetch a job posting from a URL (SSRF-safe, see Epic 3.2), then parse it exactly like a pasted
    // posting -- the tiered parser is reused completely unchanged.
    [HttpPost("parse-url")]
    public async Task<IActionResult> ParseUrl([FromBody] ParseUrlDto dto)
    {
        if (!Uri.TryCreate(dto.Url, UriKind.Absolute, out Uri? uri))
            return Result<string>.Invalid("The provided URL is not a valid absolute URI.").ToActionResult();

        Result<string> fetchResult = await _urlFetcher.FetchAsync(uri);
        if (!fetchResult.IsSuccess)
            return fetchResult.ToActionResult();

        return (await _parser.ParseAsync(fetchResult.Value!)).ToActionResult();
    }

    // Save the caller's base resume server-side, scoped to one ingestion session.
    [HttpPost("resume-context")]
    public async Task<IActionResult> SaveResumeContext([FromBody] SaveResumeContextDto dto)
    {
        if (!this.TryGetCurrentUserId(out var ownerId))
            return this.UnauthenticatedIdentity();

        return (await _resumeContext.SaveAsync(dto.IngestionSessionId, ownerId, dto.Content, dto.ContentFormat)).ToActionResult();
    }

    // Assemble the approved posting into a JobApplication with two Todo sibling tasks.
    // TaskDraft's constructor requires a Kind, but AssembleAsync never reads posting.Kind - it
    // always creates exactly one ResumeTailoring and one CoverLetterTailoring sibling regardless
    // of what's passed here, so this value is a placeholder only, not something that steers
    // behavior downstream.
    [HttpPost]
    public async Task<IActionResult> Assemble([FromBody] AssembleJobApplicationDto dto)
    {
        if (!this.TryGetCurrentUserId(out var ownerId))
            return this.UnauthenticatedIdentity();

        var posting = new TaskDraft(dto.Posting.Title, dto.Posting.Description, TaskKind.ResumeTailoring, dto.Posting.Section, dto.Posting.Company);
        return (await _assembly.AssembleAsync(dto.IngestionSessionId, ownerId, posting)).ToActionResult();
    }

    // Read the caller's base resume back for a JobApplication (Sprint 4R: the paired review needs
    // to render base resume, tailored resume, and cover letter together).
    [HttpGet("{id:int}/resume-context")]
    public async Task<IActionResult> GetResumeContext(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _resumeContext.GetForApplicationAsync(id, callerId)).ToActionResult();
    }

    // The caller's own most recently saved base resume, from any session (Sprint 6: lets the
    // intake UI offer reuse instead of forcing a re-paste every time).
    [HttpGet("resume-context/latest")]
    public async Task<IActionResult> GetMostRecentResumeContext()
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _resumeContext.GetMostRecentForCallerAsync(callerId)).ToActionResult();
    }

    // Human sign-off on the pair: moves both sibling tasks to Done and the application to Approved.
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _jobApplicationService.ApproveAsync(id, callerId)).ToActionResult();
    }

    // Human rejection of the pair: returns both sibling tasks to Todo and the application to Building.
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectTaskDto dto)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _jobApplicationService.RejectAsync(id, callerId, dto.Reason)).ToActionResult();
    }

    // Downloadable PDF/Markdown of the approved tailored resume (Sprint 5, T5.2).
    [HttpGet("{id:int}/export/resume")]
    public async Task<IActionResult> ExportResume(int id, [FromQuery] string format)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        if (!TryParseFormat(format, out var parsedFormat))
            return BadRequest(new { message = $"Invalid format '{format}'. Valid values: pdf, markdown." });

        return (await _exportService.ExportResumeAsync(id, callerId, this.GetCurrentUserName(), parsedFormat, HttpContext.RequestAborted)).ToFileActionResult();
    }

    // Downloadable PDF/Markdown of the approved tailored cover letter (Sprint 5, T5.2).
    [HttpGet("{id:int}/export/cover-letter")]
    public async Task<IActionResult> ExportCoverLetter(int id, [FromQuery] string format)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        if (!TryParseFormat(format, out var parsedFormat))
            return BadRequest(new { message = $"Invalid format '{format}'. Valid values: pdf, markdown." });

        return (await _exportService.ExportCoverLetterAsync(id, callerId, this.GetCurrentUserName(), parsedFormat, HttpContext.RequestAborted)).ToFileActionResult();
    }

    // Shared by both export actions (DRY - was duplicated verbatim). Enum.TryParse alone also
    // accepts the numeric backing value as a string ("0"/"1"), which would silently succeed as an
    // undocumented alias for Pdf/Markdown; the documented contract is pdf/markdown only, so the
    // parsed value's own name must case-insensitively match what was typed (PR #48 Copilot review
    // finding, confirmed real).
    private static bool TryParseFormat(string format, out ExportFormat result) =>
        Enum.TryParse(format, ignoreCase: true, out result) &&
        result.ToString().Equals(format, StringComparison.OrdinalIgnoreCase);
}
