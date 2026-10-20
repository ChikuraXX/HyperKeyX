using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Mouse.ViewModels;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private void SelectMouseButtonFromCommandParameter(object? parameter)
    {
        switch (parameter)
        {
            case MouseButtonStateViewModel state:
                SelectMouseButton(state.Button);
                break;
            case MouseButtonConfigViewModel editor:
                SelectMouseButton(editor.Button);
                break;
            case MouseButtonKind button:
                SelectMouseButton(button);
                break;
            case int value when Enum.IsDefined(typeof(MouseButtonKind), value):
                SelectMouseButton((MouseButtonKind)value);
                break;
            case string text when Enum.TryParse<MouseButtonKind>(text, true, out var parsed):
                SelectMouseButton(parsed);
                break;
        }
    }

    public void SelectMouseButton(MouseButtonKind button)
    {
        if (!InputTriggerIdentity.FromMouse(button).IsValid)
            return;

        if (IsRecordingSessionActive &&
            (_recordingOriginTrigger?.IsMouseTrigger != true ||
             SelectedMouseButton.Button != button))
        {
            _ = RejectContextChangeWhileRecording();
            return;
        }

        SelectedMouseButton = GetOrCreateMouseButtonEditor(button);
        StatusText = L(
            $"已选择鼠标按键: {SelectedMouseButton.TriggerDisplayName}",
            $"Selected mouse button: {SelectedMouseButton.TriggerDisplayName}");
    }

    public MouseButtonStateViewModel GetMouseButtonState(MouseButtonKind button)
    {
        if (!_mouseButtonStates.TryGetValue(button, out var state))
        {
            state = new MouseButtonStateViewModel(button, IsEnglish);
            _mouseButtonStates[button] = state;
        }

        state.IsSelected = SelectedMouseButton.Button == button;
        RefreshMouseButtonState(button);
        return state;
    }

    public void SaveMouseButtonConfig()
    {
        if (SaveCurrentTrigger())
        {
            RefreshMouseButtonState(SelectedMouseButton.Button);
            StatusText = L(
                $"已保存配置: {SelectedMouseButton.TriggerDisplayName}",
                $"Saved: {SelectedMouseButton.TriggerDisplayName}");
        }
    }

    public void SaveSelectedTriggerConfig()
    {
        if (Navigation.IsMouseActive)
            SaveMouseButtonConfig();
        else
            SaveKeyConfig();
    }

    private MouseButtonConfigViewModel GetOrCreateMouseButtonEditor(MouseButtonKind button)
    {
        var editors = GetCurrentMouseEditorCache();
        if (editors.TryGetValue(button, out var editor))
            return editor;

        MouseButtonConfig? config = null;
        _ = SelectedProfile?.Profile.MouseButtonConfigs.TryGetValue(button, out config);
        editor = new MouseButtonConfigViewModel(
            button,
            config,
            IsEnglish,
            MacroLibrary.PublishedChoices);
        editors[button] = editor;
        return editor;
    }

    private Dictionary<MouseButtonKind, MouseButtonConfigViewModel> GetCurrentMouseEditorCache()
    {
        if (SelectedProfile is null)
            return _unboundMouseEditors;

        var profileId = SelectedProfile.Profile.ProfileId;
        if (!_mouseProfileEditors.TryGetValue(profileId, out var editors))
        {
            editors = new Dictionary<MouseButtonKind, MouseButtonConfigViewModel>();
            _mouseProfileEditors[profileId] = editors;
        }

        return editors;
    }

    private void UpdateMouseEditorLocalization()
    {
        var updated = new HashSet<MouseButtonConfigViewModel>(
            ReferenceEqualityComparer.Instance);

        void Update(MouseButtonConfigViewModel editor)
        {
            if (updated.Add(editor))
                editor.UpdateLocalization(IsEnglish);
        }

        Update(SelectedMouseButton);
        foreach (var editor in _unboundMouseEditors.Values)
            Update(editor);
        foreach (var editors in _mouseProfileEditors.Values)
        {
            foreach (var editor in editors.Values)
                Update(editor);
        }
    }

    private void UpdateMouseButtonSelection(MouseButtonKind selectedButton)
    {
        foreach (var state in _mouseButtonStates.Values)
            state.IsSelected = state.Button == selectedButton;
    }

    private void RefreshMouseButtonState(MouseButtonKind button)
    {
        if (!_mouseButtonStates.TryGetValue(button, out var state))
            return;

        GetCurrentMouseEditorCache().TryGetValue(button, out var editor);
        MouseButtonConfig? savedConfig = null;
        _ = SelectedProfile?.Profile.MouseButtonConfigs.TryGetValue(button, out savedConfig);
        state.RefreshState(editor, savedConfig);
    }

    public void RefreshMouseButtonHighlights()
    {
        foreach (var button in _mouseButtonStates.Keys.ToArray())
            RefreshMouseButtonState(button);
    }

    partial void OnSelectedMouseButtonChanged(MouseButtonConfigViewModel value)
    {
        value.UpdateLocalization(IsEnglish);
        if (Navigation.IsMouseActive)
            ObserveSelectedTrigger(value);

        UpdateMouseButtonSelection(value.Button);
        RefreshMouseButtonState(value.Button);
        RaiseSelectedMouseButtonTextChanged();
        OnPropertyChanged(nameof(SelectedTriggerConfig));
        RaiseTriggerDraftState();
    }

    private void RaiseSelectedMouseButtonTextChanged()
    {
        if (Navigation.IsMouseActive)
            OnPropertyChanged(nameof(SelectedTriggerConfig));
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(RecordingIndicatorText));
    }
}
