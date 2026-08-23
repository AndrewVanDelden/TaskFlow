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

    public Task<Result<ExportedFile>> ExportResumeAsync(int applicationId, int callerId, ExportFormat format, CancellationToken ct = default) =>
        ExportAsync(applicationId, callerId, format, TaskKind.ResumeTailoring, "resume.typ", "resume", ct);

    public Task<Result<ExportedFile>> ExportCoverLetterAsync(int applicationId, int callerId, ExportFormat format, CancellationToken ct = default) =>
        ExportAsync(applicationId, callerId, format, TaskKind.CoverLetterTailoring, "cover-letter.typ", "cover-letter", ct);

    // Shared by both public methods so the ownership/state guard and sibling-fetch logic lives in
    // exactly one place (DRY) - the only differences between a resume and a cover-letter export
    // are which sibling TaskKind to pick, which template to compose with, and the base file name.
    private async Task<Result<ExportedFile>> ExportAsync(
        int applicationId, int callerId, ExportFormat format, TaskKind kind, string templateFileName, string baseFileName, CancellationToken ct)
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

        return format switch
        {
            ExportFormat.Markdown => Result<ExportedFile>.Ok(BuildMarkdownFile(task, baseFileName)),
            ExportFormat.Pdf => await BuildPdfFileAsync(task, templateFileName, baseFileName, ct),
            _ => Result<ExportedFile>.Invalid($"Unsupported export format '{format}'.")
        };
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
