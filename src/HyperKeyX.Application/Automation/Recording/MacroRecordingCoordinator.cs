using System.Diagnostics;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

/// <summary>旧录制与轨迹录制共享入口，保留旧调用者的生命周期契约。</summary>
public sealed class MacroRecordingCoordinator : IMousePathRecordingController, IDisposable
{
    private readonly object _gate = new();
    private readonly IMacroRecordingService _legacy;
    private readonly MousePathRecordingService _paths;
    private readonly AutomationInputLease _inputLeases;
    private IDisposable? _lease;
    private long _generation;
    private bool? _activePath;
    private MacroRecordingResult? _lastResult;
    private bool _disposed;

    public MacroRecordingCoordinator(IMacroRecordingService legacy, MousePathRecordingService paths,
        AutomationInputLease? inputLeases = null)
    {
        _legacy = legacy; _paths = paths;
        _inputLeases = inputLeases ?? new();
        _legacy.StatusChanged += OnLegacyStatus; _legacy.Completed += OnLegacyCompleted;
        _paths.StatusChanged += OnPathStatus; _paths.Completed += OnPathCompleted;
    }

    public event EventHandler<MacroRecordingStatus>? StatusChanged;
    public event EventHandler<MacroRecordingResult>? Completed;
    public MacroRecordingStatus CurrentStatus
    {
        get
        {
            bool? active;
            lock (_gate) active = _activePath;
            return active is true ? _paths.CurrentStatus : active is false ? _legacy.CurrentStatus : new(MacroRecordingState.Idle, 0, 0);
        }
    }

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default) =>
        StartCoreAsync(false, () => _legacy.StartAsync(cancellationToken));
    public Task<OperationResult> StartPathAsync(MousePathRecordingRequest request, CancellationToken cancellationToken = default) =>
        StartCoreAsync(true, () => _paths.StartAsync(request, cancellationToken));

    private async Task<OperationResult> StartCoreAsync(bool path, Func<Task<OperationResult>> start)
    {
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activePath is not null) return OperationResult.Failure("录制会话已在运行。");
            var admission = _inputLeases.TryAcquireRecording();
            if (!admission.Succeeded) return OperationResult.Failure("自动化输入正被其他会话使用。");
            _lease = admission.Lease;
            generation = ++_generation;
            _activePath = path;
        }
        try
        {
            var result = await start().ConfigureAwait(false);
            if (!result.Succeeded)
                ReleaseFailedStart(generation);
            return result;
        }
        catch (Exception exception)
        {
            ReleaseFailedStart(generation);
            return OperationResult.Failure(exception.Message);
        }
    }

    private void ReleaseFailedStart(long generation)
    {
        lock (_gate)
        {
            // 完成通知可能已启动下一会话，旧的启动返回不能释放新会话的执行权。
            if (_generation != generation) return;
            _lease?.Dispose(); _lease = null; _activePath = null;
        }
    }

    public Task<MacroRecordingResult> StopAsync(MacroRecordingStopReason reason, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return _activePath is true ? _paths.StopAsync(reason, cancellationToken)
                : _activePath is false ? _legacy.StopAsync(reason, cancellationToken)
                : Task.FromResult(_lastResult ?? new(MacroRecordingStopReason.Cancelled, []));
        }
    }

    public Task<OperationResult> ChangePathContextAsync(MousePathRecordingRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return _activePath is true ? _paths.ChangeContextAsync(request, cancellationToken)
                : Task.FromResult(OperationResult.Failure("当前没有轨迹录制会话。"));
    }

    private void OnLegacyStatus(object? sender, MacroRecordingStatus status) => ForwardStatus(false, status);
    private void OnPathStatus(object? sender, MacroRecordingStatus status) => ForwardStatus(true, status);
    private void OnLegacyCompleted(object? sender, MacroRecordingResult result) => ForwardCompleted(false, result);
    private void OnPathCompleted(object? sender, MacroRecordingResult result) => ForwardCompleted(true, result);
    private void ForwardStatus(bool path, MacroRecordingStatus status)
    {
        lock (_gate) if (_activePath != path) return;
        Raise(StatusChanged, status);
    }
    private void ForwardCompleted(bool path, MacroRecordingResult result)
    {
        lock (_gate)
        {
            if (_activePath != path) return;
            _lastResult = result; _activePath = null;
            _lease?.Dispose(); _lease = null;
        }
        Raise(Completed, result);
        Raise(StatusChanged, CurrentStatus);
    }
    private void Raise<T>(EventHandler<T>? handlers, T value)
    {
        if (handlers is null) return;
        using var notification = RecordingNotificationContext.Enter();
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try { handler(this, value); }
            catch (Exception exception) { Debug.WriteLine($"录制协调通知失败：{exception}"); }
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        RecordingNotificationContext.WaitForDisposal(this, DisposeAfterStopAsync());
    }

    /// <summary>先等录制清理归还执行权，再退订通知和释放子服务；重入通知时延后完成。</summary>
    private async Task DisposeAfterStopAsync()
    {
        var failures = new List<Exception>();
        try { await StopAsync(MacroRecordingStopReason.Cancelled).ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        _legacy.StatusChanged -= OnLegacyStatus; _legacy.Completed -= OnLegacyCompleted;
        _paths.StatusChanged -= OnPathStatus; _paths.Completed -= OnPathCompleted;
        try { (_legacy as IDisposable)?.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        try { _paths.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count > 0) throw new AggregateException(failures);
    }
}
