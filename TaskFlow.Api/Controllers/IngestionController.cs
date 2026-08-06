using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Ingestion;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IngestionController : ControllerBase
{
    private readonly IIngestionParser _parser;
    private readonly IDraftCommitService _commit;

    public IngestionController(IIngestionParser parser, IDraftCommitService commit)
    {
        _parser = parser;
        _commit = commit;
    }

    // Parse content into drafts for preview (no persistence).
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestDocumentDto dto) =>
        (await _parser.ParseAsync(dto.Content)).ToActionResult();

    // Persist the approved drafts as To Do board tasks.
    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] CommitDraftsDto dto) =>
        (await _commit.CommitAsync(dto.SourceName, dto.Drafts)).ToActionResult();
}
