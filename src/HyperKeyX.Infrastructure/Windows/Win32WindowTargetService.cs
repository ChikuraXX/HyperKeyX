using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Infrastructure.Windows;

public sealed class Win32WindowTargetService(IWindowNativeApi native) : IWindowTargetService, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BoundState> _bindings = [];
    private bool _disposed;

    public IReadOnlyList<WindowCandidate> EnumerateCandidates(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var candidates = new List<WindowCandidate>();
            foreach (var handle in native.EnumerateWindows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (native.ReadCandidate(handle) is { } candidate)
                    candidates.Add(candidate);
            }
            return candidates.AsReadOnly();
        }
    }

    public WindowBinding? TryBind(WindowCandidate candidate)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            var binding = new WindowBinding(Guid.NewGuid(), candidate);
            var state = new BoundState(binding);
            try
            {
                // 先监视销毁/创建，再复查进程实例；同一进程复用同一 HWND 也不能继续旧会话。
                state.Watch = native.WatchLifetime(candidate.Handle, () => Interlocked.Exchange(ref state.Invalidated, 1));
                if (state.Watch is null || !Current(state))
                {
                    state.Watch?.Dispose();
                    return null;
                }
                _bindings.Add(binding.BindingId, state);
                return binding;
            }
            catch (Exception)
            {
                state.Watch?.Dispose();
                return null;
            }
        }
    }

    public bool IsCurrent(WindowBinding binding)
    {
        lock (_gate) return FindCurrent(binding) is not null;
    }

    public WindowGeometry? GetGeometry(WindowBinding binding)
    {
        lock (_gate)
        {
            if (FindCurrent(binding) is null) return null;
            var geometry = native.ReadGeometry(binding.Candidate.Handle);
            return FindCurrent(binding) is not null ? geometry : null;
        }
    }

    public bool TryActivate(WindowBinding binding)
    {
        lock (_gate)
            return FindCurrent(binding) is not null && native.Activate(binding.Candidate.Handle) && FindCurrent(binding) is not null;
    }

    public bool IsForeground(WindowBinding binding)
    {
        lock (_gate)
            return FindCurrent(binding) is not null && Contains(binding, native.GetForegroundWindow());
    }

    public bool IsHitTarget(WindowBinding binding, PathPosition point)
    {
        lock (_gate)
            return FindCurrent(binding) is not null && PathGeometry.IsFinite(point) && Contains(binding, native.WindowAt(point));
    }

    public IReadOnlyList<PathPixelRectangle> GetMonitors()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Array.AsReadOnly(native.GetMonitors().ToArray());
        }
    }

    public void Release(WindowBinding binding)
    {
        lock (_gate)
        {
            if (_bindings.TryGetValue(binding.BindingId, out var state) && state.Binding == binding)
            {
                _bindings.Remove(binding.BindingId);
                state.Watch?.Dispose();
            }
        }
    }

    private BoundState? FindCurrent(WindowBinding binding) => !_disposed &&
        _bindings.TryGetValue(binding.BindingId, out var state) && state.Binding == binding && Current(state) ? state : null;

    private bool Current(BoundState state)
    {
        try
        {
            if (!native.FlushLifetimeEvents() || Volatile.Read(ref state.Invalidated) != 0) return false;
            var actual = native.ReadCandidate(state.Binding.Candidate.Handle);
            var expected = state.Binding.Candidate;
            return actual is not null && actual.ProcessId == expected.ProcessId &&
                actual.ProcessStartUtcTicks == expected.ProcessStartUtcTicks &&
                actual.WindowClass == expected.WindowClass && Volatile.Read(ref state.Invalidated) == 0;
        }
        catch (Exception) { return false; }
    }

    private bool Contains(WindowBinding binding, nint handle) => handle != 0 &&
        (binding.Candidate.Handle == handle || native.IsChild(binding.Candidate.Handle, handle));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            foreach (var state in _bindings.Values)
            {
                try { state.Watch?.Dispose(); }
                catch (Exception exception) { failures.Add(exception); }
            }
            _bindings.Clear();
            try { native.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count > 0) throw new AggregateException(failures);
        }
    }

    private sealed class BoundState(WindowBinding binding)
    {
        public WindowBinding Binding { get; } = binding;
        public IDisposable? Watch;
        public int Invalidated;
    }
}
