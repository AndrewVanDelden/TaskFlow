using System.Text;
using FluentAssertions;
using TaskFlow.Api.Ingestion;
using UglyToad.PdfPig.Core;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

/// <summary>
/// Base-resume PDF upload fix (2026-08-22): the frontend previously called File.text() on an
/// uploaded PDF, which is a UTF-8 decode of raw bytes - correct for plain text, garbage for a
/// binary format like PDF. PdfTextExtractor wraps PdfPig to pull real, readable text out of a
/// PDF server-side instead. The fixture PDF below is built at test-run time (BuildMinimalPdf),
/// not hand-rolled with guessed byte offsets - every xref offset is computed from the actual
/// bytes already written, so nothing here is a typed-by-hand number.
/// </summary>
public class PdfTextExtractorTests
{
    private const string MarkerText = "PdfTextExtractorTestMarker123";

    [Fact]
    public void ExtractText_returns_the_visible_text_from_a_single_page_PDF()
    {
        byte[] pdfBytes = BuildMinimalPdf(MarkerText);

        string text = PdfTextExtractor.ExtractText(pdfBytes);

        text.Should().Contain(MarkerText);
    }

    [Fact]
    public void ExtractText_throws_for_bytes_that_are_not_a_valid_PDF()
    {
        byte[] notPdfBytes = Encoding.UTF8.GetBytes("not a pdf at all");

        Action act = () => PdfTextExtractor.ExtractText(notPdfBytes);

        act.Should().Throw<PdfDocumentFormatException>();
    }

    // Builds a minimal, single-page, valid PDF (catalog -> pages -> page -> Helvetica font -> one
    // content stream showing `text` via a Tj text-showing operator). Every xref byte offset is
    // read from stream.Position immediately before writing that object, so the offsets can never
    // drift out of sync with the bytes actually written above them.
    internal static byte[] BuildMinimalPdf(string text)
    {
        using var stream = new MemoryStream();

        void Write(string s) => stream.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var offsets = new long[6]; // objects 1..5; index 0 unused (free-list head)

        offsets[1] = stream.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = stream.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = stream.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> "
            + "/MediaBox [0 0 612 792] /Contents 5 0 R >>\nendobj\n");

        offsets[4] = stream.Position;
        Write("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        offsets[5] = stream.Position;
        string content = $"BT /F1 24 Tf 72 712 Td ({text}) Tj ET";
        Write($"5 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

        long xrefOffset = stream.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
            Write($"{offsets[i]:D10} 00000 n \n");

        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write(xrefOffset.ToString());
        Write("\n%%EOF");

        return stream.ToArray();
    }
}
