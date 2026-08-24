using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Extracts visible plain text from an HTML document, stripping boilerplate (script, style, nav,
/// header, footer) entirely before extraction and collapsing whitespace (Epic 3.2 Sprint 1, task
/// S1.5) so downstream consumers receive clean job-posting body text rather than markup.
/// </summary>
public static partial class HtmlTextExtractor
{
    // PR #63 review finding: RegexOptions.Compiled is the legacy pattern. A source-generated regex
    // gives compile-time codegen and zero first-use JIT cost, and is the current .NET 7+ idiom.
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    public static string ExtractText(string html)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        HtmlNodeCollection? boilerplateNodes = doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer");
        if (boilerplateNodes is not null)
        {
            // PR #63 review finding: SelectNodes' result is a snapshot list, but node.Remove()
            // mutates the underlying document tree it was computed from - .ToList() decouples the
            // removal loop from that collection defensively, at no real cost, rather than relying
            // on this HtmlAgilityPack version's specific (undocumented) tolerance for it.
            foreach (HtmlNode node in boilerplateNodes.ToList())
                node.Remove();
        }

        string rawText = doc.DocumentNode.InnerText;
        return WhitespaceRun().Replace(rawText, " ").Trim();
    }
}
