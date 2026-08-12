namespace TaskFlow.Api.Export;

/// <summary>Output format for an artifact export (T5.1d/T5.2). Markdown is a trivial pass-through
/// of TailoredContent; Pdf goes through TailoredContentTypstRenderer + ITypstCompiler.</summary>
public enum ExportFormat
{
    Pdf,
    Markdown
}
