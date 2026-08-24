using Microsoft.Extensions.Configuration;

namespace TaskFlow.Api.Services;

/// <summary>
/// In-memory executor switch. Default comes from <c>Agents:ExecutorEnabled</c> (default false), so the
/// executor is paused until a human enables it. <c>volatile</c> because it is read on the background
/// agent thread and written on request threads.
/// </summary>
public class ExecutorSwitch : IExecutorSwitch
{
    private volatile bool _enabled;

    // Bounded to 1: the loop only ever needs to know "something happened since I last checked", not
    // how many times Enable() was called, so a Release() racing an already-pending wake is expected,
    // not an error.
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    public ExecutorSwitch(IConfiguration config) =>
        _enabled = config.GetValue("Agents:ExecutorEnabled", false);

    public bool IsEnabled => _enabled;

    public void Enable()
    {
        _enabled = true;
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake is already pending and not yet consumed; the loop will still wake once, which
            // is all this signal promises.
        }
    }

    public void Disable() => _enabled = false;

    public Task WaitForWakeAsync(CancellationToken cancellationToken) => _wakeSignal.WaitAsync(cancellationToken);
}
