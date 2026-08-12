using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TaskFlow.Api.Export;

/// <summary>
/// Converts Claude-generated <c>TaskItem.TailoredContent</c> Markdown -- ultimately derived from a
/// pasted, untrusted job posting -- into Typst markup that is safe to concatenate into a Typst
/// source document sent to the <c>typst</c> compiler.
///
/// This is a security boundary, not a formatting convenience. Typst's own markup language has a
/// code mode (entered by a bare <c>#</c>) capable of calling functions, <c>#import</c>-ing
/// packages, and reading files relative to the compiler's <c>--root</c>. If tailored content ever
/// reached the compiler with its Typst-significant characters unescaped, it could execute as live
/// Typst code instead of rendering as inert text -- the same class of failure
/// <c>dangerouslySetInnerHTML</c> would be for agent markdown on the frontend (which this codebase
/// avoids via <c>MarkdownPreview.tsx</c> + <c>rehype-sanitize</c>). This class applies that same
/// allow-list-not-deny-list philosophy to Typst instead of HTML: only an explicit allow-list of
/// Markdown constructs (headings, paragraphs, lists, emphasis/strong, line breaks, plain text) is
/// ever translated into *our own*, fixed, hardcoded Typst syntax. Everything else (raw HTML, links,
/// images, code blocks, and anything else Markdig might parse) is dropped or flattened to inert,
/// escaped plain text -- never passed through as live markup. Every leaf text run is escaped
/// *before* any of our formatting syntax is wrapped around it, so content can never introduce new,
/// unescaped Typst syntax of its own; only this class's own code ever emits live Typst syntax.
///
/// This is a different defense from <see cref="Security.PromptSafety"/>, not a reuse of it:
/// <see cref="Security.PromptSafety.WrapUntrusted"/> defends a prompt-injection boundary (untrusted
/// text steering Claude's behavior via a chat prompt). This class defends Typst's own
/// markup-injection boundary, a structurally different risk.
/// </summary>
public class TailoredContentTypstRenderer
{
    /// <summary>
    /// Typst-syntactically-significant characters that must never reach the compiler unescaped when
    /// they originate from content text, as opposed to markup this class itself emits. Confirmed
    /// against Typst's syntax reference: '#' enters code mode; '*' / '_' are strong/emphasis
    /// delimiters; '`' opens raw/code spans; '$' opens math mode; '@' begins a label reference;
    /// '&lt;' / '&gt;' are used in label syntax; '[' / ']' delimit content blocks (function
    /// arguments) -- an unescaped stray bracket in content could unbalance the surrounding Typst
    /// source's block structure even though it is not, by itself, code execution. This list is a
    /// verified starting point, not asserted exhaustive; expand it if another significant character
    /// is found. Backslash itself is handled separately below, not in this array, because it must be
    /// escaped independently and first.
    /// </summary>
    private static readonly char[] SignificantChars =
    {
        '#', '*', '_', '`', '$', '<', '>', '@', '[', ']',
    };

    /// <summary>
    /// Converts <paramref name="markdown"/> into Typst markup. Never throws on malformed or
    /// adversarial input -- worst case, disallowed constructs are dropped or flattened to escaped
    /// text.
    /// </summary>
    public string Render(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var document = Markdig.Markdown.Parse(markdown);

        var sb = new StringBuilder();
        RenderBlocks(document, sb);
        return sb.ToString().Trim();
    }

    private void RenderBlocks(ContainerBlock container, StringBuilder sb)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    RenderHeading(heading, sb);
                    break;
                case ParagraphBlock paragraph:
                    RenderParagraph(paragraph, sb);
                    break;
                case ListBlock list:
                    RenderList(list, sb, depth: 0);
                    break;
                default:
                    RenderFlattenedBlock(block, sb);
                    break;
            }
        }
    }

    private static void RenderHeading(HeadingBlock heading, StringBuilder sb)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        sb.Append('=', level).Append(' ');
        if (heading.Inline is not null)
            RenderInlines(heading.Inline, sb);
        sb.Append("\n\n");
    }

    private static void RenderParagraph(ParagraphBlock paragraph, StringBuilder sb)
    {
        if (paragraph.Inline is not null)
            RenderInlines(paragraph.Inline, sb);
        sb.Append("\n\n");
    }

    private static void RenderList(ListBlock list, StringBuilder sb, int depth)
    {
        var marker = list.IsOrdered ? "+" : "-";
        var indent = new string(' ', depth * 2);

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
                continue;

            var isFirstLineOfItem = true;
            foreach (var child in listItem)
            {
                switch (child)
                {
                    case ParagraphBlock para:
                        sb.Append(indent).Append(isFirstLineOfItem ? marker + " " : "  ");
                        if (para.Inline is not null)
                            RenderInlines(para.Inline, sb);
                        sb.Append('\n');
                        isFirstLineOfItem = false;
                        break;
                    case ListBlock nested:
                        RenderList(nested, sb, depth + 1);
                        break;
                    default:
                        RenderFlattenedBlock(child, sb);
                        break;
                }
            }
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Any block outside the allow-list (raw HTML blocks, code blocks, block quotes, tables,
    /// thematic breaks, and anything else Markdig might parse) is handled here. A raw HTML block
    /// carries no safe textual content of its own and is dropped entirely. Anything else is
    /// flattened: its leaf text (if any) is extracted and escaped as inert plain text, never passed
    /// through as its own markup.
    /// </summary>
    private static void RenderFlattenedBlock(Block block, StringBuilder sb)
    {
        if (block is HtmlBlock)
            return;

        var text = ExtractPlainText(block);
        if (!string.IsNullOrWhiteSpace(text))
            sb.Append(EscapeText(text)).Append("\n\n");
    }

    private static string ExtractPlainText(Block block)
    {
        switch (block)
        {
            case LeafBlock { Inline: not null } leaf:
                return ExtractPlainText(leaf.Inline);
            case ContainerBlock container:
                var parts = new List<string>();
                foreach (var child in container)
                {
                    var childText = ExtractPlainText(child);
                    if (!string.IsNullOrEmpty(childText))
                        parts.Add(childText);
                }
                return string.Join(" ", parts);
            default:
                // Leaf blocks whose content lives outside the inline AST (e.g. a fenced code
                // block's raw Lines) are dropped rather than flattened -- there is no inline
                // structure here for this method's escaping path to walk, and dropping is safe.
                return string.Empty;
        }
    }

    private static string ExtractPlainText(ContainerInline container)
    {
        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case ContainerInline nested:
                    sb.Append(ExtractPlainText(nested));
                    break;
                // HtmlInline, HtmlEntityInline, AutolinkInline, and anything else not handled above
                // are intentionally dropped rather than flattened: their raw markup is the only
                // "content" they carry, and passing that through as text would leak it verbatim.
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Walks an allow-listed inline tree, escaping every leaf text run before wrapping it in our
    /// own fixed Typst formatting syntax. This is the method where the allow-list is enforced at the
    /// inline level: only <see cref="LiteralInline"/>, <see cref="EmphasisInline"/>,
    /// <see cref="LineBreakInline"/>, and (flattened) <see cref="LinkInline"/>/<see cref="CodeInline"/>
    /// are handled. Everything else contributes nothing.
    /// </summary>
    private static void RenderInlines(ContainerInline container, StringBuilder sb)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(EscapeText(literal.Content.ToString()));
                    break;

                case LineBreakInline lineBreak:
                    // A hard break becomes our own controlled Typst line-break call; this is live
                    // syntax our own code emits, not user content, so it is deliberately not
                    // escaped. A soft break (ordinary reflowed newline) becomes a plain space.
                    sb.Append(lineBreak.IsHard ? " #linebreak() " : " ");
                    break;

                case EmphasisInline emphasis:
                    RenderEmphasis(emphasis, sb);
                    break;

                case LinkInline link:
                    // Links and images are outside the allow-list: flatten to their inert, escaped
                    // display/alt text. The URL itself is never emitted -- no live Typst link, no
                    // chance of it being read as anything but discarded metadata.
                    RenderInlines(link, sb);
                    break;

                case CodeInline code:
                    sb.Append(EscapeText(code.Content));
                    break;

                case HtmlInline:
                case HtmlEntityInline:
                case AutolinkInline:
                    // Raw HTML tags, HTML entities, and bare autolinks are dropped entirely: their
                    // raw markup/URL is the only content they carry, and none of it is safe to pass
                    // through as either live syntax or plain text.
                    break;

                default:
                    if (inline is ContainerInline nested)
                        RenderInlines(nested, sb);
                    break;
            }
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, StringBuilder sb)
    {
        // Markdig's EmphasisInline.DelimiterCount distinguishes *emphasis* (1, "_..._" in Typst)
        // from **strong** (2 or more, "*...*" in Typst).
        var marker = emphasis.DelimiterCount >= 2 ? "*" : "_";
        sb.Append(marker);
        RenderInlines(emphasis, sb);
        sb.Append(marker);
    }

    /// <summary>
    /// Backslash-escapes every Typst-significant character in a single pass over the original text.
    /// Backslash is escaped first and in the same pass as everything else -- never as a separate
    /// pass -- so an escaping backslash this method inserts can never itself be re-escaped or
    /// misread as escaping the following character (a two-pass approach would double-escape).
    /// A leading '-', '=', or '+' (how our own list/heading/ordered-list markers begin a Typst
    /// source line) is escaped whenever it is the first character of this text run, regardless of
    /// whether this particular occurrence will end up at an actual Typst source line start once
    /// composed: '\-' still renders as a plain '-' character, so this conservative choice has no
    /// visible cost and removes any dependency on tracking exact source-line boundaries through
    /// nested emphasis/line-break structure.
    /// </summary>
    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            var needsEscape = c == '\\' || Array.IndexOf(SignificantChars, c) >= 0;
            var isLeadingLineMarker = i == 0 && (c == '-' || c == '=' || c == '+');

            if (needsEscape || isLeadingLineMarker)
                sb.Append('\\');

            sb.Append(c);
        }
        return sb.ToString();
    }
}
