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

    public ExecutorSwitch(IConfiguration config) =>
        _enabled = config.GetValue("Agents:ExecutorEnabled", false);

    public bool IsEnabled => _enabled;
    public void Enable() => _enabled = true;
    public void Disable() => _enabled = false;
}
