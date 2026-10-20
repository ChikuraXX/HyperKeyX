using System.Diagnostics;

namespace HyperKeyX.Infrastructure.Windows;

public sealed class WindowLifetimeRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<nint, Dictionary<Guid, Action>> _watches = [];
    private bool _failed;

    public IDisposable? Watch(nint handle, Action invalidated)
    {
        lock (_gate)
        {
            if (_failed || handle == 0) return null;
            if (!_watches.TryGetValue(handle, out var callbacks))
                _watches.Add(handle, callbacks = []);
            var id = Guid.NewGuid();
            callbacks.Add(id, invalidated);
            return new Registration(this, handle, id);
        }
    }

    public void Notify(uint eventType, nint handle, int objectId, int childId)
    {
        if (eventType is not (0x8000 or 0x8001) || objectId != 0 || childId != 0) return;
        Action[] callbacks;
        lock (_gate)
        {
            if (!_watches.Remove(handle, out var removed)) return;
            // 先摘除后通知，WinEvent 回调重入也不能重复触发或恢复旧绑定。
            callbacks = removed.Values.ToArray();
        }
        Invoke(callbacks);
    }

    public void FailAll()
    {
        Action[] callbacks;
        lock (_gate)
        {
            _failed = true;
            callbacks = _watches.Values.SelectMany(values => values.Values).ToArray();
            _watches.Clear();
        }
        Invoke(callbacks);
    }

    private void Release(nint handle, Guid id)
    {
        lock (_gate)
        {
            if (!_watches.TryGetValue(handle, out var callbacks)) return;
            callbacks.Remove(id);
            if (callbacks.Count == 0) _watches.Remove(handle);
        }
    }

    private static void Invoke(IEnumerable<Action> callbacks)
    {
        foreach (var callback in callbacks)
        {
            try { callback(); }
            catch (Exception exception) { Debug.WriteLine($"窗口失效通知失败：{exception}"); }
        }
    }

    private sealed class Registration(WindowLifetimeRegistry owner, nint handle, Guid id) : IDisposable
    {
        public void Dispose() => owner.Release(handle, id);
    }
}
