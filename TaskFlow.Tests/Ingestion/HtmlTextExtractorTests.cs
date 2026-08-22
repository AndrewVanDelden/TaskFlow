using FluentAssertions;
using TaskFlow.Api.Ingestion;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

/// <summary>
/// Epic 3.2 Sprint 1, task S1.5 (HTML-to-text extraction via HtmlAgilityPack).
/// One shared fixture models a realistic job-posting page: nav/header/footer/script/style
/// boilerplate that must be stripped entirely, alongside the H1 title, company heading, and
/// paragraph content that must survive extraction. No word overlap between the two groups so
/// each assertion is unambiguous.
/// </summary>
public class HtmlTextExtractorTests
{
    private const string JobPostingHtml = """
        <html>
        <head><style>.hidden { display: none; }</style></head>
        <body>
          <nav><a href="/">Home</a><a href="/jobs">Jobs</a></nav>
          <script>console.log('tracking pixel fired');</script>
          <header><div>SiteBrand Corp</div></header>
          <main>
            <h1>Senior Backend Engineer</h1>
            <h2>Acme Corp</h2>
            <p>We are looking for an experienced backend engineer to join our team.</p>
          </main>
          <footer><p>Copyright 2026</p></footer>
        </body>
        </html>
        """;

    [Fact]
    public void Extracted_text_contains_the_page_title()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().Contain("Senior Backend Engineer");
    }

    [Fact]
    public void Extracted_text_contains_the_company_heading()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().Contain("Acme Corp");
    }

    [Fact]
    public void Extracted_text_contains_the_body_paragraph()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().Contain("We are looking for an experienced backend engineer to join our team.");
    }

    [Fact]
    public void Extracted_text_excludes_script_content()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().NotContain("tracking pixel fired");
    }

    [Fact]
    public void Extracted_text_excludes_style_content()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().NotContain("display: none");
    }

    [Fact]
    public void Extracted_text_excludes_nav_link_text()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().NotContain("Home");
        text.Should().NotContain("Jobs");
    }

    [Fact]
    public void Extracted_text_excludes_header_and_footer_content()
    {
        string text = HtmlTextExtractor.ExtractText(JobPostingHtml);

        text.Should().NotContain("SiteBrand Corp");
        text.Should().NotContain("Copyright 2026");
    }
}
