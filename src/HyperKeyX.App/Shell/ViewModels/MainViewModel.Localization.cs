using System.ComponentModel;
using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Shell.Navigation;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, Settings))
            return;

        if (e.PropertyName == nameof(SettingsViewModel.SelectedLanguageIndex))
        {
            OnPropertyChanged(nameof(SelectedLanguageIndex));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SelectedThemeIndex))
        {
            OnPropertyChanged(nameof(SelectedThemeIndex));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.IsEnglish))
        {
            OnPropertyChanged(nameof(SelectedLanguageIndex));
            OnPropertyChanged(nameof(SelectedThemeIndex));
            OnPropertyChanged(nameof(LanguageOptions));
            OnPropertyChanged(nameof(ThemeOptions));

            if (CurrentProcessName is "无" or "None")
                CurrentProcessName = IsEnglish ? "None" : "无";
            else if (CurrentProcessName is "未知" or "Unknown")
                CurrentProcessName = IsEnglish ? "Unknown" : "未知";

            var preservedTriggerIndex = SelectedTriggerIndex;
            _isUpdatingTriggerOptions = true;
            try
            {
                InitializeTriggerModes();
                SelectedTriggerIndex = preservedTriggerIndex;
            }
            finally
            {
                _isUpdatingTriggerOptions = false;
            }
            foreach (var state in _keyboardKeyStates.Values)
                state.UpdateLocalization(IsEnglish);
            foreach (var state in _mouseButtonStates.Values)
                state.UpdateLocalization(IsEnglish);
            UpdateKeyEditorLocalization();
            UpdateMouseEditorLocalization();
            MacroLibrary.UpdateLocalization(IsEnglish);
            _unavailableMacroEditor.UpdateLocalization(IsEnglish);
            _macroReferenceIndex.RebuildPublished(_publishedDocument, IsEnglish);
            foreach (var item in GetDirtyTriggerEditors())
                RefreshDraftReference(item.Identity.ProfileId, item.Editor);
            RaiseSelectedKeyTextChanged();
            RaiseSelectedMouseButtonTextChanged();
            RaiseLocalizedTextChanged();

            // 录制状态包含动态计数，切换语言时按当前快照重新格式化。
            if (IsRecordingSessionActive)
                ApplyMacroRecordingStatus(_macroRecordingService.CurrentStatus);
            else
                StatusText = GetNavigationStatusText();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.AppTheme))
        {
            OnPropertyChanged(nameof(SelectedThemeIndex));
            OnPropertyChanged(nameof(ThemeOptions));
            OnPropertyChanged(nameof(AppTheme));
        }
    }

    private void RaiseLocalizedTextChanged()
    {
        RaisePathRecordingOptions();
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(WindowTitleText));
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(RunIconGlyph));
        OnPropertyChanged(nameof(KeyboardNavigationText));
        OnPropertyChanged(nameof(MouseNavigationText));
        OnPropertyChanged(nameof(MacrosNavigationText));
        OnPropertyChanged(nameof(ProfilesText));
        OnPropertyChanged(nameof(ProfileDetailsText));
        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(SettingsTooltipText));
        OnPropertyChanged(nameof(AddProfileTooltipText));
        OnPropertyChanged(nameof(DeleteProfileTooltipText));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(ProcessPlaceholderText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(CurrentProcessText));
        OnPropertyChanged(nameof(CurrentProcessDisplayText));
        OnPropertyChanged(nameof(ActiveRuntimeProfileText));
        OnPropertyChanged(nameof(ActiveRuntimeProfileDisplayText));
        OnPropertyChanged(nameof(KeyConfigText));
        OnPropertyChanged(nameof(MouseConfigText));
        OnPropertyChanged(nameof(SelectedTriggerConfigText));
        OnPropertyChanged(nameof(TurboText));
        OnPropertyChanged(nameof(EnableTurboText));
        OnPropertyChanged(nameof(EnableAutoClickText));
        OnPropertyChanged(nameof(EnableSelectedTurboText));
        OnPropertyChanged(nameof(KeyDownDurationText));
        OnPropertyChanged(nameof(KeyUpDurationText));
        OnPropertyChanged(nameof(TriggerModeText));
        OnPropertyChanged(nameof(LongPressThresholdText));
        OnPropertyChanged(nameof(PassThroughOriginalInputText));
        OnPropertyChanged(nameof(PassThroughOriginalInputDescriptionText));
        OnPropertyChanged(nameof(MousePassThroughDescriptionText));
        OnPropertyChanged(nameof(SelectedPassThroughDescriptionText));
        OnPropertyChanged(nameof(MacroText));
        OnPropertyChanged(nameof(MacroStepsText));
        OnPropertyChanged(nameof(EnableMacroText));
        OnPropertyChanged(nameof(SelectMacroText));
        OnPropertyChanged(nameof(ClearMacroBindingText));
        OnPropertyChanged(nameof(OpenMacroText));
        OnPropertyChanged(nameof(CopyMacroText));
        OnPropertyChanged(nameof(MissingMacroReferenceText));
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(RecordingIndicatorText));
        OnPropertyChanged(nameof(NoMacroStepsText));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(SaveTriggerText));
        OnPropertyChanged(nameof(UnsavedText));
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(GlobalPreferencesText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(ThemeText));
        OnPropertyChanged(nameof(MacroLibraryTitleText));
        OnPropertyChanged(nameof(MacroLibrarySubtitleText));
        OnPropertyChanged(nameof(MacroSearchPlaceholderText));
        OnPropertyChanged(nameof(CreateMacroText));
        OnPropertyChanged(nameof(ImportMacroText));
        OnPropertyChanged(nameof(ExportMacroText));
        OnPropertyChanged(nameof(DeleteMacroText));
        OnPropertyChanged(nameof(SaveMacroText));
        OnPropertyChanged(nameof(RestoreCurrentMacroText));
        OnPropertyChanged(nameof(MacroNameText));
        OnPropertyChanged(nameof(MacroDescriptionText));
        OnPropertyChanged(nameof(MacroDescriptionPlaceholderText));
        OnPropertyChanged(nameof(MacroReferencesText));
        OnPropertyChanged(nameof(NoMacroReferencesText));
        OnPropertyChanged(nameof(NoMacroSelectedText));
        OnPropertyChanged(nameof(DeleteMacroTitleText));
        OnPropertyChanged(nameof(DeleteMacroConfirmationText));
        OnPropertyChanged(nameof(DeleteText));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(BlockedMacroDeletionText));
        OnPropertyChanged(nameof(BackToMacroListText));
        OnPropertyChanged(nameof(ExportDirtyMacroTitleText));
        OnPropertyChanged(nameof(ExportDirtyMacroText));
        OnPropertyChanged(nameof(SaveAndExportText));
        OnPropertyChanged(nameof(ExportSavedVersionText));
    }

    private string LocalizeRuntimeError(string message)
    {
        if (HyperKeyX.Application.Automation.Paths.PathRuntimeDiagnostic.TryDecode(message, out var issues))
            return string.Join("；", issues.Select(issue => HyperKeyX.Features.Automation.ViewModels.MacroValidationMessageFormatter.Format(issue, IsEnglish)));
        // 底层异常可能跟随操作系统语言；状态栏只显示稳定、可本地化的摘要。
        if (message.StartsWith("长按触发失败", StringComparison.Ordinal))
            return L("长按触发失败", "Long-press trigger failed");
        if (message.StartsWith("连发执行失败", StringComparison.Ordinal))
            return L("连发执行失败", "Turbo execution failed");
        if (message.StartsWith("宏回放失败", StringComparison.Ordinal))
            return L("宏回放失败", "Macro playback failed");
        if (message.StartsWith("释放注入按键失败", StringComparison.Ordinal))
            return L("释放注入按键失败", "Failed to release injected key");
        if (message.StartsWith("释放注入鼠标按钮失败", StringComparison.Ordinal))
            return L("释放注入鼠标按钮失败", "Failed to release injected mouse button");

        return L("运行时错误", "Runtime error");
    }

    private string LocalizeImportError(string error)
    {
        return error switch
        {
            "配置根节点为空。" => L("配置根节点为空。", "The configuration root is empty."),
            "配置中没有有效的应用配置。" => L(
                "配置中没有有效的应用配置。",
                "The configuration contains no valid profiles."),
            _ => L("导入内容不符合要求。", "The import content is invalid.")
        };
    }

    private string GetNavigationStatusText()
    {
        return Navigation.ActiveSection switch
        {
            MainSection.Mouse => MouseNavigationText,
            MainSection.Macros => MacroLibraryTitleText,
            MainSection.Profiles => ProfilesText,
            MainSection.Settings => SettingsTitleText,
            _ => L("就绪", "Ready")
        };
    }

    private string L(string chinese, string english)
    {
        return IsEnglish ? english : chinese;
    }
}
