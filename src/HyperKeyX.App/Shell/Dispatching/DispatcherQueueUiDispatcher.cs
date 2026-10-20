using Microsoft.UI.Dispatching;

namespace HyperKeyX.Shell.Dispatching;

public sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueUiDispatcher()
    {
        _queue = DispatcherQueue.GetForCurrentThread() ??
            throw new InvalidOperationException("The WinUI dispatcher is unavailable.");
    }

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _queue.TryEnqueue(() => action());
    }
}
