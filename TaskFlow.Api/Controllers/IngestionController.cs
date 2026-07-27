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
    public IngestionController(IIngestionParser parser) => _parser = parser;

    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestDocumentDto dto) =>
        (await _parser.ParseAsync(dto.Content)).ToActionResult();
}
