using TaskFlow.Api.Models;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// A proposed task produced by ingestion, before it is persisted to the board.
/// <c>Section</c> is the source heading it came from (provenance).
/// </summary>
public sealed record TaskDraft(string Title, string? Description, TaskKind Kind, string Section);
