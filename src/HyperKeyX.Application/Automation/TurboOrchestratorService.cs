using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Processes;
using HyperKeyX.Application.Automation.Paths;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 协调按住、开关、长按三种连发模式以及可取消的宏回放。
/// </summary>
public sealed class TurboOrchestratorService : ITurboOrchestratorService, IDisposable
{
    private readonly HyperKeyX.Domain.Preferences.AppShortcutSettings _shortcuts;

    private readonly object _sync = new();
    private readonly object _lifecycleSync = new();
    private readonly Dictionary<RuntimeTriggerKey, bool> _toggleStates = new();
    private readonly HashSet<InputTriggerIdentity> _physicalInputsDown = new();
    // 按下时固定拦截决定及触发归属；停止后只排空已有按键，不创建新动作。
    private readonly Dictionary<int, KeyboardPressSession> _keyboardPressSessions = new();
    private IDisposable? _keyboardDrainLease;
    private Task? _keyboardHookLifetime;
    private CancellationTokenSource? _keyboardDrainObservation;
    private readonly HashSet<RuntimeTriggerKey> _momentaryTurboTriggers = new();
    private readonly HashSet<RuntimeTriggerKey> _momentaryMacroTriggers = new();
    private readonly Dictionary<RuntimeTriggerKey, CancellationTokenSource> _turboStops = new();
    private readonly Dictionary<RuntimeTriggerKey, Task> _turboTasks = new();
    private readonly Dictionary<RuntimeTriggerKey, CancellationTokenSource> _longPressStops = new();
    private readonly Dictionary<RuntimeTriggerKey, Task> _longPressTasks = new();
    private readonly Dictionary<RuntimeTriggerKey, CancellationTokenSource> _macroStops = new();
    private readonly Dictionary<RuntimeTriggerKey, Task> _macroTasks = new();
    private readonly Dictionary<MouseButtonKind, MouseTriggerSession> _mousePressSessions = new();

    private readonly IWin32InputService _inputService;
    private readonly IMouseInputService _mouseInputService;
    private readonly IGlobalHookService _hookService;
    private readonly IGlobalMouseHookService _mouseHookService;
    private readonly IProcessNameResolver _processNameResolver;
    private readonly IRuntimeProfileCatalog _profileCatalog;
    private readonly AutomationInputLease _inputLeases;
    private readonly MousePathPlaybackService? _pathPlayback;
    private readonly HashSet<RuntimeTriggerKey> _windowPathTriggers = [];
    private bool _hasDeferredProfile;
    private AppProfile? _deferredProfile;
    private EventHandler? _emergencyStopRequested;

    private AppProfile? _activeProfile;
    private CancellationTokenSource? _runStop;
    private bool _isRunning;
    private bool _ownsKeyboardHook;
    private bool _ownsMouseHook;
    private bool _keyboardHandlerAttached;
    private bool _mouseHandlerAttached;
    private bool _disposed;

    public event EventHandler<string>? RuntimeError;
    public event EventHandler? EmergencyStopRequested
    {
        add => _emergencyStopRequested += value;
        remove => _emergencyStopRequested -= value;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _isRunning;
        }
    }

    public TurboOrchestratorService(
        IWin32InputService inputService,
        IMouseInputService mouseInputService,
        IGlobalHookService hookService,
        IGlobalMouseHookService mouseHookService,
        IProcessNameResolver processNameResolver,
        IRuntimeProfileCatalog profileCatalog,
        AutomationInputLease? inputLeases = null,
        MousePathPlaybackService? pathPlayback = null,
        HyperKeyX.Domain.Preferences.AppShortcutSettings? shortcuts = null)
    {
        _shortcuts = HyperKeyX.Domain.Preferences.AppShortcutSettings.Normalize(shortcuts);
        _inputService = inputService;
        _mouseInputService = mouseInputService;
        _hookService = hookService;
        _mouseHookService = mouseHookService;
        _processNameResolver = processNameResolver;
        _profileCatalog = profileCatalog;
        _inputLeases = inputLeases ?? new();
        _pathPlayback = pathPlayback;
        _profileCatalog.ProfilesReplaced += OnProfilesReplaced;
    }

    public void SetActiveProfile(AppProfile? profile)
    {
        var profileSnapshot = profile?.DeepClone();
        var nextProfileId = profileSnapshot?.ProfileId;
        Task[] pendingTasks;

        lock (_sync)
        {
            var previousProfileId = _activeProfile?.ProfileId;

            if (_windowPathTriggers.Count > 0)
            {
                // 窗口轨迹自行验证固定实例和前台状态；目标激活不能被前台配置切换误取消。
                _deferredProfile = profileSnapshot;
                _hasDeferredProfile = true;
                return;
            }

            // 同一配置的保存或界面刷新不能中断当前仍按住的触发器。
            if (previousProfileId == nextProfileId)
            {
                _activeProfile = profileSnapshot;
                return;
            }

            pendingTasks = CancelActiveOperations(
                cancelRun: false,
                trigger => !nextProfileId.HasValue || trigger.ProfileId != nextProfileId.Value);
            _activeProfile = profileSnapshot;
        }

        WaitForTasks(pendingTasks, TimeSpan.FromSeconds(1));
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (_lifecycleSync)
        {
            lock (_sync)
            {
                if (_isRunning)
                    return;

                CancelKeyboardDrainObservation();
                _runStop = new CancellationTokenSource();
            }

            AttachHookHandlers();
            var keyboardWasRunning = _hookService.IsRunning;
            var mouseWasRunning = _mouseHookService.IsRunning;
            try
            {
                try
                {
                    if (!keyboardWasRunning)
                    {
                        lock (_sync)
                        {
                            // 钩子已退出期间没有可靠事件；新安装必须重新读取系统基线。
                            _keyboardPressSessions.Clear();
                            _physicalInputsDown.RemoveWhere(input => input.Kind == InputTriggerKind.Keyboard);
                        }
                        _hookService.Start();
                        lock (_sync)
                            _ownsKeyboardHook = true;
                    }
                }
                finally
                {
                    // 安装后启动失败也必须捕获新身份，回滚不能误用上一轮已结束的身份。
                    lock (_sync)
                    {
                        var lifetime = (_hookService as IKeyboardHookLifetimeSource)?.CurrentLifetime;
                        // 已运行但身份变化表示借用了其他使用者的新安装，不能继承旧所有权。
                        if (keyboardWasRunning && !ReferenceEquals(lifetime, _keyboardHookLifetime))
                            _ownsKeyboardHook = false;
                        _keyboardHookLifetime = lifetime;
                    }
                }

                if (!mouseWasRunning)
                {
                    _mouseHookService.Start();
                    lock (_sync)
                        _ownsMouseHook = true;
                }
                lock (_sync)
                {
                    if (_hookService is IKeyboardStateSource state)
                    {
                        var pressed = state.GetPressedKeys();
                        var keys = pressed.Select(key => key.StorageKey).ToHashSet();
                        _physicalInputsDown.RemoveWhere(input => input.Kind == InputTriggerKind.Keyboard);
                        foreach (var key in _keyboardPressSessions.Keys.Where(key => !keys.Contains(key)).ToArray())
                            _keyboardPressSessions.Remove(key);
                        foreach (var key in pressed)
                        {
                            _physicalInputsDown.Add(InputTriggerIdentity.FromKeyboard(key));
                            _keyboardPressSessions.TryAdd(key.StorageKey, new(null, false, key.VirtualKeyCode));
                        }
                    }
                    _isRunning = true;
                    ReleaseKeyboardDrainWhileRunning();
                }
            }
            catch
            {
                RollBackHookStartup(keyboardWasRunning, mouseWasRunning);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lifecycleSync)
        {
            if (_mouseHandlerAttached)
            {
                _mouseHookService.AnyMouseInput -= OnAnyMouseInput;
                _mouseHandlerAttached = false;
            }

            bool stopMouseHook;
            lock (_sync)
            {
                _isRunning = false;
                stopMouseHook = _ownsMouseHook;
                _ownsMouseHook = false;
                foreach (var key in _keyboardPressSessions.Keys.ToArray())
                    _keyboardPressSessions[key] = _keyboardPressSessions[key] with { RuntimeKey = null, IsDraining = true };
                // 已退出的钩子不能再排空事件；下次启动重新建立系统快照。
                if (_disposed || !_hookService.IsRunning || _keyboardHookLifetime?.IsCompletedSuccessfully == true)
                    _keyboardPressSessions.Clear();
                if (_keyboardPressSessions.Count != 0)
                {
                    _keyboardDrainLease ??= _inputLeases.ReserveCleanup();
                    if (_keyboardDrainObservation is null && _keyboardHookLifetime is { } lifetime)
                    {
                        var observation = new CancellationTokenSource();
                        _keyboardDrainObservation = observation;
                        _ = Task.Run(() => ObserveKeyboardTerminationAsync(lifetime, observation));
                    }
                }
            }

            var pendingTasks = CancelActiveOperations(cancelRun: true);
            if (stopMouseHook)
                TryStopHook(_mouseHookService.Stop, "停止鼠标钩子失败");
            StopKeyboardHookIfIdle();
            WaitForTasks(pendingTasks, TimeSpan.FromSeconds(3));

            lock (_sync)
            {
                _runStop?.Dispose();
                _runStop = null;
                _toggleStates.Clear();
                _physicalInputsDown.RemoveWhere(input => input.Kind != InputTriggerKind.Keyboard);
                _mousePressSessions.Clear();
            }
        }
    }

    private void AttachHookHandlers()
    {
        if (!_keyboardHandlerAttached)
        {
            _hookService.AnyKeyStateChanged += OnAnyKeyStateChanged;
            _keyboardHandlerAttached = true;
        }
        if (!_mouseHandlerAttached)
        {
            _mouseHookService.AnyMouseInput += OnAnyMouseInput;
            _mouseHandlerAttached = true;
        }
    }

    private void DetachHookHandlers()
    {
        _hookService.AnyKeyStateChanged -= OnAnyKeyStateChanged;
        _mouseHookService.AnyMouseInput -= OnAnyMouseInput;
        _keyboardHandlerAttached = false;
        _mouseHandlerAttached = false;
    }

    /// <summary>运行已停止且原物理按键均释放后才归还键盘钩子；不等待新的按键。</summary>
    private void StopKeyboardHookIfIdle()
    {
        lock (_lifecycleSync)
        {
            bool stopHook;
            IDisposable? drainLease;
            lock (_sync)
            {
                if (_isRunning || _keyboardPressSessions.Count != 0) return;
                CancelKeyboardDrainObservation();
                if (_keyboardHandlerAttached)
                {
                    _hookService.AnyKeyStateChanged -= OnAnyKeyStateChanged;
                    _keyboardHandlerAttached = false;
                }
                // 所有权只属于最初的安装；其他使用者重装后不能沿用旧所有权关闭新钩子。
                stopHook = _ownsKeyboardHook && (_hookService is not IKeyboardHookLifetimeSource source ||
                    (ReferenceEquals(source.CurrentLifetime, _keyboardHookLifetime) &&
                     _keyboardHookLifetime is { IsCompletedSuccessfully: false } && _hookService.IsRunning));
                _ownsKeyboardHook = false;
                _physicalInputsDown.RemoveWhere(input => input.Kind == InputTriggerKind.Keyboard);
                drainLease = _keyboardDrainLease;
                _keyboardDrainLease = null;
            }
            try
            {
                if (stopHook) StopKeyboardInstallation("停止键盘钩子失败");
            }
            finally { drainLease?.Dispose(); }
        }
    }

    /// <summary>已重启时钩子继续服务；原停止按键释放后可立即归还清理占用。调用方持有 _sync。</summary>
    private void ReleaseKeyboardDrainWhileRunning()
    {
        if (!_isRunning || _keyboardPressSessions.Values.Any(session => session.IsDraining)) return;
        _keyboardDrainLease?.Dispose();
        _keyboardDrainLease = null;
    }

    /// <summary>只结束当前停止周期所属安装的收尾，不伪造抬起，也不处理重启后的会话。</summary>
    private async Task ObserveKeyboardTerminationAsync(Task lifetime, CancellationTokenSource observation)
    {
        try
        {
            await lifetime.WaitAsync(observation.Token).ConfigureAwait(false);
            lock (_lifecycleSync)
            {
                lock (_sync)
                {
                    if (_disposed || _isRunning || observation.IsCancellationRequested ||
                        !ReferenceEquals(_keyboardDrainObservation, observation) ||
                        !ReferenceEquals(_keyboardHookLifetime, lifetime)) return;
                    _keyboardPressSessions.Clear();
                }
                StopKeyboardHookIfIdle();
            }
        }
        catch (OperationCanceledException) when (observation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportRuntimeError("等待键盘钩子收尾失败", exception);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_keyboardDrainObservation, observation)) _keyboardDrainObservation = null;
                observation.Dispose();
            }
        }
    }

    /// <summary>调用方持有 _sync；等待方负责最终释放取消源，避免取消与释放竞争。</summary>
    private void CancelKeyboardDrainObservation()
    {
        var observation = _keyboardDrainObservation;
        _keyboardDrainObservation = null;
        observation?.Cancel();
    }

    /// <summary>身份检查和停止由原生服务原子执行；不支持生命周期扩展的既有适配器维持原调用契约。</summary>
    private void StopKeyboardInstallation(string errorMessage)
    {
        var lifetime = _keyboardHookLifetime;
        TryStopHook(() =>
        {
            if (_hookService is IKeyboardHookLifetimeControl control && lifetime is not null)
                _ = control.StopIfCurrent(lifetime);
            else
                _hookService.Stop();
        }, errorMessage);
    }

    private void RollBackHookStartup(bool keyboardWasRunning, bool mouseWasRunning)
    {
        DetachHookHandlers();

        bool stopKeyboardHook;
        bool stopMouseHook;
        lock (_sync)
        {
            _isRunning = false;
            stopKeyboardHook = _ownsKeyboardHook ||
                               (!keyboardWasRunning && _hookService.IsRunning);
            stopMouseHook = _ownsMouseHook ||
                            (!mouseWasRunning && _mouseHookService.IsRunning);
            _ownsKeyboardHook = false;
            _ownsMouseHook = false;
        }

        var pendingTasks = CancelActiveOperations(cancelRun: true);
        if (stopMouseHook)
            TryStopHook(_mouseHookService.Stop, "回滚鼠标钩子失败");
        if (stopKeyboardHook)
            StopKeyboardInstallation("回滚键盘钩子失败");
        WaitForTasks(pendingTasks, TimeSpan.FromSeconds(1));

        lock (_sync)
        {
            _runStop?.Dispose();
            _runStop = null;
            _mousePressSessions.Clear();
            _physicalInputsDown.Clear();
            _keyboardPressSessions.Clear();
            CancelKeyboardDrainObservation();
            _keyboardDrainLease?.Dispose();
            _keyboardDrainLease = null;
        }
    }

    private void OnAnyMouseInput(object? sender, MouseInputEventArgs args)
    {
        var input = args.Input;
        if (input.Kind == MouseInputKind.Wheel || !IsValidMouseButton(input.Button))
            return;

        if (input.Kind == MouseInputKind.ButtonUp)
        {
            CompleteMousePressSession(input.Button, args);
            return;
        }

        if (input.Kind != MouseInputKind.ButtonDown)
            return;

        if (_inputLeases.IsRecording) return;

        lock (_sync)
        {
            if (!_isRunning)
                return;

            if (_mousePressSessions.TryGetValue(input.Button, out var existingSession))
            {
                args.SuppressOriginalInput = existingSession.SuppressOriginalInput;
                return;
            }
        }

        AppProfile? profile;
        try
        {
            if (!_processNameResolver.TryGetProcessName(
                    input.TargetProcessId,
                    out var processName))
            {
                return;
            }

            profile = _profileCatalog.ResolveKnownProcess(processName);
        }
        catch (Exception ex)
        {
            ReportRuntimeError("解析鼠标指针目标失败", ex);
            return;
        }

        var config = profile?.MouseButtonConfigs?
            .GetValueOrDefault(input.Button)?
            .DeepClone();
        if (profile is null || config is null || !HasEnabledAction(config))
            return;

        var source = TriggerSource.FromMouse(input.Button);
        var runtimeTrigger = new RuntimeTriggerKey(profile.ProfileId, source.Identity);
        var session = new MouseTriggerSession(
            runtimeTrigger,
            source,
            config,
            SuppressOriginalInput: !config.PassThroughOriginalInput);
        Task[] pendingTasks;

        lock (_sync)
        {
            if (!_isRunning)
                return;

            if (_mousePressSessions.TryGetValue(input.Button, out var existingSession))
            {
                args.SuppressOriginalInput = existingSession.SuppressOriginalInput;
                return;
            }

            pendingTasks = _inputLeases.IsPathActive ? [] : CancelActiveOperations(
                cancelRun: false,
                trigger => trigger.ProfileId != profile.ProfileId);
            _mousePressSessions[input.Button] = session;
            _physicalInputsDown.Add(source.Identity);
        }

        args.SuppressOriginalInput = session.SuppressOriginalInput;
        if (pendingTasks.Length > 0)
            _ = Task.Run(() => WaitForTasks(pendingTasks, TimeSpan.FromSeconds(1)));

        switch (Math.Clamp(config.TriggerMode, 0, 2))
        {
            case 0:
                StartConfiguredAction(
                    runtimeTrigger,
                    source,
                    config,
                    isMomentary: true,
                    requirePhysicalDown: true);
                break;
            case 1:
                ToggleConfiguredAction(runtimeTrigger, source, config);
                break;
            case 2:
                StartLongPress(runtimeTrigger, source, config);
                break;
        }
    }

    private void CompleteMousePressSession(
        MouseButtonKind button,
        MouseInputEventArgs args)
    {
        MouseTriggerSession? session;
        lock (_sync)
        {
            if (!_isRunning || !_mousePressSessions.Remove(button, out session))
                return;

            _physicalInputsDown.Remove(session.Source.Identity);
        }

        // 松开时复用按下时的目标与抑制决定，避免指针跨窗口后产生不成对事件。
        args.SuppressOriginalInput = session.SuppressOriginalInput;
        CancelLongPress(session.RuntimeKey);

        bool cancelTurbo;
        bool cancelMacro;
        lock (_sync)
        {
            cancelTurbo = _momentaryTurboTriggers.Contains(session.RuntimeKey);
            cancelMacro = _momentaryMacroTriggers.Contains(session.RuntimeKey);
        }

        if (cancelTurbo)
            CancelTurbo(session.RuntimeKey);
        if (cancelMacro)
            CancelMacro(session.RuntimeKey);
    }

    private void OnProfilesReplaced(object? sender, EventArgs args)
    {
        var pendingTasks = CancelActiveOperations(cancelRun: false);
        if (pendingTasks.Length > 0)
            _ = Task.Run(() => WaitForTasks(pendingTasks, TimeSpan.FromSeconds(1)));
    }

    private void OnAnyKeyStateChanged(object? sender, KeyboardInputEventArgs args)
    {
        var input = args.Input;
        var source = TriggerSource.FromKeyboard(input.Key);
        bool emergencyStop;
        lock (_sync)
        {
            if (_hookService is IKeyboardHookLifetimeSource sourceLifetime &&
                !ReferenceEquals(sourceLifetime.CurrentLifetime, _keyboardHookLifetime)) return;
            // 安装前的回车快照只能识别 VK_RETURN；首个真实事件消除另一个被动别名。
            foreach (var pair in _keyboardPressSessions.Where(pair =>
                         pair.Key != input.Key.StorageKey && pair.Value.InitialVirtualKey == input.Key.VirtualKeyCode).ToArray())
            {
                _keyboardPressSessions.Remove(pair.Key);
                _physicalInputsDown.Remove(new(InputTriggerKind.Keyboard, pair.Key));
            }
            if (input.IsDown) _physicalInputsDown.Add(source.Identity);
            else _physicalInputsDown.Remove(source.Identity);

            emergencyStop = _isRunning && input.IsDown && !input.IsRepeat &&
                _shortcuts.EmergencyStop.Matches(input.Key.VirtualKeyCode,
                    _physicalInputsDown.Where(key => key.Kind == InputTriggerKind.Keyboard)
                        .Select(key => KeyboardKeyIdentity.FromStorageKey(key.Code).VirtualKeyCode));
            if (emergencyStop)
                _isRunning = false;
        }
        if (emergencyStop)
        {
            var pending = CancelActiveOperations(cancelRun: true);
            RaiseEmergencyStopRequested();
            _ = Task.Run(() => WaitForTasks(pending, TimeSpan.FromSeconds(3)));
            return;
        }

        lock (_sync)
        {
            if (_keyboardPressSessions.TryGetValue(input.Key.StorageKey, out var session))
            {
                args.SuppressOriginalInput = session.SuppressOriginalInput;
                if (input.IsDown) return; // 自动重复和重复通知都属于最初那次按下。
                _keyboardPressSessions.Remove(input.Key.StorageKey);
                ReleaseKeyboardDrainWhileRunning();
                if (session.RuntimeKey is { } released)
                {
                    CancelLongPress(released);
                    if (_momentaryTurboTriggers.Contains(released)) CancelTurbo(released);
                    if (_momentaryMacroTriggers.Contains(released)) CancelMacro(released);
                }
                if (!_isRunning && _keyboardPressSessions.Count == 0)
                    _ = Task.Run(StopKeyboardHookIfIdle);
                return;
            }

            // 未观察到按下的抬起必须放行；停止期间的新按键也不建立拦截或触发会话。
            if (!input.IsDown || !_isRunning)
            {
                if (!_isRunning && _keyboardPressSessions.Count == 0)
                    _ = Task.Run(StopKeyboardHookIfIdle);
                return;
            }
            var config = !input.IsRepeat && !_inputLeases.IsRecording
                ? _activeProfile?.KeyConfigs?.GetValueOrDefault(input.Key.StorageKey)?.DeepClone()
                : null;
            RuntimeTriggerKey? trigger = config is not null && _activeProfile is not null
                ? new(_activeProfile.ProfileId, source.Identity) : null;
            args.SuppressOriginalInput = config is not null && HasEnabledAction(config) && !config.PassThroughOriginalInput;
            _keyboardPressSessions[input.Key.StorageKey] = new(trigger, args.SuppressOriginalInput);
            if (config is null || trigger is not { } pressed || !HasEnabledAction(config)) return;
            switch (Math.Clamp(config.TriggerMode, 0, 2))
            {
                case 0:
                    StartConfiguredAction(pressed, source, config, isMomentary: true, requirePhysicalDown: true);
                    break;
                case 1:
                    ToggleConfiguredAction(pressed, source, config);
                    break;
                case 2:
                    StartLongPress(pressed, source, config);
                    break;
            }
        }
    }

    private bool StartConfiguredAction(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config,
        bool isMomentary,
        bool requirePhysicalDown = false,
        IDisposable? reservedLease = null)
    {
        if (HasEnabledMacro(config))
        {
            return StartMacro(
                trigger,
                config.RuntimeMacro ?? new MacroDefinition { Steps = config.MacroSteps },
                isMomentary,
                requirePhysicalDown, reservedLease);
        }

        return config.IsTurboEnabled && StartTurbo(trigger, source, config, isMomentary, requirePhysicalDown, reservedLease);
    }

    private void ToggleConfiguredAction(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config)
    {
        bool shouldStart;
        lock (_sync)
        {
            _toggleStates.TryGetValue(trigger, out var isActive);
            shouldStart = !isActive;
            _toggleStates[trigger] = shouldStart;
        }

        if (shouldStart)
        {
            if (!StartConfiguredAction(trigger, source, config, isMomentary: false))
                lock (_sync) _toggleStates.Remove(trigger);
        }
        else
            CancelConfiguredAction(trigger);
    }

    private void CancelConfiguredAction(RuntimeTriggerKey trigger)
    {
        CancelTurbo(trigger);
        CancelMacro(trigger);
    }

    private void StartLongPress(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config)
    {
        CancellationTokenSource stop;
        IDisposable lease;
        lock (_sync)
        {
            if (_longPressTasks.ContainsKey(trigger) || _runStop == null)
                return;

            var admission = _inputLeases.TryAcquire(HasPath(config));
            if (!admission.Succeeded) { ReportInputBusy(); return; }
            lease = admission.Lease!;
            stop = CancellationTokenSource.CreateLinkedTokenSource(_runStop.Token);
            _longPressStops[trigger] = stop;
            _longPressTasks[trigger] = Task.Run(
                () => RunLongPressAsync(trigger, source, config, stop, lease),
                CancellationToken.None);
        }
    }

    private async Task RunLongPressAsync(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config,
        CancellationTokenSource stop,
        IDisposable lease)
    {
        var transferred = false;
        try
        {
            await Task.Delay(
                Math.Clamp(config.LongPressThresholdMs, 250, 2000),
                stop.Token).ConfigureAwait(false);

            lock (_sync)
            {
                if (!_physicalInputsDown.Contains(source.Identity) || !_isRunning)
                    return;
            }

            transferred = StartConfiguredAction(
                trigger,
                source,
                config,
                isMomentary: true,
                requirePhysicalDown: true, reservedLease: lease);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportRuntimeError("长按触发失败", ex);
        }
        finally
        {
            if (!transferred) lease.Dispose();
            lock (_sync)
            {
                if (_longPressStops.GetValueOrDefault(trigger) == stop)
                {
                    _longPressStops.Remove(trigger);
                    _longPressTasks.Remove(trigger);
                }
            }

            stop.Dispose();
        }
    }

    private bool StartTurbo(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config,
        bool isMomentary,
        bool requirePhysicalDown = false,
        IDisposable? reservedLease = null)
    {
        CancellationTokenSource stop;
        lock (_sync)
        {
            if (_turboTasks.ContainsKey(trigger) || _runStop == null || !_isRunning ||
                (requirePhysicalDown && !_physicalInputsDown.Contains(source.Identity)))
                return false;

            var lease = reservedLease ?? _inputLeases.TryAcquire(false).Lease;
            if (lease is null) { ReportInputBusy(); return false; }
            stop = CancellationTokenSource.CreateLinkedTokenSource(_runStop.Token);
            _turboStops[trigger] = stop;
            if (isMomentary)
                _momentaryTurboTriggers.Add(trigger);
            else
                _momentaryTurboTriggers.Remove(trigger);

            _turboTasks[trigger] = Task.Run(
                () => RunTurboAsync(trigger, source, config, stop, lease),
                CancellationToken.None);
            return true;
        }
    }

    private async Task RunTurboAsync(
        RuntimeTriggerKey trigger,
        TriggerSource source,
        TriggerBehaviorConfig config,
        CancellationTokenSource stop,
        IDisposable lease)
    {
        var owned = new OwnedInputState(_inputService, _mouseInputService);
        try
        {
            var keyDownDuration = Math.Clamp(config.KeyDownDuration, 10, 1000);
            var keyUpDuration = Math.Clamp(config.KeyUpDuration, 10, 1000);

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (source.Identity.Kind == InputTriggerKind.MouseButton) owned.ButtonDown(source.MouseButton);
                    else owned.KeyDown(source.KeyboardKey);
                    await Task.Delay(keyDownDuration, stop.Token).ConfigureAwait(false);
                }
                finally
                {
                    owned.Cleanup();
                }

                if (owned.Failures.Count > 0) return;
                await Task.Delay(keyUpDuration, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportRuntimeError("连发执行失败", ex);
        }
        finally
        {
            owned.Cleanup();
            ReportOwnedFailures(owned);
            lease.Dispose();
            lock (_sync)
            {
                if (_turboStops.GetValueOrDefault(trigger) == stop)
                {
                    _turboStops.Remove(trigger);
                    _turboTasks.Remove(trigger);
                    _momentaryTurboTriggers.Remove(trigger);
                    _toggleStates.Remove(trigger);
                }
            }

            stop.Dispose();
        }
    }

    private bool StartMacro(
        RuntimeTriggerKey trigger,
        MacroDefinition configuredMacro,
        bool isMomentary,
        bool requirePhysicalDown = false,
        IDisposable? reservedLease = null)
    {
        CancellationTokenSource stop;
        var macro = configuredMacro.DeepClone();
        var steps = macro.Steps;

        lock (_sync)
        {
            if (_macroTasks.ContainsKey(trigger) || _runStop == null || !_isRunning ||
                (requirePhysicalDown && !_physicalInputsDown.Contains(trigger.Trigger)))
                return false;

            var lease = reservedLease ?? _inputLeases.TryAcquire(steps.Any(s => s.ActionType == MacroActionType.MousePath)).Lease;
            if (lease is null) { ReportInputBusy(); return false; }
            stop = CancellationTokenSource.CreateLinkedTokenSource(_runStop.Token);
            _macroStops[trigger] = stop;
            if (steps.Any(s => s.MousePath is not null) && macro.PathGroups.Any(g => g.TargetId is not null))
                _windowPathTriggers.Add(trigger);
            if (isMomentary)
                _momentaryMacroTriggers.Add(trigger);
            else
                _momentaryMacroTriggers.Remove(trigger);

            _macroTasks[trigger] = Task.Run(
                () => RunMacroAsync(trigger, macro, stop, lease),
                CancellationToken.None);
            return true;
        }
    }

    private async Task RunMacroAsync(
        RuntimeTriggerKey trigger,
        MacroDefinition macro,
        CancellationTokenSource stop,
        IDisposable lease)
    {
        var owned = new OwnedInputState(_inputService, _mouseInputService);
        var steps = macro.Steps;
        MousePathExecutionSession? pathSession = null;
        var hasConfiguredDelay = steps.Any(step => step.DurationMs > 0);

        try
        {
            if (steps.Any(s => s.ActionType == MacroActionType.MousePath))
                pathSession = (_pathPlayback ?? throw new InvalidOperationException("尚未配置轨迹播放服务。"))
                    .CreateSession(macro, owned, stop.Token);
            while (!stop.IsCancellationRequested)
            {
                if (pathSession is not null) await pathSession.PlayCycleAsync(stop.Token).ConfigureAwait(false);
                else await RunMacroCycleAsync(steps, owned, stop.Token).ConfigureAwait(false);
                owned.Cleanup();
                if (owned.Failures.Count > 0) return;

                // 完全无延迟的宏每轮主动让出执行权，避免形成高占用忙循环。
                if (!hasConfiguredDelay)
                    await Task.Delay(1, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportRuntimeError("宏回放失败", ex);
        }
        finally
        {
            owned.Cleanup();
            pathSession?.Dispose();
            if (pathSession?.CleanupIssues.Count > 0)
                ReportRuntimeError("释放轨迹窗口绑定失败", new MousePathExecutionException(pathSession.CleanupIssues));
            ReportOwnedFailures(owned);
            lease.Dispose();

            lock (_sync)
            {
                if (_macroStops.GetValueOrDefault(trigger) == stop)
                {
                    _macroStops.Remove(trigger);
                    _macroTasks.Remove(trigger);
                    _windowPathTriggers.Remove(trigger);
                    _momentaryMacroTriggers.Remove(trigger);
                    _toggleStates.Remove(trigger);
                }
            }

            stop.Dispose();
            ApplyDeferredProfile();
        }
    }

    private void ApplyDeferredProfile()
    {
        AppProfile? profile;
        lock (_sync)
        {
            if (!_hasDeferredProfile || _windowPathTriggers.Count != 0) return;
            profile = _deferredProfile; _deferredProfile = null; _hasDeferredProfile = false;
        }
        SetActiveProfile(profile);
    }

    private async Task RunMacroCycleAsync(
        IReadOnlyCollection<MacroStep> steps,
        OwnedInputState owned,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step.ActionType == MacroActionType.Delay)
            {
                await Task.Delay(
                    MacroDelay.ResolveMilliseconds(step),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            switch (step.ActionType)
            {
                case MacroActionType.KeyDown:
                {
                    var key = KeyboardKeyIdentity.FromMacroStep(step);
                    if (!key.IsValid)
                        continue;

                    owned.KeyDown(key);
                    break;
                }
                case MacroActionType.KeyUp:
                {
                    var key = KeyboardKeyIdentity.FromMacroStep(step);
                    if (!key.IsValid)
                        continue;

                    owned.KeyUp(key);
                    break;
                }
                case MacroActionType.MouseButtonDown:
                    if (!IsValidMouseButton(step.MouseButton))
                        continue;

                    owned.ButtonDown(step.MouseButton);
                    break;
                case MacroActionType.MouseButtonUp:
                    if (!IsValidMouseButton(step.MouseButton))
                        continue;

                    owned.ButtonUp(step.MouseButton);
                    break;
                case MacroActionType.MouseWheel:
                    if (step.WheelDelta == 0)
                        continue;

                    owned.Wheel(step.WheelDelta);
                    break;
                default:
                    continue;
            }

            // 兼容旧配置：动作本身携带的 DurationMs 仍作为动作后的等待时间。
            if (step.DurationMs > 0 || step.RandomDurationMaxMs is > 0)
            {
                await Task.Delay(
                    MacroDelay.ResolveMilliseconds(step),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ReportInputBusy() => ReportRuntimeError("自动化输入正被其他会话使用", new MousePathExecutionException([new(PathIssueCode.InputBusy)]));
    private void ReportOwnedFailures(OwnedInputState owned)
    {
        foreach (var failure in owned.Failures)
        {
            Debug.WriteLine(failure.Exception);
            ReportRuntimeError($"输入会话失败 ({failure.Issue.Code})", new MousePathExecutionException([failure.Issue]));
        }
    }

    private static bool HasPath(TriggerBehaviorConfig config) => HasEnabledMacro(config) &&
        (config.RuntimeMacro?.Steps ?? config.MacroSteps).Any(s => s.ActionType == MacroActionType.MousePath);

    private Task[] CancelActiveOperations(
        bool cancelRun,
        Func<RuntimeTriggerKey, bool>? shouldCancel = null)
    {
        lock (_sync)
        {
            shouldCancel ??= static _ => true;

            if (cancelRun)
                _runStop?.Cancel();

            foreach (var stop in _turboStops
                         .Where(pair => shouldCancel(pair.Key))
                         .Select(pair => pair.Value)
                         .ToArray())
                stop.Cancel();
            foreach (var stop in _longPressStops
                         .Where(pair => shouldCancel(pair.Key))
                         .Select(pair => pair.Value)
                         .ToArray())
                stop.Cancel();
            foreach (var stop in _macroStops
                         .Where(pair => shouldCancel(pair.Key))
                         .Select(pair => pair.Value)
                         .ToArray())
                stop.Cancel();

            foreach (var trigger in _toggleStates.Keys.Where(shouldCancel).ToArray())
                _toggleStates.Remove(trigger);
            _momentaryTurboTriggers.RemoveWhere(trigger => shouldCancel(trigger));
            _momentaryMacroTriggers.RemoveWhere(trigger => shouldCancel(trigger));

            return _turboTasks
                .Where(pair => shouldCancel(pair.Key))
                .Select(pair => pair.Value)
                .Concat(_longPressTasks
                    .Where(pair => shouldCancel(pair.Key))
                    .Select(pair => pair.Value))
                .Concat(_macroTasks
                    .Where(pair => shouldCancel(pair.Key))
                    .Select(pair => pair.Value))
                .Distinct()
                .ToArray();
        }
    }

    private void CancelTurbo(RuntimeTriggerKey trigger)
    {
        lock (_sync)
        {
            if (_turboStops.TryGetValue(trigger, out var stop))
                stop.Cancel();
            _momentaryTurboTriggers.Remove(trigger);
        }
    }

    private void CancelLongPress(RuntimeTriggerKey trigger)
    {
        lock (_sync)
        {
            if (_longPressStops.TryGetValue(trigger, out var stop))
                stop.Cancel();
        }
    }

    private void CancelMacro(RuntimeTriggerKey trigger)
    {
        lock (_sync)
        {
            if (_macroStops.TryGetValue(trigger, out var stop))
                stop.Cancel();
            _momentaryMacroTriggers.Remove(trigger);
        }
    }

    private void TryStopHook(Action stopHook, string message)
    {
        try
        {
            stopHook();
        }
        catch (Exception ex)
        {
            ReportRuntimeError(message, ex);
        }
    }

    private void ReportRuntimeError(string message, Exception exception)
    {
        Debug.WriteLine($"[TurboOrchestratorService] {message}: {exception}");
        if (RuntimeError is not { } handlers) return;
        // 错误展示属于外部订阅者，不能打断按键清理或执行权归还。
        foreach (EventHandler<string> handler in handlers.GetInvocationList())
        {
            try { handler(this, exception is MousePathExecutionException path
                ? PathRuntimeDiagnostic.Encode(path.Issues) : $"{message}: {exception.Message}"); }
            catch (Exception notificationError) { Debug.WriteLine($"运行错误通知失败：{notificationError}"); }
        }
    }

    private void RaiseEmergencyStopRequested() =>
        _emergencyStopRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsAnyControlDown(IReadOnlySet<InputTriggerIdentity> inputs) =>
        ContainsKeyboardInput(inputs, 0x11) ||
        ContainsKeyboardInput(inputs, 0xA2) ||
        ContainsKeyboardInput(inputs, 0xA3);

    private static bool IsAnyShiftDown(IReadOnlySet<InputTriggerIdentity> inputs) =>
        ContainsKeyboardInput(inputs, 0x10) ||
        ContainsKeyboardInput(inputs, 0xA0) ||
        ContainsKeyboardInput(inputs, 0xA1);

    private static bool ContainsKeyboardInput(
        IReadOnlySet<InputTriggerIdentity> inputs,
        int storageKey) =>
        inputs.Contains(new InputTriggerIdentity(InputTriggerKind.Keyboard, storageKey));

    private static bool HasEnabledAction(TriggerBehaviorConfig? config) =>
        config?.IsTurboEnabled == true ||
        HasEnabledMacro(config);

    private static bool HasEnabledMacro(TriggerBehaviorConfig? config) =>
        config?.IsMacroEnabled == true && (config.RuntimeMacro?.Steps.Count ?? config.MacroSteps.Count) > 0;

    private static bool IsValidMouseButton(MouseButtonKind button) =>
        button is >= MouseButtonKind.Left and <= MouseButtonKind.XButton2;

    private static MacroStep CloneMacroStep(MacroStep step)
    {
        return step.DeepClone();
    }

    /// <summary>初始快照中的键只透传；RuntimeKey 仅在原按下所属运行会话内有效。</summary>
    private sealed record KeyboardPressSession(
        RuntimeTriggerKey? RuntimeKey, bool SuppressOriginalInput, int? InitialVirtualKey = null, bool IsDraining = false);

    private sealed record MouseTriggerSession(
        RuntimeTriggerKey RuntimeKey,
        TriggerSource Source,
        MouseButtonConfig Config,
        bool SuppressOriginalInput);

    private readonly record struct TriggerSource(
        InputTriggerIdentity Identity,
        KeyboardKeyIdentity KeyboardKey,
        MouseButtonKind MouseButton)
    {
        public static TriggerSource FromKeyboard(KeyboardKeyIdentity key) =>
            new(InputTriggerIdentity.FromKeyboard(key), key, MouseButtonKind.None);

        public static TriggerSource FromMouse(MouseButtonKind button) =>
            new(InputTriggerIdentity.FromMouse(button), default, button);
    }

    private static void WaitForTasks(IEnumerable<Task> tasks, TimeSpan timeout)
    {
        var pending = tasks.Where(task => !task.IsCompleted).ToArray();
        if (pending.Length == 0)
            return;

        try
        {
            _ = Task.WhenAll(pending).Wait(timeout);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(
                   inner => inner is TaskCanceledException or OperationCanceledException))
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _profileCatalog.ProfilesReplaced -= OnProfilesReplaced;
        Stop();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TurboOrchestratorService));
    }
}
