using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFlow.Api.Export;
using Xunit;

namespace TaskFlow.Tests.Export;

/// <summary>
/// Real-binary end-to-end tests for <see cref="ProcessTypstCompiler"/>, per Sprint 5's
/// testing-strategy decision (TaskFlow_Epic3_ResumeBuilder.md, "Decisions owned here" under
/// "Sprint 5 — Artifact Export"): a small number of tests actually shell out to the real
/// <c>typst</c> CLI and are excluded from the default <c>.\test</c> run via the
/// <c>RequiresTypstBinary</c> trait, since the binary is not guaranteed to be installed on every
/// machine that runs the suite. Opt in explicitly with
/// <c>dotnet test --filter "RequiresTypstBinary=true"</c> on a machine that has <c>typst</c> on
/// PATH.
///
/// NOT EXECUTED as of T5.1a: the `typst` binary is not installed on this machine, so these tests
/// were written carefully against Typst's documented CLI/scripting contract but could not actually
/// be run to confirm RED-then-GREEN. See the T5.1a report for exactly what was and wasn't verified.
/// </summary>
[Trait("RequiresTypstBinary", "true")]
public class ProcessTypstCompilerTests
{
    private static IConfiguration Config(int? timeoutSeconds = null)
    {
        var values = new Dictionary<string, string?>();
        if (timeoutSeconds is not null)
            values["Export:TypstCompileTimeoutSeconds"] = timeoutSeconds.Value.ToString();

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ProcessTypstCompiler CreateSut(int? timeoutSeconds = null) =>
        new(Config(timeoutSeconds), NullLogger<ProcessTypstCompiler>.Instance);

    [Fact]
    public async Task Valid_Typst_source_compiles_to_PDF_bytes()
    {
        var sut = CreateSut();

        // Plain text is itself valid minimal Typst markup - a document with no directives at all
        // renders as a one-paragraph PDF.
        var result = await sut.CompilePdfAsync("Hello, world!");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        var magic = Encoding.ASCII.GetString(result.Value!, 0, Math.Min(5, result.Value!.Length));
        magic.Should().Be("%PDF-");
    }

    [Fact]
    public async Task Invalid_Typst_source_returns_failure_Result_without_throwing()
    {
        var sut = CreateSut();

        // Calling an undefined function is a compile-time error in Typst - a reliable way to force
        // a non-zero exit without depending on any specific diagnostic message wording. Awaiting
        // this directly (rather than wrapping in a throw-assertion) is itself the "does not throw"
        // proof: an unhandled exception here would fail the test.
        var result = await sut.CompilePdfAsync("#this_function_does_not_exist()");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Long_running_source_is_killed_by_the_configured_timeout_and_returns_failure_within_bounds()
    {
        // Typst's scripting layer supports `while` loops (confirmed against Typst's documented
        // syntax); `#while true { }` should never terminate on its own. UNVERIFIED: this has not
        // been executed to confirm it actually hangs the real typst process rather than, say, being
        // rejected at parse/compile time some other way - flagged explicitly in the T5.1a report
        // rather than asserted as fact.
        var sut = CreateSut(timeoutSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        var result = await sut.CompilePdfAsync("#while true { }");
        stopwatch.Stop();

        result.IsSuccess.Should().BeFalse();
        // Generous upper bound relative to the 1s configured timeout - proves the call returns
        // instead of hanging the test suite, without being a flaky tight timing assertion.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
