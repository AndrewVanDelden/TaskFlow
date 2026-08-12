using System.Diagnostics;
using System.Text;
using TaskFlow.Api.Common;

namespace TaskFlow.Api.Export;

/// <summary>
/// Shells out to the external <c>typst</c> CLI (<c>typst compile - -</c>, reading Typst source from
/// stdin and writing the compiled PDF to stdout — confirmed supported by the real Typst CLI). No
/// temp files are written or cleaned up anywhere; everything passes through pipes.
///
/// Security/robustness, matching Sprint 5's "Decisions owned here" in
/// TaskFlow_Epic3_ResumeBuilder.md:
/// - <c>--root</c> is pinned to a dedicated, empty, never-written-to sandbox directory created once
///   in the constructor — defense in depth so Typst's own <c>read()</c>/<c>#import</c> cannot reach
///   real files even if the caller's own escaping ever has a bug.
/// - A configured timeout (<c>Export:TypstCompileTimeoutSeconds</c>, default 15s) kills the entire
///   process tree rather than letting a pathological input hang a request.
/// - A non-zero exit or any subprocess failure logs the detail (which may include absolute paths)
///   server-side only; the <see cref="Result{T}"/> returned to the caller carries a generic message,
///   never stderr content.
/// </summary>
public class ProcessTypstCompiler : ITypstCompiler
{
    private readonly IConfiguration _config;
    private readonly ILogger<ProcessTypstCompiler> _logger;
    private readonly string _sandboxRoot;

    public ProcessTypstCompiler(IConfiguration config, ILogger<ProcessTypstCompiler> logger)
    {
        _config = config;
        _logger = logger;

        // Dedicated, empty, never-written-to sandbox directory pinned as --root. Created once here
        // (not the real filesystem root) so Typst's read()/#import has nothing reachable even if
        // upstream escaping ever has a bug. Never written to by this class - it exists purely to be
        // an empty --root.
        _sandboxRoot = Path.Combine(Path.GetTempPath(), "taskflow-typst-sandbox");
        Directory.CreateDirectory(_sandboxRoot);
    }

    public async Task<Result<byte[]>> CompilePdfAsync(string typstSource, CancellationToken ct = default)
    {
        var binaryPath = _config.GetValue("Export:TypstBinaryPath", "typst") ?? "typst";
        var timeoutSeconds = Math.Max(1, _config.GetValue("Export:TypstCompileTimeoutSeconds", 15));

        var psi = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("compile");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("--root");
        psi.ArgumentList.Add(_sandboxRoot);

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Typst compiler process at '{BinaryPath}'.", binaryPath);
            return Result<byte[]>.InternalError("PDF compilation failed to start.");
        }

        // Write stdin and read stdout/stderr concurrently, not sequentially - Typst may start
        // writing to stdout before it has finished reading stdin, and stdout/stderr pipes have a
        // bounded OS buffer, so writing all of stdin first (while nothing drains stdout) can deadlock.
        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdinTask = WriteStdinAsync(process, typstSource, ct);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // Our own configured timeout, not caller cancellation: kill the whole tree rather than
            // let a pathological input hang the request, and report failure via Result, not a throw.
            KillSafely(process);
            await ObserveAsync(stdinTask, stdoutTask, stderrTask);
            _logger.LogError("Typst compile timed out after {TimeoutSeconds}s.", timeoutSeconds);
            return Result<byte[]>.InternalError("PDF compilation timed out.");
        }
        catch (OperationCanceledException)
        {
            // Caller's own token was cancelled - kill the subprocess, then propagate the
            // cancellation as an exception, matching normal .NET cancellation semantics (this is not
            // an "expected failure" a Result should encode, unlike a timeout or non-zero exit).
            KillSafely(process);
            throw;
        }

        byte[] stdoutBytes;
        string stderrText;
        try
        {
            stdoutBytes = await stdoutTask;
            stderrText = await stderrTask;
            await stdinTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed reading Typst compiler process streams.");
            return Result<byte[]>.InternalError("PDF compilation failed.");
        }

        if (process.ExitCode != 0)
        {
            // stderr may contain absolute paths - log server-side only, never in the returned Result.
            _logger.LogError(
                "Typst compile exited with code {ExitCode}. stderr: {Stderr}",
                process.ExitCode, stderrText);
            return Result<byte[]>.InternalError("PDF compilation failed.");
        }

        return Result<byte[]>.Ok(stdoutBytes);
    }

    private static void KillSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort: the process may have already exited between the check and the kill.
        }
    }

    private static async Task ObserveAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // We're already returning a failure Result; this just prevents unobserved task
            // exceptions from the streams we abandoned after killing the process.
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static async Task WriteStdinAsync(Process process, string source, CancellationToken ct)
    {
        try
        {
            await using var stdin = process.StandardInput.BaseStream;
            var bytes = Encoding.UTF8.GetBytes(source);
            await stdin.WriteAsync(bytes, ct);
            await stdin.FlushAsync(ct);
        }
        catch (IOException)
        {
            // The child process closed stdin early (e.g. it already failed) - nothing left to write to.
        }
        catch (ObjectDisposedException)
        {
            // Same as above: the stream was closed from the other end.
        }
    }
}
