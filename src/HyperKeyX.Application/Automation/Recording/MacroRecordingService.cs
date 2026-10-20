using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Automation.Recording;

/// <summary>
/// 协调键盘与鼠标钩子的录制会话生命周期。
/// </summary>
public sealed class MacroRecordingService : IMacroRecordingService, IDisposable
{
    private readonly HyperKeyX.Domain.Preferences.AppShortcutSettings _shortcuts;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _hookLifecycle = new(1, 1);
    private readonly IGlobalHookService _keyboardHookService;
    private readonly IGlobalMouseHookService _mouseHookService;
    private readonly IMacroRecordingClock _clock;
    private readonly MacroRecordingOptions _options;
    private readonly List<CapturedInput> _capturedInputs = new();
    private readonly List<CapturedInput> _modifierBuffer = new();
    private readonly HashSet<int> _physicalKeysDown = new();

    private MacroRecordingStatus _currentStatus = new(
        MacroRecordingState.Idle,
        CountdownSeconds: 0,
        CapturedStepCount: 0);
    private CancellationTokenSource? _sessionStop;
    private Task<MacroRecordingResult>? _stopTask;
    private MacroRecordingResult? _lastResult;
    private bool _ownsKeyboardHook;
    private bool _ownsMouseHook;
    private bool _keyboardHandlerAttached;
    private bool _mouseHandlerAttached;
    private long _nextSequence;
    private bool _stopQueued;
    private bool _disposed;

    public MacroRecordingService(
        IGlobalHookService keyboardHookService,
        IGlobalMouseHookService mouseHookService,
        HyperKeyX.Domain.Preferences.AppShortcutSettings? shortcuts = null)
        : this(
            keyboardHookService,
            mouseHookService,
            new StopwatchMacroRecordingClock(),
            new MacroRecordingOptions(), shortcuts)
    {
    }

    internal MacroRecordingService(
        IGlobalHookService keyboardHookService,
        IGlobalMouseHookService mouseHookService,
        IMacroRecordingClock clock,
        MacroRecordingOptions options, HyperKeyX.Domain.Preferences.AppShortcutSettings? shortcuts = null)
    {
        _shortcuts = HyperKeyX.Domain.Preferences.AppShortcutSettings.Normalize(shortcuts);
        _keyboardHookService = keyboardHookService;
        _mouseHookService = mouseHookService;
        _clock = clock;
        _options = options with
        {
            CountdownSeconds = Math.Clamp(options.CountdownSeconds, 0, 60),
            MaxSteps = Math.Clamp(options.MaxSteps, 1, 512)
        };
    }

    public event EventHandler<MacroRecordingStatus>? StatusChanged;
    public event EventHandler<MacroRecordingResult>? Completed;

    public MacroRecordingStatus CurrentStatus
    {
        get
        {
            lock (_sync)
                return _currentStatus;
        }
    }

    public async Task<OperationResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource sessionStop;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_sessionStop is not null ||
                _currentStatus.State != MacroRecordingState.Idle)
            {
                return OperationResult.Failure("宏录制会话已经处于活动状态。");
            }

            sessionStop = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _sessionStop = sessionStop;
            _stopTask = null;
            _lastResult = null;
            _ownsKeyboardHook = false;
            _ownsMouseHook = false;
            _capturedInputs.Clear();
            _modifierBuffer.Clear();
            _physicalKeysDown.Clear();
            _nextSequence = 0;
            _stopQueued = false;
            _currentStatus = new MacroRecordingStatus(
                MacroRecordingState.Countdown,
                _options.CountdownSeconds,
                CapturedStepCount: 0);
        }

        try
        {
            await StartHooksAsync().ConfigureAwait(false);

            for (var remaining = _options.CountdownSeconds; remaining > 0; remaining--)
            {
                sessionStop.Token.ThrowIfCancellationRequested();
                if (!TryPublishActiveStatus(
                        sessionStop,
                        new MacroRecordingStatus(
                            MacroRecordingState.Countdown,
                            remaining,
                            CapturedStepCount: 0)))
                {
                    throw new OperationCanceledException(sessionStop.Token);
                }

                await _clock.DelayAsync(
                    TimeSpan.FromSeconds(1),
                    sessionStop.Token).ConfigureAwait(false);
            }

            sessionStop.Token.ThrowIfCancellationRequested();
            if (!TryPublishActiveStatus(
                    sessionStop,
                    new MacroRecordingStatus(
                        MacroRecordingState.Recording,
                        CountdownSeconds: 0,
                        CapturedStepCount: 0)))
            {
                throw new OperationCanceledException(sessionStop.Token);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested)
        {
            // 释放可能已经关闭公开入口；启动任务仍需等待本会话的内部取消清理。
            await BeginCompletion(
                MacroRecordingStopReason.Cancelled,
                    errorMessage: null,
                    preserveReason: false).ConfigureAwait(false);
            return OperationResult.Failure("宏录制已取消。");
        }
        catch (Exception ex)
        {
            var completion = BeginCompletion(
                MacroRecordingStopReason.StartupFailed,
                ex.Message,
                preserveReason: true);
            await completion.ConfigureAwait(false);
            return OperationResult.Failure(ex.Message);
        }
    }

    public Task<MacroRecordingResult> StopAsync(
        MacroRecordingStopReason reason,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
            ThrowIfDisposed();

        var completion = BeginCompletion(
            reason,
            errorMessage: null,
            preserveReason: false);
        return cancellationToken.CanBeCanceled
            ? completion.WaitAsync(cancellationToken)
            : completion;
    }

    public void Dispose()
    {
        bool needsStop;
        lock (_sync)
        {
            if (_disposed)
                return;
            // 先拒绝重入启动，完成回调中也不能创建新的录制会话。
            _disposed = true;
            needsStop = _sessionStop is not null;
        }

        Task stopping = needsStop
            ? BeginCompletion(MacroRecordingStopReason.Cancelled, errorMessage: null, preserveReason: false)
            : Task.CompletedTask;
        RecordingNotificationContext.WaitForDisposal(this, DisposeAfterStopAsync(stopping));
    }

    /// <summary>保留通知外同步清理的契约，重入通知时由原停止任务负责结束释放。</summary>
    private async Task DisposeAfterStopAsync(Task stopping)
    {
        try
        {
            await stopping.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _sessionStop?.Dispose();
                _sessionStop = null;
            }

            _hookLifecycle.Dispose();
        }
    }

    private async Task StartHooksAsync()
    {
        await _hookLifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            AttachHookHandlers();

            if (!_keyboardHookService.IsRunning)
            {
                _keyboardHookService.Start();
                lock (_sync)
                    _ownsKeyboardHook = true;
            }

            if (!_mouseHookService.IsRunning)
            {
                _mouseHookService.Start();
                lock (_sync)
                    _ownsMouseHook = true;
            }
        }
        finally
        {
            _hookLifecycle.Release();
        }
    }

    private Task<MacroRecordingResult> BeginCompletion(
        MacroRecordingStopReason reason,
        string? errorMessage,
        bool preserveReason)
    {
        MacroRecordingStatus? stoppingStatus = null;
        Task<MacroRecordingResult> completion;

        lock (_sync)
        {
            if (_stopTask is not null)
                return _stopTask;

            if (_sessionStop is null &&
                _currentStatus.State == MacroRecordingState.Idle)
            {
                return Task.FromResult(
                    _lastResult ?? CreateEmptyResult(
                        MacroRecordingStopReason.Cancelled,
                        errorMessage: null));
            }

            var actualReason = !preserveReason &&
                               _currentStatus.State == MacroRecordingState.Countdown
                ? MacroRecordingStopReason.Cancelled
                : reason;
            _sessionStop?.Cancel();
            stoppingStatus = new MacroRecordingStatus(
                MacroRecordingState.Stopping,
                CountdownSeconds: 0,
                _currentStatus.CapturedStepCount);
            _currentStatus = stoppingStatus;
            completion = CompleteSessionAsync(actualReason, errorMessage);
            _stopTask = completion;
        }

        RaiseStatusChanged(stoppingStatus);
        return completion;
    }

    private async Task<MacroRecordingResult> CompleteSessionAsync(
        MacroRecordingStopReason reason,
        string? errorMessage)
    {
        await Task.Yield();
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(errorMessage))
            errors.Add(errorMessage);

        await _hookLifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            DetachHookHandlers();

            bool stopMouse;
            bool stopKeyboard;
            lock (_sync)
            {
                stopMouse = _ownsMouseHook;
                stopKeyboard = _ownsKeyboardHook;
                _ownsMouseHook = false;
                _ownsKeyboardHook = false;
            }

            if (stopMouse)
                TryStopHook(_mouseHookService.Stop, "停止鼠标钩子失败", errors);
            if (stopKeyboard)
                TryStopHook(_keyboardHookService.Stop, "停止键盘钩子失败", errors);
        }
        finally
        {
            _hookLifecycle.Release();
        }

        var result = new MacroRecordingResult(
            reason,
            CreateStepSnapshot(reason),
            errors.Count == 0 ? null : string.Join(" | ", errors));
        MacroRecordingStatus idleStatus;
        lock (_sync)
        {
            _sessionStop?.Dispose();
            _sessionStop = null;
            _lastResult = result;
            idleStatus = new MacroRecordingStatus(
                MacroRecordingState.Idle,
                CountdownSeconds: 0,
                CapturedStepCount: 0);
            _currentStatus = idleStatus;
        }

        RaiseCompleted(result);
        RaiseStatusChanged(idleStatus);
        return result;
    }

    private bool TryPublishActiveStatus(
        CancellationTokenSource sessionStop,
        MacroRecordingStatus status)
    {
        lock (_sync)
        {
            if (_sessionStop != sessionStop ||
                _currentStatus.State == MacroRecordingState.Stopping)
            {
                return false;
            }

            _currentStatus = status;
        }

        RaiseStatusChanged(status);
        return true;
    }

    private void AttachHookHandlers()
    {
        if (!_keyboardHandlerAttached)
        {
            _keyboardHookService.AnyKeyStateChanged += OnKeyboardInput;
            _keyboardHandlerAttached = true;
        }

        if (!_mouseHandlerAttached)
        {
            _mouseHookService.AnyMouseInput += OnMouseInput;
            _mouseHandlerAttached = true;
        }
    }

    private void DetachHookHandlers()
    {
        if (_mouseHandlerAttached)
        {
            _mouseHookService.AnyMouseInput -= OnMouseInput;
            _mouseHandlerAttached = false;
        }

        if (_keyboardHandlerAttached)
        {
            _keyboardHookService.AnyKeyStateChanged -= OnKeyboardInput;
            _keyboardHandlerAttached = false;
        }
    }

    private void OnKeyboardInput(object? sender, KeyboardInputEventArgs args)
    {
        var timestamp = _clock.GetTimestamp();
        MacroRecordingStatus? status = null;
        MacroRecordingStopReason? stopReason = null;

        lock (_sync)
        {
            if (_currentStatus.State != MacroRecordingState.Recording || _stopQueued)
                return;

            var sequence = _nextSequence++;
            var input = args.Input;
            if (input.IsRepeat)
                return;

            if (input.IsDown)
                _physicalKeysDown.Add(input.Key.VirtualKeyCode);
            else
                _physicalKeysDown.Remove(input.Key.VirtualKeyCode);

            if (input.IsDown &&
                _shortcuts.FinishRecording.Matches(input.Key.VirtualKeyCode, _physicalKeysDown))
            {
                // 停止组合仍进入前台程序，但其尾部修饰键和配置的完成主键不写入宏。
                _modifierBuffer.Clear();
                _stopQueued = true;
                stopReason = MacroRecordingStopReason.Shortcut;
            }
            else
            {
                var captured = new List<CapturedInput>(_capturedInputs);
                var modifiers = new List<CapturedInput>(_modifierBuffer);
                var candidate = new CapturedInput(
                    timestamp,
                    sequence,
                    CreateKeyboardStep(input));

                if (input.IsDown && IsModifier(input.Key.VirtualKeyCode))
                {
                    modifiers.Add(candidate);
                }
                else
                {
                    FlushModifiers(captured, modifiers);
                    captured.Add(candidate);
                }

                if (!TryCommitCandidate(captured, modifiers, out status))
                {
                    _stopQueued = true;
                    stopReason = MacroRecordingStopReason.StepLimitReached;
                    status = null;
                }
            }
        }

        if (status is not null)
            RaiseStatusChanged(status);
        if (stopReason is not null)
            QueueStop(stopReason.Value);
    }

    private void OnMouseInput(object? sender, MouseInputEventArgs args)
    {
        var timestamp = _clock.GetTimestamp();
        MacroRecordingStatus? status = null;
        var shouldStop = false;

        lock (_sync)
        {
            if (_currentStatus.State != MacroRecordingState.Recording || _stopQueued)
                return;

            var sequence = _nextSequence++;
            if (args.Input.TargetProcessId == Environment.ProcessId)
                return;

            var step = CreateMouseStep(args.Input);
            if (step is null)
                return;

            var captured = new List<CapturedInput>(_capturedInputs);
            var modifiers = new List<CapturedInput>(_modifierBuffer);
            FlushModifiers(captured, modifiers);
            captured.Add(new CapturedInput(timestamp, sequence, step));

            if (!TryCommitCandidate(captured, modifiers, out status))
            {
                _stopQueued = true;
                shouldStop = true;
                status = null;
            }
        }

        if (status is not null)
            RaiseStatusChanged(status);
        if (shouldStop)
            QueueStop(MacroRecordingStopReason.StepLimitReached);
    }

    private IReadOnlyList<MacroStep> CreateStepSnapshot(
        MacroRecordingStopReason reason)
    {
        lock (_sync)
        {
            IReadOnlyList<MacroStep> snapshot;
            if (reason is MacroRecordingStopReason.Cancelled or
                MacroRecordingStopReason.StartupFailed)
            {
                snapshot = Array.AsReadOnly(Array.Empty<MacroStep>());
            }
            else
            {
                if (reason == MacroRecordingStopReason.Shortcut)
                    _modifierBuffer.Clear();
                else
                    FlushModifiers(_capturedInputs, _modifierBuffer);

                snapshot = Array.AsReadOnly(
                    CreateNormalizedSteps(_capturedInputs).ToArray());
            }

            _capturedInputs.Clear();
            _modifierBuffer.Clear();
            _physicalKeysDown.Clear();
            _stopQueued = false;
            return snapshot;
        }
    }

    private bool TryCommitCandidate(
        List<CapturedInput> captured,
        List<CapturedInput> modifiers,
        out MacroRecordingStatus status)
    {
        var stepCount = CreateNormalizedSteps(
            captured.Concat(modifiers)).Count;
        if (stepCount > _options.MaxSteps)
        {
            status = _currentStatus;
            return false;
        }

        _capturedInputs.Clear();
        _capturedInputs.AddRange(captured);
        _modifierBuffer.Clear();
        _modifierBuffer.AddRange(modifiers);
        status = new MacroRecordingStatus(
            MacroRecordingState.Recording,
            CountdownSeconds: 0,
            CapturedStepCount: stepCount);
        _currentStatus = status;
        return true;
    }

    private List<MacroStep> CreateNormalizedSteps(
        IEnumerable<CapturedInput> source)
    {
        var ordered = source
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Sequence)
            .ToArray();
        var steps = new List<MacroStep>();
        CapturedInput? previous = null;

        foreach (var item in ordered)
        {
            if (previous is not null)
            {
                var elapsed = _clock.GetElapsedTime(
                    previous.Value.Timestamp,
                    item.Timestamp);
                var delay = (int)Math.Clamp(
                    (long)elapsed.TotalMilliseconds,
                    0L,
                    60_000L);
                if (delay > 0)
                {
                    steps.Add(new MacroStep
                    {
                        ActionType = MacroActionType.Delay,
                        DurationMs = delay
                    });
                }
            }

            steps.Add(CloneStep(item.Step));
            previous = item;
        }

        return steps;
    }

    private static MacroStep CreateKeyboardStep(KeyboardInputEvent input)
    {
        return new MacroStep
        {
            ActionType = input.IsDown
                ? MacroActionType.KeyDown
                : MacroActionType.KeyUp,
            VirtualKeyCode = input.Key.VirtualKeyCode,
            ScanCode = input.Key.ScanCode,
            IsExtendedKey = input.Key.IsExtendedKey
        };
    }

    private static MacroStep? CreateMouseStep(MouseInputEvent input)
    {
        return input.Kind switch
        {
            MouseInputKind.ButtonDown when IsValidMouseButton(input.Button) =>
                new MacroStep
                {
                    ActionType = MacroActionType.MouseButtonDown,
                    MouseButton = input.Button
                },
            MouseInputKind.ButtonUp when IsValidMouseButton(input.Button) =>
                new MacroStep
                {
                    ActionType = MacroActionType.MouseButtonUp,
                    MouseButton = input.Button
                },
            MouseInputKind.Wheel when input.WheelDelta != 0 =>
                new MacroStep
                {
                    ActionType = MacroActionType.MouseWheel,
                    WheelDelta = Math.Clamp(input.WheelDelta, -1200, 1200)
                },
            _ => null
        };
    }

    private static MacroStep CloneStep(MacroStep step)
    {
        return step.DeepClone();
    }

    private static void FlushModifiers(
        ICollection<CapturedInput> captured,
        ICollection<CapturedInput> modifiers)
    {
        foreach (var modifier in modifiers)
            captured.Add(modifier);
        modifiers.Clear();
    }

    private void QueueStop(MacroRecordingStopReason reason) =>
        _ = CompleteQueuedStopAsync(reason);

    private async Task CompleteQueuedStopAsync(MacroRecordingStopReason reason)
    {
        try
        {
            await StopAsync(reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MacroRecordingService] 自动停止录制失败: {ex}");
        }
    }

    private static bool IsModifier(int virtualKeyCode) =>
        IsControl(virtualKeyCode) || IsShift(virtualKeyCode) || virtualKeyCode is 0x12 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private static bool IsControl(int virtualKeyCode) =>
        virtualKeyCode is 0x11 or 0xA2 or 0xA3;

    private static bool IsShift(int virtualKeyCode) =>
        virtualKeyCode is 0x10 or 0xA0 or 0xA1;

    private static bool IsAnyControlDown(IReadOnlySet<int> keys) =>
        keys.Any(IsControl);

    private static bool IsAnyShiftDown(IReadOnlySet<int> keys) =>
        keys.Any(IsShift);

    private static bool IsValidMouseButton(MouseButtonKind button) =>
        button is >= MouseButtonKind.Left and <= MouseButtonKind.XButton2;

    private static MacroRecordingResult CreateEmptyResult(
        MacroRecordingStopReason reason,
        string? errorMessage)
    {
        return new MacroRecordingResult(
            reason,
            Array.AsReadOnly(Array.Empty<MacroStep>()),
            errorMessage);
    }

    private static void TryStopHook(
        Action stop,
        string message,
        ICollection<string> errors)
    {
        try
        {
            stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MacroRecordingService] {message}: {ex}");
            errors.Add($"{message}: {ex.Message}");
        }
    }

    private void RaiseStatusChanged(MacroRecordingStatus status) =>
        InvokeSubscribers(StatusChanged, status, "状态订阅者异常");

    private void RaiseCompleted(MacroRecordingResult result) =>
        InvokeSubscribers(Completed, result, "完成订阅者异常");

    private void InvokeSubscribers<T>(
        EventHandler<T>? handlers,
        T value,
        string message)
    {
        if (handlers is null)
            return;

        using var notification = RecordingNotificationContext.Enter();
        foreach (var subscriber in handlers
                     .GetInvocationList()
                     .Cast<EventHandler<T>>())
        {
            try
            {
                subscriber(this, value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacroRecordingService] {message}: {ex}");
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MacroRecordingService));
    }

    private readonly record struct CapturedInput(
        long Timestamp,
        long Sequence,
        MacroStep Step);
}
