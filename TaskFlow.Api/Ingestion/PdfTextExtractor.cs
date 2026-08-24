using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Wraps PdfPig to pull the visible text out of a PDF. A PDF is a binary format - decoding its raw
/// bytes as UTF-8 (what the frontend used to do via File.text()) produces garbage, not real text.
/// </summary>
public static class PdfTextExtractor
{
    public static string ExtractText(byte[] pdfBytes)
    {
        using PdfDocument document = PdfDocument.Open(pdfBytes);

        var text = new StringBuilder();
        foreach (Page page in document.GetPages())
            text.AppendLine(page.Text);

        return text.ToString();
    }
}
