using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Composes two parsers free-first: run the free parser, and only escalate to the paid one when
/// the free parser produced nothing (content it could not structure). Free when it reaches the
/// outcome, agent when it must. The composition root wires the free and paid implementations, so
/// this class depends only on the <see cref="IIngestionParser"/> abstraction (DIP).
/// </summary>
public sealed class TieredIngestionParser : IIngestionParser
{
    private readonly IIngestionParser _free;
    private readonly IIngestionParser _paid;

    public TieredIngestionParser(IIngestionParser free, IIngestionParser paid)
    {
        _free = free;
        _paid = paid;
    }

    public async Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default)
    {
        var free = await _free.ParseAsync(documentText, cancellationToken);

        // Free reached the outcome (or errored) - do not pay.
        if (!free.IsSuccess || free.Value!.Count > 0)
            return free;

        // Free found nothing structured; escalate. The paid parser handles its own no-key case.
        return await _paid.ParseAsync(documentText, cancellationToken);
    }
}
