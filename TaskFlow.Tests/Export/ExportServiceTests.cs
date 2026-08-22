using System.Text;
using FluentAssertions;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Export;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using Xunit;

namespace TaskFlow.Tests.Export;

/// <summary>
/// T5.1d: ExportService ties ITypstCompiler (T5.1a), TailoredContentTypstRenderer (T5.1b), and the
/// resume/cover-letter templates (T5.1c) together, behind the same ownership/state-guard
/// convention as JobApplicationService.ApproveAsync (see JobApplicationServiceTests, mirrored
/// here). Mocked repositories and compiler; TailoredContentTypstRenderer is pure/stateless so a
/// real instance is constructed rather than mocked (mocking a deterministic pure class you can
/// just instantiate is unnecessary ceremony).
/// </summary>
public class ExportServiceTests
{
    private const int OwnerId = 1;
    private const int ApplicationId = 5;
    private const int ResumeTaskId = 10;
    private const int CoverLetterTaskId = 11;

    private readonly Mock<IJobApplicationRepository> _applications = new();
    private readonly Mock<ITaskRepository> _tasks = new();
    private readonly Mock<ITypstCompiler> _compiler = new();
    private readonly TailoredContentTypstRenderer _renderer = new();

    // A real FileTemplateProvider by default, so the existing "Pdf" tests below keep proving
    // composition against the actual resume.typ/cover-letter.typ content. Tests that need to
    // simulate a template load failure pass a mocked ITemplateProvider instead.
    private ExportService CreateSut(ITemplateProvider? templates = null) =>
        new(_applications.Object, _tasks.Object, _compiler.Object, _renderer, templates ?? new FileTemplateProvider());

    private static JobApplication Application(ApplicationState state, int ownerId = OwnerId) => new()
    {
        Id = ApplicationId,
        OwnerId = ownerId,
        State = state,
        IngestionSessionId = "session-A"
    };

    private static List<TaskItem> Siblings(string resumeContent = "Resume body.", string coverLetterContent = "Cover letter body.") => new()
    {
        new TaskItem { Id = ResumeTaskId, Title = "Tailor resume", Kind = TaskKind.ResumeTailoring, ApplicationId = ApplicationId, Status = WorkflowStatus.Done, TailoredContent = resumeContent },
        new TaskItem { Id = CoverLetterTaskId, Title = "Tailor cover letter", Kind = TaskKind.CoverLetterTailoring, ApplicationId = ApplicationId, Status = WorkflowStatus.Done, TailoredContent = coverLetterContent }
    };

    private void SetUpApprovedApplication(string resumeContent = "Resume body.", string coverLetterContent = "Cover letter body.")
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(ApplicationState.Approved));
        _tasks.Setup(t => t.GetByApplicationIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Siblings(resumeContent, coverLetterContent));
    }

    // ── Markdown: no Typst involved at all ──────────────────────────────────────
    [Fact]
    public async Task ExportResumeAsync_Markdown_returns_the_raw_tailored_content_and_never_calls_the_compiler()
    {
        SetUpApprovedApplication(resumeContent: "UniqueResumeMarkerXYZ123");

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/markdown; charset=utf-8");
        result.Value.FileName.Should().Be("resume.md");
        Encoding.UTF8.GetString(result.Value.Content).Should().Be("UniqueResumeMarkerXYZ123");
        _compiler.Verify(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportCoverLetterAsync_Markdown_returns_the_raw_tailored_content_and_never_calls_the_compiler()
    {
        SetUpApprovedApplication(coverLetterContent: "UniqueCoverLetterMarkerXYZ123");

        var result = await CreateSut().ExportCoverLetterAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/markdown; charset=utf-8");
        result.Value.FileName.Should().Be("cover-letter.md");
        Encoding.UTF8.GetString(result.Value.Content).Should().Be("UniqueCoverLetterMarkerXYZ123");
        _compiler.Verify(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Pdf: template + rendered content composed, compiler invoked ────────────
    [Fact]
    public async Task ExportResumeAsync_Pdf_composes_the_resume_template_with_rendered_content_and_returns_compiled_bytes()
    {
        SetUpApprovedApplication(resumeContent: "UniqueResumeMarkerXYZ123");
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        string? capturedSource = null;
        _compiler.Setup(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((source, _) => capturedSource = source)
            .ReturnsAsync(Result<byte[]>.Ok(pdfBytes));

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Pdf);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("resume.pdf");
        result.Value.Content.Should().BeEquivalentTo(pdfBytes);
        capturedSource.Should().Contain("#let document");
        capturedSource.Should().Contain("UniqueResumeMarkerXYZ123");
    }

    [Fact]
    public async Task ExportCoverLetterAsync_Pdf_composes_the_cover_letter_template_with_rendered_content_and_returns_compiled_bytes()
    {
        SetUpApprovedApplication(coverLetterContent: "UniqueCoverLetterMarkerXYZ123");
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        string? capturedSource = null;
        _compiler.Setup(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((source, _) => capturedSource = source)
            .ReturnsAsync(Result<byte[]>.Ok(pdfBytes));

        var result = await CreateSut().ExportCoverLetterAsync(ApplicationId, OwnerId, ExportFormat.Pdf);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("cover-letter.pdf");
        result.Value.Content.Should().BeEquivalentTo(pdfBytes);
        capturedSource.Should().Contain("#let document");
        capturedSource.Should().Contain("UniqueCoverLetterMarkerXYZ123");
    }

    // ── Compile failure propagates, not swallowed ───────────────────────────────
    [Fact]
    public async Task ExportResumeAsync_Pdf_propagates_a_compiler_failure_instead_of_swallowing_it_into_success()
    {
        SetUpApprovedApplication();
        _compiler.Setup(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<byte[]>.InternalError("PDF compilation failed."));

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Pdf);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Error);
        result.Value.Should().BeNull();
        result.Error.Should().Be("PDF compilation failed.");
    }

    // ── Template load failure surfaces as a Result, never an unhandled exception ─
    // Copilot review finding (PR #48): the old private GetTemplateText threw straight out of a
    // static Lazy<string> on a read failure. Proven here via a mocked ITemplateProvider so no real
    // file needs to be broken - FileTemplateProviderTests covers the provider's own caching/retry
    // behavior in isolation.
    [Fact]
    public async Task ExportResumeAsync_Pdf_surfaces_a_template_load_failure_as_an_Error_result_instead_of_throwing()
    {
        SetUpApprovedApplication();
        var templates = new Mock<ITemplateProvider>();
        templates.Setup(t => t.GetTemplateText("resume.typ"))
            .Returns(Result<string>.InternalError("Failed to load export template 'resume.typ'."));

        var result = await CreateSut(templates.Object).ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Pdf);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Error);
        result.Error.Should().Be("Failed to load export template 'resume.typ'.");
        _compiler.Verify(c => c.CompilePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── State guard ──────────────────────────────────────────────────────────────
    // ReviewReady is no longer refused (see the two ReviewReady-succeeds tests below): a user
    // reviewing a pair needs to inspect the real PDF/Markdown output before deciding to approve or
    // reject it, and the underlying render/compile pipeline has no dependency on approval state at
    // all - it only ever reads TailoredContent, which already exists once a task reaches Review.
    // Building is still refused: neither sibling has necessarily finished yet, so there may be
    // nothing (or only one artifact) to export.
    [Theory]
    [InlineData(ApplicationState.Building)]
    public async Task ExportResumeAsync_refuses_a_non_ReviewReady_non_Approved_application_with_Validation(ApplicationState state)
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(state));

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.GetByApplicationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ApplicationState.Building)]
    public async Task ExportCoverLetterAsync_refuses_a_non_ReviewReady_non_Approved_application_with_Validation(ApplicationState state)
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(state));

        var result = await CreateSut().ExportCoverLetterAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.GetByApplicationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportResumeAsync_succeeds_for_a_ReviewReady_application()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(ApplicationState.ReviewReady));
        _tasks.Setup(t => t.GetByApplicationIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Siblings(resumeContent: "UniqueResumeMarkerXYZ123"));

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.IsSuccess.Should().BeTrue();
        Encoding.UTF8.GetString(result.Value!.Content).Should().Be("UniqueResumeMarkerXYZ123");
    }

    [Fact]
    public async Task ExportCoverLetterAsync_succeeds_for_a_ReviewReady_application()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(ApplicationState.ReviewReady));
        _tasks.Setup(t => t.GetByApplicationIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Siblings(coverLetterContent: "UniqueCoverLetterMarkerXYZ123"));

        var result = await CreateSut().ExportCoverLetterAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.IsSuccess.Should().BeTrue();
        Encoding.UTF8.GetString(result.Value!.Content).Should().Be("UniqueCoverLetterMarkerXYZ123");
    }

    // ── Ownership guard: missing and wrong-owner are indistinguishable ─────────
    [Fact]
    public async Task ExportResumeAsync_returns_NotFound_when_the_application_does_not_exist()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync((JobApplication?)null);

        var result = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ExportResumeAsync_missing_and_wrong_owner_produce_the_identical_NotFound_message()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync((JobApplication?)null);
        var missingResult = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application(ApplicationState.Approved, ownerId: 999));
        var wrongOwnerResult = await CreateSut().ExportResumeAsync(ApplicationId, OwnerId, ExportFormat.Markdown);

        missingResult.Status.Should().Be(ResultStatus.NotFound);
        wrongOwnerResult.Status.Should().Be(ResultStatus.NotFound);
        wrongOwnerResult.Error.Should().Be(missingResult.Error);
    }
}
