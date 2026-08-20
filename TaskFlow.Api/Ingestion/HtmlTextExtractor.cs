using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Extracts visible plain text from an HTML document, stripping boilerplate (script, style, nav,
/// header, footer) entirely before extraction and collapsing whitespace (Epic 3.2 Sprint 1, task
/// S1.5) so downstream consumers receive clean job-posting body text rather than markup.
/// </summary>
public static class HtmlTextExtractor
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public static string ExtractText(string html)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        HtmlNodeCollection? boilerplateNodes = doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer");
        if (boilerplateNodes is not null)
        {
            foreach (HtmlNode node in boilerplateNodes)
                node.Remove();
        }

        string rawText = doc.DocumentNode.InnerText;
        return WhitespaceRun.Replace(rawText, " ").Trim();
    }
}
