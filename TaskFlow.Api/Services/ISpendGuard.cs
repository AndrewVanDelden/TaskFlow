namespace TaskFlow.Api.Services;

/// <summary>Decides whether the executor may run this cycle, given a spend/usage budget.</summary>
public interface ISpendGuard
{
    Task<bool> CanRunAsync(CancellationToken ct = default);
}
