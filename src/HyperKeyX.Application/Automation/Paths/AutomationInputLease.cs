using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record InputLeaseResult(IDisposable? Lease, PathIssue? Issue)
{
    public bool Succeeded => Lease is not null && Issue is null;
}

/// <summary>录制和轨迹独占，旧输入生产者可并存；拒绝的请求不排队。</summary>
public sealed class AutomationInputLease
{
    private readonly object _gate = new();
    private int _activeCount;
    private Kind _exclusive;
    public bool IsPathActive { get { lock (_gate) return _exclusive == Kind.Path; } }
    public bool IsRecording { get { lock (_gate) return _exclusive == Kind.Recording; } }

    public InputLeaseResult TryAcquire(bool hasPath, CancellationToken token = default) =>
        Acquire(hasPath ? Kind.Path : Kind.Legacy, token);
    public InputLeaseResult TryAcquireRecording(CancellationToken token = default) => Acquire(Kind.Recording, token);

    /// <summary>延续已有会话的清理占用，不产生新输入；即使原独占任务正在取消，也不留下录制接管空隙。</summary>
    internal IDisposable ReserveCleanup()
    {
        lock (_gate)
        {
            _activeCount++;
            return new Lease(this, Kind.Legacy);
        }
    }

    private InputLeaseResult Acquire(Kind kind, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_exclusive != Kind.Legacy || (kind != Kind.Legacy && _activeCount != 0))
                return new(null, new(PathIssueCode.InputBusy));
            _activeCount++;
            if (kind != Kind.Legacy) _exclusive = kind;
            return new(new Lease(this, kind), null);
        }
    }

    private void Release(Kind kind)
    {
        lock (_gate)
        {
            _activeCount--;
            if (kind != Kind.Legacy) _exclusive = Kind.Legacy;
        }
    }

    private enum Kind { Legacy, Path, Recording }
    private sealed class Lease(AutomationInputLease owner, Kind kind) : IDisposable
    {
        private AutomationInputLease? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(kind);
    }
}
