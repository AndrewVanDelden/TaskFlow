using TaskFlow.Api.Models;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// A proposed task produced by ingestion, before it is persisted to the board.
/// <c>Section</c> is the source heading it came from (provenance). <c>Company</c> is optional and
/// trailing so every existing 4-arg call site (generic ingestion parsers, existing tests) keeps
/// compiling unchanged; only the job-posting parsers populate it.
/// </summary>
public sealed record TaskDraft(string Title, string? Description, TaskKind Kind, string Section, string? Company = null);
