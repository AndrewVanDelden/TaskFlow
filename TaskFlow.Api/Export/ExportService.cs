using System.Text;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Export;

/// <summary>
/// EF/repository-backed implementation of <see cref="IExportService"/>. Ties T5.1a-c together:
/// ITypstCompiler (the compiler seam), TailoredContentTypstRenderer (Markdown -> escaped Typst
/// markup), ITemplateProvider (template read + cache), and the resume/cover-letter .typ templates.
/// Ownership and state guards mirror JobApplicationService.ApproveAsync's exact convention (Sprint
/// 5 "Decisions owned here" in TaskFlow_Epic3_ResumeBuilder.md): missing and wrong-owner both
/// collapse into NotFound; an application that is neither ReviewReady nor Approved is Invalid (PR
/// #65: ReviewReady was added so a reviewer can preview the real output before approving).
/// </summary>
public class ExportService : IExportService
{
    private readonly IJobApplicationRepository _jobApplications;
    private readonly ITaskRepository _tasks;
    private readonly ITypstCompiler _compiler;
    private readonly TailoredContentTypstRenderer _renderer;
    private readonly ITemplateProvider _templates;

    public ExportService(
        IJobApplicationRepository jobApplications,
        ITaskRepository tasks,
        ITypstCompiler compiler,
        TailoredContentTypstRenderer renderer,
        ITemplateProvider templates)
    {
        _jobApplications = jobApplications;
        _tasks = tasks;
        _compiler = compiler;
        _renderer = renderer;
        _templates = templates;
    }

    public Task<Result<ExportedFile>> ExportResumeAsync(int applicationId, int callerId, string callerName, ExportFormat format, CancellationToken ct = default) =>
        ExportAsync(applicationId, callerId, callerName, format, TaskKind.ResumeTailoring, "resume.typ", "Resume", ct);

    public Task<Result<ExportedFile>> ExportCoverLetterAsync(int applicationId, int callerId, string callerName, ExportFormat format, CancellationToken ct = default) =>
        ExportAsync(applicationId, callerId, callerName, format, TaskKind.CoverLetterTailoring, "cover-letter.typ", "Cover_Letter", ct);

    // Shared by both public methods so the ownership/state guard and sibling-fetch logic lives in
    // exactly one place (DRY) - the only differences between a resume and a cover-letter export
    // are which sibling TaskKind to pick, which template to compose with, and the document label
    // that goes into the file name.
    private async Task<Result<ExportedFile>> ExportAsync(
        int applicationId, int callerId, string callerName, ExportFormat format, TaskKind kind, string templateFileName, string documentLabel, CancellationToken ct)
    {
        var application = await _jobApplications.GetByIdAsync(applicationId, ct);

        // Same NotFound for missing and wrong-owner as JobApplicationService.ApproveAsync: a
        // cross-owner probe must be indistinguishable from a genuine 404.
        if (application is null || application.OwnerId != callerId)
            return Result<ExportedFile>.NotFound($"JobApplication {applicationId} not found.");

        // The caller is a confirmed owner at this point, so a specific "wrong state" message is
        // fine - nothing to hide from a genuine owner, matching ApproveAsync's own convention.
        // User report (2026-08-22): a reviewer needs the real PDF/Markdown output to judge it
        // before deciding to approve or reject - ReviewReady is allowed here too, not just
        // Approved, since the render/compile pipeline below only ever reads TailoredContent, which
        // already exists once a task reaches Review. Building is still refused: neither sibling has
        // necessarily finished yet.
        if (application.State != ApplicationState.Approved && application.State != ApplicationState.ReviewReady)
            return Result<ExportedFile>.Invalid(
                $"JobApplication {applicationId} is {application.State}; only ReviewReady or Approved applications can be exported.");

        var siblings = await _tasks.GetByApplicationIdAsync(applicationId, ct);
        var task = siblings.FirstOrDefault(t => t.Kind == kind);

        // Structurally should never happen: an Approved application only ever comes from
        // TryApprovePairAsync, which requires both required sibling kinds to already exist and be
        // Review before it will flip the state (Sprint 1/3R's invariant). Defensive, not assumed.
        if (task is null)
            return Result<ExportedFile>.NotFound($"JobApplication {applicationId} has no {kind} task.");

        // User report (2026-08-24): a downloaded file named after a GUID/generic literal gives no
        // clue which application it belongs to once it's sitting in a Downloads folder next to
        // several others. "Andrew_Van_Delden_Resume_Acme_Corp.pdf" is self-identifying; the company
        // segment is only appended when the application actually has one (free-text parsing or a
        // manually-entered posting can leave it blank).
        var baseFileName = BuildFileNameBase(callerName, documentLabel, application.Company);

        return format switch
        {
            ExportFormat.Markdown => Result<ExportedFile>.Ok(BuildMarkdownFile(task, baseFileName)),
            ExportFormat.Pdf => await BuildPdfFileAsync(task, templateFileName, baseFileName, ct),
            _ => Result<ExportedFile>.Invalid($"Unsupported export format '{format}'.")
        };
    }

    // PR #72 review finding: the blank-company check must run on the *sanitized* value - a company
    // built entirely from characters SanitizeForFileName strips (e.g. "???", plausible from
    // free-text job-posting parsing) is non-blank going in but sanitizes down to "", and must fall
    // back the same way a genuinely blank company already does (not a dangling trailing underscore).
    private static string BuildFileNameBase(string callerName, string documentLabel, string? company)
    {
        var namePart = SanitizeForFileName(callerName);
        var sanitizedCompany = string.IsNullOrWhiteSpace(company) ? null : SanitizeForFileName(company);
        var companyPart = string.IsNullOrEmpty(sanitizedCompany) ? null : sanitizedCompany;
        return companyPart is null ? $"{namePart}_{documentLabel}" : $"{namePart}_{documentLabel}_{companyPart}";
    }

    // Filesystem-invalid characters this file name must never carry. Explicit and fixed, not solely
    // Path.GetInvalidFileNameChars() (PR #72 review finding): that BCL call reflects the *API
    // server's* host OS, not the client's - on Linux it returns only NUL and '/', so a company/name
    // containing e.g. ':' or '?' would sail through unsanitized if this API is ever hosted on Linux,
    // producing a file name that's illegal on the Windows machine actually saving it. Unioned with
    // Path.GetInvalidFileNameChars() rather than replacing it, so whatever the current host OS also
    // can't handle keeps being caught - this only ever removes more characters, never fewer.
    private static readonly char[] WindowsReservedFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    // Strips characters the filesystem/Content-Disposition header can't carry (a caller name or
    // company scraped from a job posting is untrusted free text) and turns spaces into underscores,
    // matching this file name scheme's own convention.
    private static string SanitizeForFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().Union(WindowsReservedFileNameChars);
        var cleaned = new string(value.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return cleaned.Replace(' ', '_');
    }

    // Markdown export is a trivial pass-through of the raw TailoredContent - no renderer, no
    // compiler, no Typst involved at all, per this sprint's own decision.
    private static ExportedFile BuildMarkdownFile(TaskItem task, string baseFileName) =>
        new(Encoding.UTF8.GetBytes(task.TailoredContent ?? string.Empty), "text/markdown; charset=utf-8", $"{baseFileName}.md");

    private async Task<Result<ExportedFile>> BuildPdfFileAsync(TaskItem task, string templateFileName, string baseFileName, CancellationToken ct)
    {
        var templateResult = _templates.GetTemplateText(templateFileName);
        if (!templateResult.IsSuccess)
            return Result<ExportedFile>.InternalError(templateResult.Error!);

        var typstContent = _renderer.Render(task.TailoredContent);
        // Verbatim concatenation, not #import: the template's trailing "#show: document" picks up
        // everything appended after it as its body argument.
        var fullSource = templateResult.Value + typstContent;

        var compileResult = await _compiler.CompilePdfAsync(fullSource, ct);
        if (!compileResult.IsSuccess)
            return MapCompileFailure(compileResult);

        return Result<ExportedFile>.Ok(new ExportedFile(compileResult.Value!, "application/pdf", $"{baseFileName}.pdf"));
    }

    // Rides the compiler's own Status/Error through rather than re-deriving a message, so a
    // ResultStatus.Error compile failure isn't swallowed into a different status.
    private static Result<ExportedFile> MapCompileFailure(Result<byte[]> failed) => failed.Status switch
    {
        ResultStatus.NotFound => Result<ExportedFile>.NotFound(failed.Error!),
        ResultStatus.Conflict => Result<ExportedFile>.Conflict(failed.Error!),
        ResultStatus.Validation => Result<ExportedFile>.Invalid(failed.Error!),
        ResultStatus.Unauthorized => Result<ExportedFile>.Unauthorized(failed.Error!),
        _ => Result<ExportedFile>.InternalError(failed.Error ?? "PDF compilation failed.")
    };
}
