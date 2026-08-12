using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
/// Executed for real (2026-08-11, after the T5.1a commit): the `typst` binary was installed and
/// these tests were run against it directly, confirming RED-then-GREEN rather than leaving them
/// unverified. One real bug surfaced during that run, since fixed and recorded above and in
/// TaskFlow_Epic3_ResumeBuilder.md - the invalid-syntax and timeout tests originally asserted only
/// `IsSuccess == false`, which also holds if the binary is simply unresolvable (Process.Start
/// fails near-instantly), so both weakly passed even when typst couldn't be found at all. Now they
/// assert the exact failure message, which does distinguish those cases.
/// </summary>
[Trait("RequiresTypstBinary", "true")]
public class ProcessTypstCompilerTests
{
    private static IConfiguration Config(int? timeoutSeconds = null, int? maxConcurrent = null)
    {
        var values = new Dictionary<string, string?>();
        if (timeoutSeconds is not null)
            values["Export:TypstCompileTimeoutSeconds"] = timeoutSeconds.Value.ToString();
        if (maxConcurrent is not null)
            values["Export:MaxConcurrentTypstCompiles"] = maxConcurrent.Value.ToString();

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ProcessTypstCompiler CreateSut(int? timeoutSeconds = null, int? maxConcurrent = null, ILogger<ProcessTypstCompiler>? logger = null) =>
        new(Config(timeoutSeconds, maxConcurrent), logger ?? NullLogger<ProcessTypstCompiler>.Instance);

    // A minimal ILogger test double that captures formatted messages, so a test can assert on
    // exactly what did (and did not) get logged, without a mocking library's ILogger extension-
    // method awkwardness (LogError etc. are extension methods over the single Log<TState> method).
    private sealed class CapturingLogger : ILogger<ProcessTypstCompiler>
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

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
        // Confirmed by direct observation (2026-08-11, once typst was installed mid-session): with
        // the binary unresolvable, this assertion alone still passed, because ProcessTypstCompiler
        // also reports IsSuccess == false when Process.Start itself fails to find the executable -
        // "PDF compilation failed to start." vs. this test's real target, "PDF compilation failed."
        // (the non-zero-exit path). Asserting the exact message is what actually proves typst ran
        // and rejected the source, rather than never having been invoked at all.
        result.Error.Should().Be("PDF compilation failed.");
    }

    // Copilot review finding (PR #48): stderr can carry Typst's own diagnostic source excerpts,
    // which for real usage would be the (mostly-escaped) resume/cover-letter content - logging it
    // raw risks persisting PII in server logs even though it never reaches the HTTP response. The
    // literal marker below would appear in Typst's error output if stderr content ever leaked into
    // a logged message.
    [Fact]
    public async Task Invalid_Typst_source_logs_the_exit_code_but_never_the_raw_stderr_content()
    {
        var logger = new CapturingLogger();
        var sut = CreateSut(logger: logger);

        await sut.CompilePdfAsync("#this_marker_should_never_appear_in_logs()");

        logger.Messages.Should().NotContain(m => m.Contains("this_marker_should_never_appear_in_logs"));
        logger.Messages.Should().Contain(m => m.Contains("exited with code"));
    }

    // Copilot review finding (PR #48): every export request spawned an unbounded, CPU-intensive
    // typst subprocess with no concurrency cap - an authenticated client could exhaust CPU/memory
    // by firing many concurrent exports. Proven via timing (matching this file's own timeout test's
    // style): with the limit set to 1, a second long-running compile cannot even start until the
    // first's timeout releases its slot, so two concurrent calls take roughly 2x one call's
    // timeout - not ~1x, as they would if allowed to race in parallel.
    [Fact]
    public async Task Concurrent_compiles_beyond_the_configured_limit_are_queued_not_run_in_parallel()
    {
        var sut = CreateSut(timeoutSeconds: 2, maxConcurrent: 1);
        const string longRunningSource = "#for i in range(100000000) { for j in range(1000) { } }";

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(
            sut.CompilePdfAsync(longRunningSource),
            sut.CompilePdfAsync(longRunningSource));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public async Task Long_running_source_is_killed_by_the_configured_timeout_and_returns_failure_within_bounds()
    {
        // The originally-flagged-as-unverified premise turned out to be wrong when actually run
        // (2026-08-11): `#while true { }` does NOT hang typst - it's statically rejected at compile
        // time ("error: condition is always true"), confirmed by running it directly against the
        // real binary, so it exercised the non-zero-exit path, not the timeout path, and the test
        // was silently passing for the wrong reason. A large but genuinely finite nested loop does
        // not trip that static check and was confirmed, by direct manual probing with a plain
        // `timeout` wrapper outside this test, to still be running past an 8s budget - a real,
        // observed long-running computation, not a hang trick relying on typst's own scripting
        // limits.
        var sut = CreateSut(timeoutSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        var result = await sut.CompilePdfAsync("#for i in range(100000000) { for j in range(1000) { } }");
        stopwatch.Stop();

        result.IsSuccess.Should().BeFalse();
        // Same reasoning as the invalid-syntax test above: IsSuccess == false alone would also pass
        // if typst were simply unresolvable (a near-instant Process.Start failure), which would
        // trivially satisfy the "under 15s" bound below too without ever proving a timeout actually
        // happened. The exact message distinguishes "timed out" from "failed to start."
        result.Error.Should().Be("PDF compilation timed out.");
        // Generous upper bound relative to the 1s configured timeout - proves the call returns
        // instead of hanging the test suite, without being a flaky tight timing assertion.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
