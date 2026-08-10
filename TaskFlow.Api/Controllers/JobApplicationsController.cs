using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Ingestion;
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
    public async Task<IActionResult> SaveResumeContext([FromBody] SaveResumeContextDto dto) =>
        (await _resumeContext.SaveAsync(dto.IngestionSessionId, CurrentUserId(), dto.Content, dto.ContentFormat)).ToActionResult();

    // Assemble the approved posting into a JobApplication with two Todo sibling tasks.
    [HttpPost]
    public async Task<IActionResult> Assemble([FromBody] AssembleJobApplicationDto dto) =>
        (await _assembly.AssembleAsync(dto.IngestionSessionId, CurrentUserId(), dto.Posting)).ToActionResult();

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
