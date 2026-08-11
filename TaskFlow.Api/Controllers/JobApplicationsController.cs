using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
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
    private readonly IResumeContextService _resumeContext;
    private readonly IJobApplicationAssemblyService _assembly;

    public JobApplicationsController(
        IJobPostingIngestionParser parser,
        IResumeContextService resumeContext,
        IJobApplicationAssemblyService assembly)
    {
        _parser = parser;
        _resumeContext = resumeContext;
        _assembly = assembly;
    }

    // Parse a pasted job posting into title/company/requirements (no persistence).
    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] IngestDocumentDto dto) =>
        (await _parser.ParseAsync(dto.Content)).ToActionResult();

    // Save the caller's base resume server-side, scoped to one ingestion session.
    [HttpPost("resume-context")]
    public async Task<IActionResult> SaveResumeContext([FromBody] SaveResumeContextDto dto)
    {
        if (!TryGetCurrentUserId(out var ownerId))
            return UnauthenticatedIdentity();

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
        if (!TryGetCurrentUserId(out var ownerId))
            return UnauthenticatedIdentity();

        var posting = new TaskDraft(dto.Posting.Title, dto.Posting.Description, TaskKind.ResumeTailoring, dto.Posting.Section);
        return (await _assembly.AssembleAsync(dto.IngestionSessionId, ownerId, posting)).ToActionResult();
    }

    // A missing or non-numeric NameIdentifier claim (misconfigured auth, a token from a different
    // issuer) must not throw - [Authorize] only proves a valid token was presented, not that its
    // claims are shaped the way this controller expects.
    private bool TryGetCurrentUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private UnauthorizedObjectResult UnauthenticatedIdentity() =>
        Unauthorized(new { message = "The request's identity claim is missing or invalid." });
}
