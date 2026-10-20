using System.ComponentModel;
using HyperKeyX.Application.Automation.Recording;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Keyboard.ViewModels;
using HyperKeyX.Features.Mouse.ViewModels;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private void OnSelectedTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTriggerConfig) ||
            sender is not TriggerConfigViewModel trigger)
        {
            return;
        }

        if (e.PropertyName == nameof(TriggerConfigViewModel.KeyDownDuration))
            KeyDownDuration = NormalizeDuration(trigger.KeyDownDuration);
        else if (e.PropertyName == nameof(TriggerConfigViewModel.KeyUpDuration))
            KeyUpDuration = NormalizeDuration(trigger.KeyUpDuration);
        else if (e.PropertyName == nameof(TriggerConfigViewModel.SelectedTriggerIndex))
            SelectedTriggerIndex = Math.Clamp(trigger.SelectedTriggerIndex, 0, 2);

        if (e.PropertyName is
            nameof(TriggerConfigViewModel.IsTurboEnabled) or
            nameof(TriggerConfigViewModel.IsMacroEnabled) or
            nameof(TriggerConfigViewModel.SelectedMacroId) or
            nameof(TriggerConfigViewModel.IsDirty) or
            nameof(TriggerConfigViewModel.CanSave))
        {
            if (trigger is KeyConfigViewModel key)
                RefreshKeyState(key.KeyId);
            else if (trigger is MouseButtonConfigViewModel mouse)
                RefreshMouseButtonState(mouse.Button);
        }

        if (e.PropertyName is nameof(TriggerConfigViewModel.IsDirty) or
            nameof(TriggerConfigViewModel.CanSave) or
            nameof(TriggerConfigViewModel.SelectedMacroId))
        {
            if (SelectedProfile is not null)
            {
                RefreshDraftReference(
                    SelectedProfile.Profile.ProfileId,
                    trigger);
            }

            RaiseTriggerDraftState();
        }
    }

    private void SelectKeyFromCommandParameter(object? parameter)
    {
        switch (parameter)
        {
            case KeyButtonStateViewModel keyState:
                SelectKey(keyState.KeyId);
                break;
            case KeyConfigViewModel key:
                SelectKey(key.KeyId);
                break;
            case int keyId:
                SelectKey(keyId);
                break;
            case string text when int.TryParse(text, out var keyId):
                SelectKey(keyId);
                break;
        }
    }

    public void SelectKey(int keyId)
    {
        var identity = KeyboardKeyIdentity.FromStorageKey(keyId);
        if (!identity.IsValid)
            return;

        if (IsRecordingSessionActive &&
            (_recordingOriginTrigger?.IsMouseTrigger == true || SelectedKey.KeyId != keyId))
        {
            _ = RejectContextChangeWhileRecording();
            return;
        }

        SelectedKey = GetOrCreateKeyEditor(keyId);
        StatusText = L(
            $"已选择按键: {SelectedKey.KeyDisplayName}",
            $"Selected key: {SelectedKey.KeyDisplayName}");
    }

    public void SaveKeyConfig()
    {
        if (SaveCurrentTrigger())
        {
            RefreshKeyState(SelectedKey.KeyId);
            StatusText = L(
                $"已保存配置: {SelectedKey.KeyDisplayName}",
                $"Saved: {SelectedKey.KeyDisplayName}");
        }
    }

    public async Task ToggleRecordingAsync()
    {
        if (IsRecordingSessionActive)
        {
            var reason = RecordingState == MacroRecordingState.Countdown
                ? MacroRecordingStopReason.Cancelled
                : MacroRecordingStopReason.UserRequested;
            try
            {
                await _macroRecordingService.StopAsync(reason);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 停止宏录制失败: {ex}");
                StatusText = L("无法停止宏录制", "Unable to stop recording");
            }

            return;
        }

        if (IsOrchestratorRunning)
        {
            StatusText = L("请先停止连发服务再录制宏", "Stop the turbo service before recording");
            return;
        }

        var target = MacroLibrary.SelectedMacro;
        if (target is null)
        {
            StatusText = L("请先选择一个宏", "Select a macro first");
            return;
        }

        _recordingTargetMacro = target;
        _recordingOriginTrigger = SelectedTriggerConfig;
        _recordingOriginSection = Navigation.ActiveSection;
        try
        {
            HyperKeyX.Application.Common.OperationResult result;
            if (RecordingUsesPath)
            {
                var request = CreatePathRecordingRequest();
                if (request is null)
                {
                    _recordingTargetMacro = null; _recordingOriginTrigger = null; _recordingOriginSection = null;
                    return;
                }
                result = _macroRecordingService is IMousePathRecordingController controller
                    ? await controller.StartPathAsync(request)
                    : HyperKeyX.Application.Common.OperationResult.Failure("Path capture service is unavailable.");
            }
            else result = await _macroRecordingService.StartAsync();
            if (!result.Succeeded && ReferenceEquals(_recordingTargetMacro, target))
            {
                _recordingTargetMacro = null;
                _recordingOriginTrigger = null;
                _recordingOriginSection = null;
                StatusText = L("无法启动宏录制", "Unable to start recording");
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_recordingTargetMacro, target))
            {
                _recordingTargetMacro = null;
                _recordingOriginTrigger = null;
                _recordingOriginSection = null;
            }
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 启动宏录制失败: {ex}");
            StatusText = L("无法启动宏录制", "Unable to start recording");
        }
    }

    private void OnMacroRecordingStatusChanged(
        object? sender,
        MacroRecordingStatus status)
    {
        RunOnUiThread(() => ApplyMacroRecordingStatus(status));
    }

    private void ApplyMacroRecordingStatus(MacroRecordingStatus status)
    {
        RecordingCountdownSeconds = status.CountdownSeconds;
        CapturedStepCount = status.CapturedStepCount;
        RecordingState = status.State;

        StatusText = status.State switch
        {
            MacroRecordingState.Countdown when status.WaitingForRelease => L(
                "请释放全部键盘按键和鼠标按钮后开始录制。", "Release all keyboard keys and mouse buttons to start recording."),
            MacroRecordingState.Countdown => IsEnglish
                ? $"Recording starts in {status.CountdownSeconds} seconds"
                : $"宏录制将在 {status.CountdownSeconds} 秒后开始",
            MacroRecordingState.Recording when RecordingUsesPath => IsEnglish
                ? $"Recording: {status.CapturedStepCount} steps, {status.CapturedPointCount} source points · {Settings.ActiveShortcuts.FinishRecording} to finish"
                : $"轨迹录制中：{status.CapturedStepCount} 步，{status.CapturedPointCount} 个源点 · {Settings.ActiveShortcuts.FinishRecording} 完成",
            MacroRecordingState.Recording => IsEnglish
                ? $"Recording: {status.CapturedStepCount} captured steps"
                : $"宏录制中：已捕获 {status.CapturedStepCount} 步",
            MacroRecordingState.Stopping => L("正在停止宏录制", "Stopping macro recording"),
            _ => StatusText
        };
    }

    private void OnMacroRecordingCompleted(
        object? sender,
        MacroRecordingResult result)
    {
        RunOnUiThread(() => ApplyMacroRecordingResult(result));
    }

    private void ApplyMacroRecordingResult(MacroRecordingResult result)
    {
        var target = _recordingTargetMacro;
        _recordingTargetMacro = null;
        _recordingOriginTrigger = null;
        _recordingOriginSection = null;

        if (result.CanApply && target is not null)
        {
            var captureIssues = (result.PathIssues ?? []).Where(i => i.Code is
                HyperKeyX.Domain.Automation.Paths.PathIssueCode.CaptureInterrupted or
                HyperKeyX.Domain.Automation.Paths.PathIssueCode.CaptureQueueOverflow or
                HyperKeyX.Domain.Automation.Paths.PathIssueCode.CaptureDurationLimitExceeded or
                HyperKeyX.Domain.Automation.Paths.PathIssueCode.CaptureStepLimitExceeded).ToList();
            if (result.Reason is MacroRecordingStopReason.ContextInterrupted or MacroRecordingStopReason.QueueOverflow or
                MacroRecordingStopReason.PointLimitReached or MacroRecordingStopReason.DurationLimitReached)
                captureIssues.Add(new(HyperKeyX.Domain.Automation.Paths.PathIssueCode.CaptureInterrupted));
            var applied = result.Macro is { } macro
                ? target.Editor.InsertRecordedMacro(macro, captureIssues.Distinct().ToArray())
                : target.Editor.ReplaceFromRecording(result.Steps);
            if (!applied)
            {
                StatusText = target.Editor.CommandErrorText;
                return;
            }
        }

        StatusText = result.Reason switch
        {
            MacroRecordingStopReason.UserRequested or MacroRecordingStopReason.Shortcut when !result.Succeeded => L(
                "录制已结束，但发生错误", "Recording ended with errors"),
            MacroRecordingStopReason.UserRequested => L("宏录制已完成", "Recording completed"),
            MacroRecordingStopReason.Shortcut => L("宏录制已由快捷键完成", "Recording completed by shortcut"),
            MacroRecordingStopReason.StepLimitReached => L(
                "已达到宏步骤上限，录制结果已保留",
                "The macro step limit was reached; captured steps were kept"),
            MacroRecordingStopReason.StartupFailed => L(
                "宏录制启动失败",
                "Macro recording failed to start"),
            MacroRecordingStopReason.QueueOverflow or MacroRecordingStopReason.PointLimitReached or
            MacroRecordingStopReason.DurationLimitReached or MacroRecordingStopReason.ContextInterrupted => L(
                "轨迹录制已中断，草稿已保留。请撤销本次录制或删除其轨迹片段后重新录制。",
                "Path capture was interrupted and kept as a draft. Undo this capture or delete its path fragments, then record again."),
            _ => L("宏录制已取消", "Recording cancelled")
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            StatusText += L("：", ": ") + result.ErrorMessage;
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed)
            return;

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                    action();
            }))
        {
            System.Diagnostics.Debug.WriteLine("[MainViewModel] 无法将宏录制状态派发到界面线程。");
        }
    }

    public KeyButtonStateViewModel GetKeyboardKeyState(int keyId)
    {
        if (!_keyboardKeyStates.TryGetValue(keyId, out var state))
        {
            state = new KeyButtonStateViewModel(
                KeyboardKeyIdentity.FromStorageKey(keyId),
                IsEnglish);
            _keyboardKeyStates[keyId] = state;
        }

        state.IsSelected = SelectedKey.KeyId == keyId;
        RefreshKeyState(keyId);
        return state;
    }

    private KeyConfigViewModel GetOrCreateKeyEditor(int keyId)
    {
        var editors = GetCurrentEditorCache();
        if (editors.TryGetValue(keyId, out var editor))
            return editor;

        KeyConfig? config = null;
        _ = SelectedProfile?.Profile.KeyConfigs.TryGetValue(keyId, out config);
        editor = new KeyConfigViewModel(
            keyId,
            config,
            IsEnglish,
            MacroLibrary.PublishedChoices);
        editors[keyId] = editor;
        return editor;
    }

    private void UpdateKeyEditorLocalization()
    {
        var updated = new HashSet<KeyConfigViewModel>(ReferenceEqualityComparer.Instance);

        void Update(KeyConfigViewModel editor)
        {
            if (updated.Add(editor))
                editor.UpdateLocalization(IsEnglish);
        }

        Update(SelectedKey);
        foreach (var editor in _unboundEditors.Values)
            Update(editor);
        foreach (var editors in _profileEditors.Values)
        {
            foreach (var editor in editors.Values)
                Update(editor);
        }
    }

    private Dictionary<int, KeyConfigViewModel> GetCurrentEditorCache()
    {
        if (SelectedProfile == null)
            return _unboundEditors;

        var profileId = SelectedProfile.Profile.ProfileId;
        if (!_profileEditors.TryGetValue(profileId, out var editors))
        {
            editors = new Dictionary<int, KeyConfigViewModel>();
            _profileEditors[profileId] = editors;
        }

        return editors;
    }

    private void UpdateKeyboardSelection(int selectedKeyId)
    {
        foreach (var state in _keyboardKeyStates.Values)
            state.IsSelected = state.KeyId == selectedKeyId;
    }

    private void RefreshKeyState(int keyId)
    {
        if (!_keyboardKeyStates.TryGetValue(keyId, out var state))
            return;

        GetCurrentEditorCache().TryGetValue(keyId, out var editor);
        KeyConfig? savedConfig = null;
        _ = SelectedProfile?.Profile.KeyConfigs.TryGetValue(keyId, out savedConfig);
        state.RefreshState(editor, savedConfig);
    }

    public void RefreshKeyHighlights()
    {
        foreach (var keyId in _keyboardKeyStates.Keys.ToArray())
            RefreshKeyState(keyId);
    }

    private void RaiseSelectedKeyTextChanged()
    {
        OnPropertyChanged(nameof(SelectedKeyConfig));
        if (!Navigation.IsMouseActive)
            OnPropertyChanged(nameof(SelectedTriggerConfig));
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(RecordingIndicatorText));
    }

    private void RaiseRecordingStateChanged()
    {
        OnPropertyChanged(nameof(IsRecordingSessionActive));
        OnPropertyChanged(nameof(CanEditKeyboardConfiguration));
        OnPropertyChanged(nameof(CanEditTriggerConfiguration));
        OnPropertyChanged(nameof(CanEditProfileWorkspace));
        OnPropertyChanged(nameof(CanEditSelectedMacro));
        OnPropertyChanged(nameof(CanToggleRecording));
        OnPropertyChanged(nameof(RecordIconGlyph));
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(RecordingIndicatorText));
        RaiseTriggerDraftState();
        RaisePathRecordingOptions();
    }
}
