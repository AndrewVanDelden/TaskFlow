using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.Ingestion;
using UglyToad.PdfPig.Core;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    // Base-resume/job-posting/generic-document PDF uploads: client-side File.text() decodes raw PDF
    // bytes as UTF-8, which produces garbage since PDF is a binary format. This endpoint extracts
    // the real, readable text server-side via PdfTextExtractor instead.
    [HttpPost("extract-pdf-text")]
    public async Task<IActionResult> ExtractPdfText(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return Result<string>.Invalid("No file was provided.").ToActionResult();

        if (!IsPdf(file))
            return Result<string>.Invalid("Only PDF files are supported.").ToActionResult();

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        try
        {
            string text = PdfTextExtractor.ExtractText(stream.ToArray());
            return Result<string>.Ok(text).ToActionResult();
        }
        catch (PdfDocumentFormatException)
        {
            return Result<string>.Invalid("The uploaded file is not a valid PDF.").ToActionResult();
        }
    }

    private static bool IsPdf(IFormFile file) =>
        file.ContentType == "application/pdf" ||
        file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}
