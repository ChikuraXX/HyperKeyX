using System.Collections.ObjectModel;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record WindowGroupResolution(WindowGroupGuard? Guard, PathIssue? Issue)
{
    public bool IsValid => Guard is not null && Issue is null;
}

public sealed class WindowTargetSession : IDisposable
{
    private readonly IWindowTargetService _service;
    private readonly Dictionary<Guid, WindowGroupGuard> _groups = [];
    private readonly List<PathIssue> _cleanupIssues = [];
    private Guid? _activeBindingId;
    private bool _disposed;
    public PathIssue? Failure { get; private set; }

    internal WindowTargetSession(IWindowTargetService service, Dictionary<Guid, WindowBinding> bindings)
    {
        _service = service;
        Bindings = new ReadOnlyDictionary<Guid, WindowBinding>(new Dictionary<Guid, WindowBinding>(bindings));
    }

    public IReadOnlyDictionary<Guid, WindowBinding> Bindings { get; }
    public IReadOnlyList<PathIssue> CleanupIssues => _cleanupIssues.AsReadOnly();

    public PathIssue? ValidateBindings()
    {
        if (Failure is not null) return Failure;
        if (_disposed) return Fail(new(PathIssueCode.TargetUnavailable));
        foreach (var pair in Bindings)
        {
            try
            {
                if (!_service.IsCurrent(pair.Value))
                    return Fail(new(PathIssueCode.TargetUnavailable, TargetId: pair.Key));
            }
            catch (Exception) { return Fail(new(PathIssueCode.TargetUnavailable, TargetId: pair.Key)); }
        }
        return null;
    }

    public WindowGroupResolution EnterGroup(Guid groupId, Guid targetId, bool hasHeldInput)
    {
        if (ValidateBindings() is { } failure) return new(null, failure);
        if (!Bindings.TryGetValue(targetId, out var binding))
            return new(null, Fail(new(PathIssueCode.MissingTarget, TargetId: targetId)));
        if (_groups.TryGetValue(groupId, out var existing))
        {
            if (existing.TargetId != targetId)
                return new(null, Fail(new(PathIssueCode.InvalidGroup, TargetId: targetId)));
            return new(existing, existing.Validate());
        }
        if (hasHeldInput && _activeBindingId != binding.BindingId)
            return new(null, Fail(new(PathIssueCode.TargetSwitchWhileHeld, TargetId: targetId)));
        try
        {
            var geometry = _service.GetGeometry(binding);
            if (geometry is not { IsUsable: true })
                return new(null, Fail(new(PathIssueCode.TargetUnavailable, TargetId: targetId)));
            if (!_service.TryActivate(binding) || !_service.IsForeground(binding))
                return new(null, Fail(new(PathIssueCode.TargetUnavailable, TargetId: targetId)));
            geometry = _service.GetGeometry(binding);
            if (geometry is not { IsUsable: true } || !_service.IsCurrent(binding))
                return new(null, Fail(new(PathIssueCode.TargetUnavailable, TargetId: targetId)));
            var guard = new WindowGroupGuard(this, _service, targetId, binding, geometry);
            _groups.Add(groupId, guard);
            _activeBindingId = binding.BindingId;
            return new(guard, null);
        }
        catch (Exception) { return new(null, Fail(new(PathIssueCode.TargetUnavailable, TargetId: targetId))); }
    }

    public PathIssue? BeginCycle(bool hasHeldInput)
    {
        if (ValidateBindings() is { } failure) return failure;
        if (hasHeldInput) return Fail(new(PathIssueCode.TargetSwitchWhileHeld));
        _groups.Clear();
        _activeBindingId = null;
        return null;
    }

    internal PathIssue Fail(PathIssue issue) => Failure ??= issue;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var binding in Bindings.Values.DistinctBy(b => b.BindingId))
        {
            try { _service.Release(binding); }
            catch (Exception) { _cleanupIssues.Add(new(PathIssueCode.TargetUnavailable)); }
        }
        _groups.Clear();
    }
}

public sealed class WindowGroupGuard
{
    private readonly WindowTargetSession _session;
    private readonly IWindowTargetService _service;
    internal WindowGroupGuard(WindowTargetSession session, IWindowTargetService service, Guid targetId,
        WindowBinding binding, WindowGeometry geometry)
    {
        _session = session; _service = service; TargetId = targetId; Binding = binding; Geometry = geometry;
    }
    public Guid TargetId { get; }
    public WindowBinding Binding { get; }
    public WindowGeometry Geometry { get; }

    public PathIssue? Validate(PathPosition? clickPosition = null)
    {
        if (_session.ValidateBindings() is { } failure) return failure;
        try
        {
            if (_service.GetGeometry(Binding) != Geometry)
                return _session.Fail(new(PathIssueCode.TargetChanged, TargetId: TargetId));
            if (!_service.IsForeground(Binding) || (clickPosition is { } point && !_service.IsHitTarget(Binding, point)))
                return _session.Fail(new(PathIssueCode.TargetUnavailable, TargetId: TargetId));
            return null;
        }
        catch (Exception) { return _session.Fail(new(PathIssueCode.TargetUnavailable, TargetId: TargetId)); }
    }
}
