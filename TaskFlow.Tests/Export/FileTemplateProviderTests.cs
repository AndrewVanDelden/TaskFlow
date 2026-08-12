using FluentAssertions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Export;
using Xunit;

namespace TaskFlow.Tests.Export;

/// <summary>
/// PR #48 Copilot review finding: ExportService's old private GetTemplateText cached a template
/// read (including a thrown exception) in a static Lazy&lt;string&gt; for the process lifetime -
/// one transient read failure (permission glitch, disk hiccup) would permanently break every later
/// export of that document kind, and the failure surfaced as an unhandled exception rather than a
/// Result. Extracted here as its own seam (mirroring ITypstCompiler's existing precedent in this
/// file) specifically so this caching/failure behavior is unit-testable in isolation, without
/// needing to manipulate ExportService's real template files.
/// </summary>
public class FileTemplateProviderTests
{
    [Fact]
    public void GetTemplateText_returns_the_real_resume_template_text()
    {
        var provider = new FileTemplateProvider();

        var result = provider.GetTemplateText("resume.typ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("#let document");
    }

    [Fact]
    public void GetTemplateText_returns_InternalError_without_throwing_when_the_file_is_missing()
    {
        var provider = new FileTemplateProvider();

        var result = provider.GetTemplateText($"missing-{Guid.NewGuid()}.typ");

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Error);
    }

    [Fact]
    public void GetTemplateText_caches_a_successful_read_so_repeated_calls_return_the_same_cached_instance()
    {
        var provider = new FileTemplateProvider();

        var first = provider.GetTemplateText("resume.typ");
        var second = provider.GetTemplateText("resume.typ");

        ReferenceEquals(first.Value, second.Value).Should().BeTrue();
    }

    // The actual bug: a failed read must never be permanently cached - once the underlying problem
    // clears (the file now exists), the very next call must succeed, not keep replaying the old
    // failure. Uses a real, uniquely-named scratch file under the same Templates directory so no
    // other test or parallel run is affected.
    [Fact]
    public void GetTemplateText_does_not_permanently_cache_a_failed_read_and_recovers_once_the_file_appears()
    {
        var fileName = $"recovers-{Guid.NewGuid()}.typ";
        var path = Path.Combine(AppContext.BaseDirectory, "Export", "Templates", fileName);
        var provider = new FileTemplateProvider();

        try
        {
            var firstAttempt = provider.GetTemplateText(fileName);
            firstAttempt.IsSuccess.Should().BeFalse();

            File.WriteAllText(path, "= Recovered\n");
            var secondAttempt = provider.GetTemplateText(fileName);

            secondAttempt.IsSuccess.Should().BeTrue();
            secondAttempt.Value.Should().Be("= Recovered\n");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
