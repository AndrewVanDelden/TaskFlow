using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Controllers;
using Xunit;
using static TaskFlow.Tests.Ingestion.PdfTextExtractorTests;

namespace TaskFlow.Tests.Controllers;

/// <summary>
/// Base-resume PDF upload fix (2026-08-22): FilesController is a thin, stateless endpoint with no
/// injected dependencies (a static call-through to PdfTextExtractor.ExtractText), so these are
/// real IFormFile doubles wrapping real (or intentionally invalid) PDF bytes calling the real
/// static extractor - there is no seam to mock and none is needed for this locked design.
/// </summary>
public class FilesControllerTests
{
    private const string MarkerText = "PdfTextExtractorTestMarker123";

    [Fact]
    public async Task ExtractPdfText_returns_200_with_the_extracted_text_for_a_valid_PDF()
    {
        IFormFile file = BuildFormFile(BuildMinimalPdf(MarkerText), "resume.pdf", "application/pdf");
        var controller = new FilesController();

        IActionResult result = await controller.ExtractPdfText(file);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<string>()
            .Which.Should().Contain(MarkerText);
    }

    [Fact]
    public async Task ExtractPdfText_returns_400_when_no_file_is_provided()
    {
        var controller = new FilesController();

        IActionResult result = await controller.ExtractPdfText(null!);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ExtractPdfText_returns_400_for_a_non_PDF_file()
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes("just some plain resume text, not a PDF");
        IFormFile file = BuildFormFile(plainTextBytes, "resume.txt", "text/plain");
        var controller = new FilesController();

        IActionResult result = await controller.ExtractPdfText(file);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ExtractPdfText_returns_400_not_500_for_a_corrupt_PDF()
    {
        byte[] corruptBytes = Encoding.UTF8.GetBytes("this claims to be a PDF but has no real PDF structure inside");
        IFormFile file = BuildFormFile(corruptBytes, "resume.pdf", "application/pdf");
        var controller = new FilesController();

        IActionResult result = await controller.ExtractPdfText(file);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static FormFile BuildFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
