using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

/// <summary>有界轨迹录制引擎；回调只维护控制状态和入队，处理及通知留在后台。</summary>
public sealed class MousePathRecordingService : IDisposable
{
    private readonly HyperKeyX.Domain.Preferences.AppShortcutSettings _shortcuts;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IGlobalHookService _keyboard;
    private readonly IGlobalMouseHookService _mouse;
    private readonly IMousePathRecordingEnvironment _environment;
    private readonly IMacroRecordingClock _clock;
    private readonly MacroRecordingOptions _options;
    private readonly HashSet<int> _keys = [];
    private readonly HashSet<MouseButtonKind> _buttons = [];
    private readonly List<long> _pendingModifiers = [];
    private readonly HashSet<long> _ignored = [];
    private readonly List<CapturedMacroEvent> _received = [];
    private readonly List<PathIssue> _captureIssues = [];
    private MacroRecordingStatus _status = new(MacroRecordingState.Idle, 0, 0);
    private CancellationTokenSource? _stop;
    private MousePathCaptureBuffer? _buffer;
    private IMousePathRecordingContextLease? _lease;
    private MousePathRecordingContext? _initialContext;
    private IDisposable? _movement;
    private Task _reader = Task.CompletedTask;
    private Task _watcher = Task.CompletedTask;
    private Task<MacroRecordingResult>? _completion;
    private MacroRecordingResult? _lastResult;
    private MacroRecordingStopReason? _queuedReason;
    private long _armedAt;
    private long _armedAtUs;
    private long _sequence;
    private int _pointCost;
    private int _stepCost;
    private bool _ownsKeyboard;
    private bool _ownsMouse;
    private bool _attached;
    private bool _disposed;
    private bool _changingContext;

    public MousePathRecordingService(IGlobalHookService keyboard, IGlobalMouseHookService mouse,
        IMousePathRecordingEnvironment environment, HyperKeyX.Domain.Preferences.AppShortcutSettings? shortcuts = null) : this(keyboard, mouse, environment,
            new StopwatchMacroRecordingClock(), new MacroRecordingOptions(), shortcuts) { }

    internal MousePathRecordingService(IGlobalHookService keyboard, IGlobalMouseHookService mouse,
        IMousePathRecordingEnvironment environment, IMacroRecordingClock clock, MacroRecordingOptions options,
        HyperKeyX.Domain.Preferences.AppShortcutSettings? shortcuts = null)
    {
        _shortcuts = HyperKeyX.Domain.Preferences.AppShortcutSettings.Normalize(shortcuts);
        _keyboard = keyboard; _mouse = mouse; _environment = environment; _clock = clock; _options = options;
    }

    public event EventHandler<MacroRecordingStatus>? StatusChanged;
    public event EventHandler<MacroRecordingResult>? Completed;
    public MacroRecordingStatus CurrentStatus { get { lock (_gate) return _status; } }

    public async Task<OperationResult> StartAsync(MousePathRecordingRequest request, CancellationToken cancellationToken = default)
    {
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stop is not null) return OperationResult.Failure("录制会话已在运行。");
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = _stop.Token;
            _completion = null; _lastResult = null; _queuedReason = null;
            _keys.Clear(); _buttons.Clear(); _received.Clear(); _ignored.Clear(); _pendingModifiers.Clear(); _captureIssues.Clear();
            _buffer = null; _lease = null; _initialContext = null; _sequence = 0; _pointCost = 1; _stepCost = 1;
            _changingContext = false;
            _reader = _watcher = Task.CompletedTask;
            _status = new(MacroRecordingState.Countdown, _options.CountdownSeconds, 0);
        }
        PublishStatus();
        try
        {
            request = request.DeepClone();
            if (_mouse is not IPointerCaptureSource source)
                throw new InvalidOperationException("鼠标钩子不支持轨迹采集。");
            await _lifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                _keyboard.AnyKeyStateChanged += OnKeyboard;
                _mouse.AnyMouseInput += OnMouse;
                _attached = true;
                _movement = source.SubscribeMovement(OnPointer);
                _ownsKeyboard = !_keyboard.IsRunning;
                if (_ownsKeyboard) _keyboard.Start();
                _ownsMouse = !_mouse.IsRunning;
                if (_ownsMouse) _mouse.Start();
            }
            finally { _lifecycle.Release(); }

            for (var remaining = _options.CountdownSeconds; remaining > 0; remaining--)
            {
                SetStatus(new(MacroRecordingState.Countdown, remaining, 0));
                await _clock.DelayAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool locallyReleased;
                lock (_gate) locallyReleased = _keys.Count == 0 && _buttons.Count == 0;
                if (_environment.AreAllInputsReleased() && locallyReleased)
                {
                    if (TryArm(request, token)) break;
                }
                SetStatus(new(MacroRecordingState.Countdown, 0, 0, WaitingForRelease: true));
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
            }
            PublishStatus();
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await StopAsync(MacroRecordingStopReason.Cancelled).ConfigureAwait(false);
            return OperationResult.Failure("宏录制已取消。");
        }
        catch (Exception exception)
        {
            await StopCoreAsync(MacroRecordingStopReason.StartupFailed, exception.Message).ConfigureAwait(false);
            return OperationResult.Failure(exception.Message);
        }
    }

    private bool TryArm(MousePathRecordingRequest request, CancellationToken token)
    {
        var lease = _environment.CaptureContext(request);
        var transferred = false;
        try
        {
            if (lease.Validate() is { } issue)
                throw new InvalidOperationException($"录制上下文无效：{issue.Code}");
            if (!MousePathRecordingAssembler.Assemble(lease.Context, [], 1, token).IsValid)
                throw new InvalidOperationException("轨迹录制参数或起始坐标无效。");
            lock (_gate)
            {
                if (token.IsCancellationRequested || _status.State != MacroRecordingState.Countdown ||
                    _keys.Count != 0 || _buttons.Count != 0) return false;
                _armedAt = _clock.GetTimestamp();
                _armedAtUs = _clock.GetElapsedTime(0, _armedAt).Ticks / 10;
                _buffer = new(_options.CaptureQueueCapacity);
                _reader = ReadEventsAsync(_buffer);
                _lease = lease;
                _initialContext = lease.Context.DeepClone();
                _status = new(MacroRecordingState.Recording, 0, 1);
                _watcher = Task.Run(() => WatchAsync(token), CancellationToken.None);
                transferred = true;
                return true;
            }
        }
        finally
        {
            // 准备途中取消或验证抛错时，尚未交给会话的窗口监视也必须释放。
            if (!transferred) lease.Dispose();
        }
    }

    public async Task<OperationResult> ChangeContextAsync(MousePathRecordingRequest request, CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        IMousePathRecordingContextLease? next = null;
        try
        {
            lock (_gate)
            {
                if (_status.State != MacroRecordingState.Recording || _queuedReason is not null)
                    return OperationResult.Failure("当前没有可切换上下文的轨迹录制。");
                if (_keys.Count > 0 || _buttons.Count > 0)
                {
                    RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.TargetSwitchWhileHeld));
                    return OperationResult.Failure("持有输入时不能切换录制上下文。");
                }
                _changingContext = true;
            }
            next = _environment.CaptureContext(request.DeepClone());
            if (next.Validate() is not null || !MousePathRecordingAssembler.Assemble(next.Context, [], 1, token).IsValid)
                throw new InvalidOperationException("新的录制上下文无效。");
            IMousePathRecordingContextLease? previous;
            lock (_gate)
            {
                if (_status.State != MacroRecordingState.Recording || _queuedReason is not null || token.IsCancellationRequested)
                    return OperationResult.Failure("录制上下文切换已中止。");
                if (!Capture(new(ElapsedUs(), CapturedMacroEventKind.ContextChanged,
                    next.Context.InitialPosition, Context: next.Context)))
                    return OperationResult.Failure("录制已达到捕获限制。");
                previous = _lease;
                _lease = next;
                next = null;
            }
            previous?.Dispose();
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            lock (_gate) RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.CaptureInterrupted));
            return OperationResult.Failure(exception.Message);
        }
        finally
        {
            try { next?.Dispose(); }
            finally
            {
                lock (_gate) _changingContext = false;
                _lifecycle.Release();
            }
        }
    }

    public Task<MacroRecordingResult> StopAsync(MacroRecordingStopReason reason, CancellationToken cancellationToken = default)
    {
        var task = StopCoreAsync(reason, null);
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private Task<MacroRecordingResult> StopCoreAsync(MacroRecordingStopReason reason, string? error)
    {
        lock (_gate)
        {
            if (_completion is not null) return _completion;
            if (_stop is null) return Task.FromResult(_lastResult ?? new(MacroRecordingStopReason.Cancelled, []));
            reason = _queuedReason ?? reason;
            if (_status.State == MacroRecordingState.Countdown && reason != MacroRecordingStopReason.StartupFailed)
                reason = MacroRecordingStopReason.Cancelled;
            _status = _status with { State = MacroRecordingState.Stopping, WaitingForRelease = false };
            _buffer?.Close(Math.Min(ElapsedUs(), _options.MaxPathDurationUs));
            _stop.Cancel();
            _completion = CompleteAsync(reason, error);
            return _completion;
        }
    }

    private async Task<MacroRecordingResult> CompleteAsync(MacroRecordingStopReason reason, string? error)
    {
        await Task.Yield();
        PublishStatus();
        var errors = new List<string>();
        if (error is not null) errors.Add(error);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_attached)
            {
                _keyboard.AnyKeyStateChanged -= OnKeyboard;
                _mouse.AnyMouseInput -= OnMouse;
                _attached = false;
            }
            TryCleanup(() => _movement?.Dispose(), errors);
            _movement = null;
            if (_ownsMouse) TryCleanup(_mouse.Stop, errors);
            if (_ownsKeyboard) TryCleanup(_keyboard.Stop, errors);
            _ownsKeyboard = _ownsMouse = false;
        }
        finally { _lifecycle.Release(); }
        await _reader.ConfigureAwait(false);
        await _watcher.ConfigureAwait(false);
        MousePathCaptureResult? capture = null;
        if (reason is not (MacroRecordingStopReason.Cancelled or MacroRecordingStopReason.StartupFailed) && _initialContext is not null && _buffer is not null)
        {
            var cutoff = _buffer.Close(ElapsedUs());
            capture = MousePathRecordingAssembler.Assemble(_initialContext,
                _received.Where(p => !_ignored.Contains(p.Sequence)), cutoff.TimestampUs, CancellationToken.None,
                _options.MaxPathPoints, _options.MaxSteps, _options.MaxPathDurationUs);
        }
        TryCleanup(() => _lease?.Dispose(), errors);
        _lease = null;
        var issues = _captureIssues.Concat(capture?.Issues ?? []).ToArray();
        // 停止时补齐末尾停留也可能耗尽预算，结果必须报告实际截断原因。
        if (reason is MacroRecordingStopReason.UserRequested or MacroRecordingStopReason.Shortcut)
        {
            if (issues.Any(i => i.Code == PathIssueCode.TooManyPoints)) reason = MacroRecordingStopReason.PointLimitReached;
            else if (issues.Any(i => i.Code == PathIssueCode.CaptureStepLimitExceeded)) reason = MacroRecordingStopReason.StepLimitReached;
            else if (issues.Any(i => i.Code == PathIssueCode.CaptureDurationLimitExceeded)) reason = MacroRecordingStopReason.DurationLimitReached;
        }
        if (issues.Length > 0) errors.Add("轨迹录制因约束或上下文问题停止，请检查草稿中的诊断。");
        var macro = capture?.Macro.DeepClone();
        var result = new MacroRecordingResult(reason, Array.AsReadOnly((macro?.Steps ?? []).Select(s => s.DeepClone()).ToArray()),
            errors.Count == 0 ? null : string.Join(" | ", errors), macro, Array.AsReadOnly(issues), capture?.SequenceIssues);
        lock (_gate)
        {
            _lastResult = result;
            _stop?.Dispose(); _stop = null;
            _status = new(MacroRecordingState.Idle, 0, 0);
        }
        Raise(Completed, result);
        PublishStatus();
        return result;
    }

    private async Task ReadEventsAsync(MousePathCaptureBuffer buffer)
    {
        await foreach (var input in buffer.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            _received.Add(input);
    }

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(25), token).ConfigureAwait(false);
                PathIssue? issue;
                await _lifecycle.WaitAsync(token).ConfigureAwait(false);
                try { issue = _lease?.Validate(); }
                finally { _lifecycle.Release(); }
                if (issue is not null)
                {
                    lock (_gate) RequestStop(MacroRecordingStopReason.ContextInterrupted, issue);
                    return;
                }
                lock (_gate)
                {
                    if (ElapsedUs() >= _options.MaxPathDurationUs)
                    {
                        RequestStop(MacroRecordingStopReason.DurationLimitReached, new(PathIssueCode.CaptureDurationLimitExceeded));
                        return;
                    }
                    if (_status.State != MacroRecordingState.Recording) return;
                    _status = _status with { CapturedPointCount = _pointCost, CapturedStepCount = _stepCost };
                }
                PublishStatus();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_gate)
                if (_status.State == MacroRecordingState.Recording) RequestStop(MacroRecordingStopReason.Cancelled, null);
        }
        catch (Exception)
        {
            lock (_gate) RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.CaptureInterrupted));
        }
    }

    private void OnKeyboard(object? sender, KeyboardInputEventArgs args)
    {
        lock (_gate)
        {
            var input = args.Input;
            if (input.IsDown) _keys.Add(input.Key.StorageKey); else _keys.Remove(input.Key.StorageKey);
            if (_status.State != MacroRecordingState.Recording || _queuedReason is not null || input.IsRepeat) return;
            if (_changingContext)
            {
                RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.CaptureInterrupted));
                return;
            }
            var timestampUs = CaptureTime(input.TimestampUs);
            if (timestampUs < 0) return;
            if (input.IsDown && _shortcuts.FinishRecording.Matches(input.Key.VirtualKeyCode,
                _keys.Select(key => KeyboardKeyIdentity.FromStorageKey(key).VirtualKeyCode)))
            {
                _ignored.UnionWith(_pendingModifiers);
                _pendingModifiers.Clear();
                RequestStop(MacroRecordingStopReason.Shortcut, null);
                return;
            }
            var bufferedModifier = input.IsDown && input.Key.VirtualKeyCode is 0x11 or 0xA2 or 0xA3 or 0x10 or 0xA0 or 0xA1 or 0x12 or 0xA4 or 0xA5 or 0x5B or 0x5C;
            if (!bufferedModifier) _pendingModifiers.Clear();
            if (Capture(new(timestampUs, input.IsDown ? CapturedMacroEventKind.KeyDown : CapturedMacroEventKind.KeyUp,
                Key: input.Key, SourceSequence: input.Sequence)) && bufferedModifier)
                _pendingModifiers.Add(_sequence);
        }
    }

    private void OnMouse(object? sender, MouseInputEventArgs args) => OnPointer(args.Input);
    private void OnPointer(MouseInputEvent input)
    {
        lock (_gate)
        {
            if (input.Kind == MouseInputKind.ButtonDown) _buttons.Add(input.Button);
            if (input.Kind == MouseInputKind.ButtonUp) _buttons.Remove(input.Button);
            if (_status.State != MacroRecordingState.Recording || _queuedReason is not null) return;
            if (_changingContext)
            {
                RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.CaptureInterrupted));
                return;
            }
            var timestampUs = CaptureTime(input.TimestampUs);
            if (timestampUs < 0) return;
            RecordingInputDisposition disposition;
            try { disposition = _environment.Classify(input); }
            catch (Exception)
            {
                RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.CaptureInterrupted));
                return;
            }
            if (disposition != RecordingInputDisposition.Record)
            {
                RequestStop(disposition == RecordingInputDisposition.StopControl ? MacroRecordingStopReason.UserRequested : MacroRecordingStopReason.ContextInterrupted,
                    disposition == RecordingInputDisposition.StopControl ? null : new(PathIssueCode.CaptureInterrupted));
                return;
            }
            if (input.Position is null)
            {
                RequestStop(MacroRecordingStopReason.ContextInterrupted, new(PathIssueCode.InvalidCoordinate));
                return;
            }
            var kind = input.Kind switch
            {
                MouseInputKind.Move => CapturedMacroEventKind.Move,
                MouseInputKind.ButtonDown => CapturedMacroEventKind.ButtonDown,
                MouseInputKind.ButtonUp => CapturedMacroEventKind.ButtonUp,
                MouseInputKind.Wheel => CapturedMacroEventKind.Wheel,
                _ => (CapturedMacroEventKind)(-1)
            };
            if (!Enum.IsDefined(kind)) return;
            if (kind != CapturedMacroEventKind.Move) _pendingModifiers.Clear();
            Capture(new(timestampUs, kind, input.Position, Button: input.Button, WheelDelta: input.WheelDelta,
                SourceSequence: input.Sequence));
        }
    }

    private bool Capture(CapturedMacroEvent input)
    {
        var pointCost = input.Kind == CapturedMacroEventKind.Move ? 1 : 2;
        var stepCost = input.Kind == CapturedMacroEventKind.Move ? 0 : input.Kind == CapturedMacroEventKind.ContextChanged ? 1 : 2;
        if (input.TimestampUs > _options.MaxPathDurationUs)
        {
            RequestStop(MacroRecordingStopReason.DurationLimitReached, new(PathIssueCode.CaptureDurationLimitExceeded)); return false;
        }
        if (_pointCost + pointCost > _options.MaxPathPoints)
        {
            RequestStop(MacroRecordingStopReason.PointLimitReached, new(PathIssueCode.TooManyPoints)); return false;
        }
        if (_stepCost + stepCost > _options.MaxSteps)
        {
            RequestStop(MacroRecordingStopReason.StepLimitReached, new(PathIssueCode.CaptureStepLimitExceeded)); return false;
        }
        if (_buffer is null || !_buffer.TryAppend(input))
        {
            RequestStop(MacroRecordingStopReason.QueueOverflow, new(PathIssueCode.CaptureQueueOverflow)); return false;
        }
        _sequence++; _pointCost += pointCost; _stepCost += stepCost;
        return true;
    }

    private void RequestStop(MacroRecordingStopReason reason, PathIssue? issue)
    {
        if (_queuedReason is not null || _status.State != MacroRecordingState.Recording) return;
        _queuedReason = reason;
        if (issue is not null) _captureIssues.Add(issue);
        _buffer?.Close(Math.Min(ElapsedUs(), _options.MaxPathDurationUs));
        _ = Task.Run(async () => await StopAsync(reason).ConfigureAwait(false));
    }

    private long ElapsedUs() => Math.Max(0, _clock.GetElapsedTime(_armedAt, _clock.GetTimestamp()).Ticks / 10);
    private long CaptureTime(long timestampUs) => timestampUs > 0 ? timestampUs - _armedAtUs : ElapsedUs();
    private void SetStatus(MacroRecordingStatus status)
    {
        lock (_gate)
        {
            if (_status.State == MacroRecordingState.Stopping || _stop is null) return;
            _status = status;
        }
        PublishStatus();
    }
    private void PublishStatus() => Raise(StatusChanged, CurrentStatus);
    private void Raise<T>(EventHandler<T>? handlers, T value)
    {
        if (handlers is null) return;
        using var notification = RecordingNotificationContext.Enter();
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try { handler(this, value); }
            catch (Exception exception) { Debug.WriteLine($"轨迹录制通知失败：{exception}"); }
        }
    }
    private static void TryCleanup(Action action, List<string> errors)
    {
        try { action(); }
        catch (Exception exception) { errors.Add(exception.Message); }
    }
    public void Dispose()
    {
        bool needsStop;
        // 完成通知可重入；先关闭入口，避免销毁期间启动新的录制。
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            needsStop = _stop is not null;
        }
        // _stop 在资源清理后置空；停止状态回调中的释放则由原停止任务继续完成。
        Task stopping = needsStop ? StopAsync(MacroRecordingStopReason.Cancelled) : Task.CompletedTask;
        RecordingNotificationContext.WaitForDisposal(this, DisposeAfterStopAsync(stopping));
    }

    /// <summary>后台停止及通知结束后才释放生命周期锁，避免打断仍在进行的清理。</summary>
    private async Task DisposeAfterStopAsync(Task stopping)
    {
        try { await stopping.ConfigureAwait(false); }
        finally { _lifecycle.Dispose(); }
    }
}
