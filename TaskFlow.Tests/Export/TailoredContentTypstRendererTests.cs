using FluentAssertions;
using TaskFlow.Api.Export;
using Xunit;

namespace TaskFlow.Tests.Export;

/// <summary>
/// <see cref="TailoredContentTypstRenderer"/> is the security-critical piece of Sprint 5's export
/// feature: it converts Claude-generated <c>TaskItem.TailoredContent</c> (ultimately derived from
/// an untrusted, pasted job posting) into Typst markup. Typst's own markup language has a code mode
/// entered by a bare <c>#</c>, capable of calling functions, <c>#import</c>-ing packages, and
/// reading files. These tests therefore weight adversarial/escaping cases at least as heavily as
/// happy-path formatting -- an escaping bug here is a remote-code/file-read-shaped bug, not a
/// cosmetic one.
/// </summary>
public class TailoredContentTypstRendererTests
{
    private readonly TailoredContentTypstRenderer _renderer = new();

    // ----- Happy path: allow-listed constructs map to our own fixed Typst syntax -----

    [Fact]
    public void Level_one_heading_converts_to_single_equals_Typst_heading()
    {
        var result = _renderer.Render("# Jane Doe — Senior Engineer");

        result.Should().Contain("= Jane Doe");
        result.Should().NotContain("# Jane Doe");
    }

    [Fact]
    public void Level_two_heading_converts_to_double_equals_Typst_heading()
    {
        var result = _renderer.Render("## Experience");

        result.Should().Contain("== Experience");
    }

    [Fact]
    public void Bullet_list_converts_to_Typst_dash_list_items()
    {
        var result = _renderer.Render("- Led a team of five engineers\n- Shipped the v2 API");

        result.Should().Contain("- Led a team of five engineers");
        result.Should().Contain("- Shipped the v2 API");
    }

    [Fact]
    public void Bold_text_converts_to_Typst_strong_asterisks()
    {
        var result = _renderer.Render("Managed **cross-functional** launches.");

        result.Should().Contain("*cross-functional*");
        result.Should().NotContain("**cross-functional**");
    }

    // ----- Adversarial cases: content must never introduce live Typst syntax -----

    [Fact]
    public void Literal_hash_character_mid_sentence_is_backslash_escaped()
    {
        var result = _renderer.Render("Rated #1 in my class of 400 students.");

        result.Should().Contain(@"\#1");
        result.Should().NotContain("Rated #1");
    }

    [Fact]
    public void At_sign_is_backslash_escaped()
    {
        var result = _renderer.Render("Reporting directly to @manager on this initiative.");

        result.Should().Contain(@"\@manager");
        result.Should().NotContain("to @manager");
    }

    [Fact]
    public void Literal_backslash_in_content_is_escaped_without_double_escaping_neighbours()
    {
        // A backslash followed by a non-punctuation character (a letter) is preserved literally by
        // CommonMark's own escaping rules, so Markdig hands this class a genuine backslash character
        // to escape -- distinct from a markdown escape sequence like "\*" being consumed by Markdig
        // before this class ever sees it.
        var result = _renderer.Render(@"Backup path was C:\backup on the old server.");

        result.Should().Contain(@"C:\\backup");
    }

    [Fact]
    public void Prose_dash_at_start_of_a_text_run_is_escaped_not_read_as_a_Typst_list_marker()
    {
        // The leading backslash in the markdown source keeps Markdig's block parser from reading
        // this line as a real bulleted list (a bare "- " at a true line start would be); the
        // resulting parsed text still legitimately starts with a literal '-' character, which is
        // exactly the case this class must escape on the way out.
        var result = _renderer.Render(
            "\\- this isn't actually a list item, just prose starting with a dash");

        result.Should().Contain(@"\- this isn't actually a list item");
        result.Should().NotContain("\n- this isn't actually a list item");
    }

    [Fact]
    public void Prose_equals_at_start_of_a_text_run_is_escaped_not_read_as_a_Typst_heading()
    {
        var result = _renderer.Render(
            "\\= this isn't actually a heading, just prose starting with an equals sign");

        result.Should().Contain(@"\= this isn't actually a heading");
        result.Should().NotContain("\n= this isn't actually a heading");
    }

    // Copilot review finding (PR #48, round 2): '/' was absent from SignificantChars, so untrusted
    // content could still introduce live Typst markup - a leading "/ " creates a term-list item,
    // and "//" starts a line comment recognized even in markup mode (not just inside "#" code mode).
    // Both are a live-syntax leak this class's own allow-list boundary exists to prevent, even
    // though neither is itself code execution.
    [Fact]
    public void Slash_term_list_marker_at_start_of_a_text_run_is_escaped_not_read_as_a_Typst_term_list()
    {
        var result = _renderer.Render("/ Term: this could become a live definition-list item");

        result.Should().Contain(@"\/ Term: this could become a live definition-list item");
        result.Should().NotContain("\n/ Term:");
    }

    [Fact]
    public void Double_slash_comment_attempt_does_not_swallow_the_rest_of_the_line()
    {
        var result = _renderer.Render("Visible before. // this must not become a Typst comment. Visible after.");

        result.Should().Contain(@"\/\/");
        result.Should().Contain("Visible after.");
    }

    [Fact]
    public void Raw_HTML_script_and_img_tags_are_never_passed_through_as_Typst_or_HTML()
    {
        var result = _renderer.Render(
            "Before text. <script>alert(1)</script> <img src=\"x\" onerror=\"doEvil()\"> After text.");

        result.Should().NotContain("<script");
        result.Should().NotContain("</script>");
        result.Should().NotContain("<img");
        result.Should().NotContain("onerror");
        result.Should().NotContain("doEvil");
        result.Should().Contain("Before text.");
        result.Should().Contain("After text.");
    }

    [Fact]
    public void Content_attempting_a_Typst_import_call_renders_as_inert_escaped_text()
    {
        var result = _renderer.Render(
            "Skills: #import(\"/etc/passwd\") was listed on the fraudulent posting.");

        // Both the '#' that would enter code mode and every '/' in the path are escaped - the
        // slashes matter too (PR #48 review finding): unescaped, "//" anywhere reads as a Typst
        // line comment, not just a leading "#import(" that would enter code mode.
        result.Should().Contain("\\#import(\"\\/etc\\/passwd\")");
        result.Should().NotContain("\n#import(\"/etc/passwd\")");
        result.Should().NotContain("#import(\"/etc/passwd\")");
    }

    [Fact]
    public void Links_are_flattened_to_their_inert_text_never_a_live_Typst_link()
    {
        var result = _renderer.Render("See my [portfolio](https://evil.example/payload).");

        result.Should().Contain("portfolio");
        result.Should().NotContain("https://evil.example/payload");
    }

    [Fact]
    public void Fenced_code_blocks_are_dropped_not_passed_through_as_Typst_raw_syntax()
    {
        var result = _renderer.Render("Intro paragraph.\n\n```\n#import(\"/etc/passwd\")\n```\n\nOutro paragraph.");

        result.Should().Contain("Intro paragraph.");
        result.Should().Contain("Outro paragraph.");
        result.Should().NotContain("#import(\"/etc/passwd\")");
        result.Should().NotContain("```");
    }

    // ----- Structural round-trip: a realistic tailored-resume document -----

    [Fact]
    public void Realistic_tailored_resume_document_produces_well_formed_Typst_structure()
    {
        const string markdown = """
            # Jane Doe

            Results-driven engineer with 8 years of experience shipping **production** systems.

            ## Experience

            - Led migration of the core API to .NET 10
            - Reduced p95 latency by 40% across the platform
            - Mentored three junior engineers
            """;

        var result = _renderer.Render(markdown);

        result.Should().Contain("= Jane Doe");
        result.Should().Contain("== Experience");
        result.Should().Contain("*production*");
        result.Should().Contain("- Led migration of the core API to .NET 10");
        result.Should().Contain("- Reduced p95 latency by 40% across the platform");
        result.Should().Contain("- Mentored three junior engineers");

        var headingIndex = result.IndexOf("= Jane Doe", StringComparison.Ordinal);
        var subheadingIndex = result.IndexOf("== Experience", StringComparison.Ordinal);
        var firstBulletIndex = result.IndexOf("- Led migration", StringComparison.Ordinal);

        headingIndex.Should().BeLessThan(subheadingIndex);
        subheadingIndex.Should().BeLessThan(firstBulletIndex);
    }

    [Fact]
    public void Null_or_empty_markdown_renders_to_empty_string()
    {
        _renderer.Render(null!).Should().BeEmpty();
        _renderer.Render("").Should().BeEmpty();
    }
}
