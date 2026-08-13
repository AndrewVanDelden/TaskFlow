using TaskFlow.Api.Common;

namespace TaskFlow.Api.Export;

/// <summary>
/// Seam over the external <c>typst</c> CLI (no mature, officially-maintained .NET binding exists —
/// confirmed via research, see Sprint 5 "Decisions owned here" in
/// TaskFlow_Epic3_ResumeBuilder.md). Compiles Typst source markup into a PDF. Never throws for
/// expected failure cases (non-zero exit, timeout) — those surface as a failed <see cref="Result{T}"/>,
/// matching this codebase's service convention.
/// </summary>
public interface ITypstCompiler
{
    Task<Result<byte[]>> CompilePdfAsync(string typstSource, CancellationToken ct = default);
}
