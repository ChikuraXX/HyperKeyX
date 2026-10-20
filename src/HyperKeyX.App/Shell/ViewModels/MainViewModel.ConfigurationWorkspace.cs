using System.Collections.Specialized;
using System.ComponentModel;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Keyboard.ViewModels;
using HyperKeyX.Features.Macros.ViewModels;
using HyperKeyX.Features.Mouse.ViewModels;
using HyperKeyX.Shell.Navigation;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    public bool SaveCurrentTrigger()
    {
        if (_hasPendingProfileWorkspaceChanges)
        {
            StatusText = L(
                "存在未保存的配置更改，请使用“保存全部”",
                "Profile changes are pending; use Save All");
            return false;
        }

        if (PendingConfigurationImport is not null)
        {
            StatusText = L(
                "完整配置替换待保存，不能单独保存触发器",
                "Save or discard the pending replacement before saving a trigger");
            return false;
        }

        if (RejectContextChangeWhileRecording() || SelectedProfile is null)
            return false;

        var editor = SelectedTriggerConfig;
        if (!editor.IsDirty)
            return true;

        if (!editor.CanSave)
        {
            NavigateToTrigger(SelectedProfile.Profile.ProfileId, editor);
            StatusText = L(
                "启用宏前必须选择一个已保存的宏",
                "Select a saved macro before enabling it");
            return false;
        }

        var identity = new TriggerDraftIdentity(
            SelectedProfile.Profile.ProfileId,
            editor.TriggerIdentity);
        var request = new ConfigurationSaveRequest(
            CreateWorkspaceDocument(),
            [],
            CreateTriggerSnapshots(
                new HashSet<TriggerDraftIdentity> { identity },
                includePendingBindings: false));
        var saved = _configurationSaveCoordinator.Save(request);
        if (!saved.Succeeded)
        {
            StatusText = L("保存触发器失败", "Failed to save the trigger");
            RaiseWorkspaceState();
            return false;
        }

        ApplySavePlan(saved.Value);
        StatusText = L("触发器已保存", "Trigger saved");
        return true;
    }

    public bool SaveCurrentMacro()
    {
        if (_hasPendingProfileWorkspaceChanges)
        {
            StatusText = L(
                "存在未保存的配置更改，请使用“保存全部”",
                "Profile changes are pending; use Save All");
            return false;
        }

        if (PendingConfigurationImport is not null)
        {
            StatusText = L(
                "完整配置替换待保存，不能单独保存宏",
                "Save or discard the pending replacement before saving a macro");
            return false;
        }

        var macro = MacroLibrary.SelectedMacro;
        if (macro is null)
            return false;
        if (!macro.IsDirty)
            return true;
        if (!EnsureMacroCanSave(macro))
            return false;

        var request = new ConfigurationSaveRequest(
            CreateWorkspaceDocument(),
            [CreateMacroChange(macro)],
            CreateTriggerSnapshots(
                new HashSet<TriggerDraftIdentity>(),
                includePendingBindings: false));
        var saved = _configurationSaveCoordinator.Save(request);
        if (!saved.Succeeded)
        {
            StatusText = L("保存宏失败", "Failed to save the macro");
            RaiseWorkspaceState();
            return false;
        }

        ApplySavePlan(saved.Value);

        // 单独保存复制出的宏后才把新 ID 写入触发器草稿，原引用此前始终不变。
        var pending = _pendingMacroBindingIntents.FirstOrDefault(
            intent => intent.MacroId == macro.MacroId);
        if (pending is not null && TryGetTriggerEditor(
                new TriggerDraftIdentity(pending.ProfileId, pending.Trigger),
                out var editor))
        {
            _pendingMacroBindingIntents.Remove(pending);
            editor.SelectedMacroId = pending.MacroId;
            RefreshDraftReference(pending.ProfileId, editor);
        }

        RaiseWorkspaceState();
        StatusText = L("宏已保存", "Macro saved");
        return true;
    }

    public bool SaveAllChanges()
    {
        var isConfirmedReplacement = PendingConfigurationImport is not null;
        var macroChanges = new List<MacroDraftChange>();
        foreach (var macro in MacroLibrary.Macros.Where(macro => macro.IsDirty))
        {
            if (!EnsureMacroCanSave(macro))
                return false;
            macroChanges.Add(CreateMacroChange(macro));
        }

        var dirtyIdentities = GetDirtyTriggerEditors()
            .Select(item => item.Identity)
            .ToHashSet();
        foreach (var pending in _pendingMacroBindingIntents)
        {
            dirtyIdentities.Add(new TriggerDraftIdentity(
                pending.ProfileId,
                pending.Trigger));
        }

        foreach (var item in GetDirtyTriggerEditors())
        {
            var hasPendingOverride = _pendingMacroBindingIntents.Any(intent =>
                intent.ProfileId == item.Identity.ProfileId &&
                intent.Trigger == item.Identity.Trigger);
            if (!hasPendingOverride && !item.Editor.CanSave)
            {
                NavigateToTrigger(item.Identity.ProfileId, item.Editor);
                StatusText = L(
                    "存在未选择有效宏的触发器",
                    "A trigger does not have a valid macro selection");
                return false;
            }
        }

        if (!isConfirmedReplacement &&
            macroChanges.Count == 0 &&
            dirtyIdentities.Count == 0 &&
            !_hasPendingProfileWorkspaceChanges)
        {
            return true;
        }

        var request = new ConfigurationSaveRequest(
            CreateWorkspaceDocument(),
            macroChanges,
            CreateTriggerSnapshots(
                dirtyIdentities,
                includePendingBindings: true),
            isConfirmedReplacement
                ? ConfigurationWriteIntent.ConfirmedReplacement
                : ConfigurationWriteIntent.Ordinary);
        var saved = _configurationSaveCoordinator.Save(request);
        if (!saved.Succeeded)
        {
            StatusText = L("保存全部更改失败", "Failed to save all changes");
            RaiseWorkspaceState();
            return false;
        }

        _pendingConfigurationImport = null;
        _hasPendingProfileWorkspaceChanges = false;
        ApplySavePlan(saved.Value);
        _pendingMacroBindingIntents.RemoveAll(intent =>
            saved.Value.AcceptedMacroIds.Contains(intent.MacroId) &&
            saved.Value.AcceptedTriggerIds.Contains(new TriggerDraftIdentity(
                intent.ProfileId,
                intent.Trigger)));
        RaiseWorkspaceState();
        OnPropertyChanged(nameof(PendingConfigurationImport));
        StatusText = L("已保存全部更改", "All changes saved");
        return true;
    }

    public MacroDefinitionViewModel? CopySelectedTriggerMacro()
    {
        if (SelectedProfile is null ||
            SelectedTriggerConfig.SelectedMacroId is not Guid sourceId)
        {
            return null;
        }

        var source = MacroLibrary.Macros.FirstOrDefault(macro =>
            macro.MacroId == sourceId && !macro.IsDeleted && !macro.IsNew);
        if (source is null)
            return null;

        var copy = MacroLibrary.CopyMacro(source);
        var identity = new TriggerDraftIdentity(
            SelectedProfile.Profile.ProfileId,
            SelectedTriggerConfig.TriggerIdentity);
        _pendingMacroBindingIntents.RemoveAll(intent =>
            intent.ProfileId == identity.ProfileId &&
            intent.Trigger == identity.Trigger);
        _pendingMacroBindingIntents.Add(new PendingMacroBindingIntent(
            copy.MacroId,
            identity.ProfileId,
            identity.Trigger));
        RaiseWorkspaceState();
        return copy;
    }

    private bool SaveProfileWorkspace()
    {
        if (PendingConfigurationImport is not null)
            return false;

        var request = new ConfigurationSaveRequest(
            CreateWorkspaceDocument(),
            [],
            CreateTriggerSnapshots(
                new HashSet<TriggerDraftIdentity>(),
                includePendingBindings: false));
        var saved = _configurationSaveCoordinator.Save(request);
        if (!saved.Succeeded)
        {
            RaiseWorkspaceState();
            return false;
        }

        _hasPendingProfileWorkspaceChanges = false;
        ApplySavePlan(saved.Value);
        return true;
    }

    private AutomationConfigurationDocument CreateWorkspaceDocument() => new()
    {
        SchemaVersion = GetWorkspaceBaseline().SchemaVersion,
        Macros = GetWorkspaceBaseline().Macros
            .Select(macro => macro.DeepClone())
            .ToList(),
        Profiles = _profiles
            .Select(profile => profile.DeepClone())
            .ToList()
    };

    private AutomationConfigurationDocument GetWorkspaceBaseline() =>
        PendingConfigurationImport?.CreateCandidateSnapshot() ??
        _publishedDocument;

    private IReadOnlyList<TriggerDraftSnapshot> CreateTriggerSnapshots(
        IReadOnlySet<TriggerDraftIdentity> included,
        bool includePendingBindings)
    {
        var snapshots = GetDirtyTriggerEditors()
            .ToDictionary(
                item => item.Identity,
                item => new TriggerDraftSnapshot(
                    item.Identity.ProfileId,
                    item.Identity.Trigger,
                    CreateTriggerConfig(item.Editor),
                    included.Contains(item.Identity)));

        if (includePendingBindings)
        {
            foreach (var pending in _pendingMacroBindingIntents)
            {
                var identity = new TriggerDraftIdentity(
                    pending.ProfileId,
                    pending.Trigger);
                if (!TryGetTriggerEditor(identity, out var editor))
                    continue;

                var config = CreateTriggerConfig(editor);
                config.MacroId = pending.MacroId;
                snapshots[identity] = new TriggerDraftSnapshot(
                    identity.ProfileId,
                    identity.Trigger,
                    config,
                    isIncludedInSave: true);
            }
        }

        return snapshots.Values.ToArray();
    }

    private IReadOnlyList<TriggerEditorEntry> GetDirtyTriggerEditors()
    {
        var entries = new List<TriggerEditorEntry>();
        foreach (var profile in _profileEditors)
        {
            entries.AddRange(profile.Value.Values
                .Where(editor => editor.IsDirty)
                .Select(editor => new TriggerEditorEntry(
                    new TriggerDraftIdentity(profile.Key, editor.TriggerIdentity),
                    editor)));
        }

        foreach (var profile in _mouseProfileEditors)
        {
            entries.AddRange(profile.Value.Values
                .Where(editor => editor.IsDirty)
                .Select(editor => new TriggerEditorEntry(
                    new TriggerDraftIdentity(profile.Key, editor.TriggerIdentity),
                    editor)));
        }

        return entries;
    }

    private static TriggerBehaviorConfig CreateTriggerConfig(
        TriggerConfigViewModel editor) => editor switch
    {
        KeyConfigViewModel key => key.ToConfig(),
        MouseButtonConfigViewModel mouse => mouse.ToConfig(),
        _ => throw new InvalidOperationException("Unsupported trigger editor.")
    };

    private static MacroDraftChange CreateMacroChange(
        MacroDefinitionViewModel macro) => macro.IsDeleted
        ? MacroDraftChange.Delete(macro.MacroId)
        : MacroDraftChange.Upsert(macro.ToModel());

    private bool EnsureMacroCanSave(MacroDefinitionViewModel macro)
    {
        if (macro.CanSave)
            return true;

        MacroLibrary.SelectedMacro = macro;
        macro.Editor.FocusFirstIssue();
        StatusText = L(
            "宏存在错误，保存已阻止",
            "The macro contains errors and could not be saved");
        RaiseWorkspaceState();
        return false;
    }

    private void ApplySavePlan(ConfigurationSavePlan plan)
    {
        _publishedDocument = plan.Candidate.DeepClone();

        // 先接受宏基线，使新宏进入可选列表，再接受引用它的触发器基线。
        foreach (var macroId in plan.AcceptedMacroIds)
        {
            var saved = _publishedDocument.Macros.FirstOrDefault(
                macro => macro.MacroId == macroId);
            if (saved is null)
                MacroLibrary.CommitMacroDeletion(macroId);
            else
                MacroLibrary.MarkMacroSaved(macroId, saved);
        }

        foreach (var identity in plan.AcceptedTriggerIds)
        {
            if (!TryGetTriggerEditor(identity, out var editor) ||
                !TryGetPublishedTrigger(identity, out var config))
            {
                continue;
            }

            editor.SelectedMacroId = config.MacroId;
            editor.MarkSaved();
            CopyPublishedTriggerToWorkspace(identity, config);
            _macroReferenceIndex.RemoveDraftReference(
                identity.ProfileId,
                identity.Trigger);
        }

        _macroReferenceIndex.RebuildPublished(_publishedDocument, IsEnglish);
        RefreshKeyHighlights();
        RefreshMouseButtonHighlights();
        RaiseWorkspaceState();
    }

    private bool TryGetPublishedTrigger(
        TriggerDraftIdentity identity,
        out TriggerBehaviorConfig config)
    {
        config = null!;
        var profile = _publishedDocument.Profiles.FirstOrDefault(
            candidate => candidate.ProfileId == identity.ProfileId);
        if (profile is null)
            return false;

        if (identity.Trigger.Kind == InputTriggerKind.Keyboard &&
            profile.KeyConfigs.TryGetValue(identity.Trigger.Code, out var key))
        {
            config = key;
            return true;
        }

        if (identity.Trigger.Kind == InputTriggerKind.MouseButton &&
            profile.MouseButtonConfigs.TryGetValue(
                (MouseButtonKind)identity.Trigger.Code,
                out var mouse))
        {
            config = mouse;
            return true;
        }

        return false;
    }

    private void CopyPublishedTriggerToWorkspace(
        TriggerDraftIdentity identity,
        TriggerBehaviorConfig config)
    {
        var profile = _profiles.First(candidate =>
            candidate.ProfileId == identity.ProfileId);
        if (identity.Trigger.Kind == InputTriggerKind.Keyboard &&
            config is KeyConfig key)
        {
            profile.KeyConfigs[identity.Trigger.Code] = key.DeepClone();
        }
        else if (identity.Trigger.Kind == InputTriggerKind.MouseButton &&
                 config is MouseButtonConfig mouse)
        {
            profile.MouseButtonConfigs[(MouseButtonKind)identity.Trigger.Code] =
                mouse.DeepClone();
        }
    }

    private bool TryGetTriggerEditor(
        TriggerDraftIdentity identity,
        out TriggerConfigViewModel editor)
    {
        editor = null!;
        if (identity.Trigger.Kind == InputTriggerKind.Keyboard &&
            _profileEditors.TryGetValue(identity.ProfileId, out var keys) &&
            keys.TryGetValue(identity.Trigger.Code, out var key))
        {
            editor = key;
            return true;
        }

        if (identity.Trigger.Kind == InputTriggerKind.MouseButton &&
            _mouseProfileEditors.TryGetValue(identity.ProfileId, out var mouseButtons) &&
            mouseButtons.TryGetValue(
                (MouseButtonKind)identity.Trigger.Code,
                out var mouse))
        {
            editor = mouse;
            return true;
        }

        return false;
    }

    private void NavigateToTrigger(Guid profileId, TriggerConfigViewModel editor)
    {
        SelectedProfile = Profiles.FirstOrDefault(profile =>
            profile.Profile.ProfileId == profileId);
        if (editor is KeyConfigViewModel key)
        {
            Navigation.ActiveSection = MainSection.Keyboard;
            SelectKey(key.KeyId);
        }
        else if (editor is MouseButtonConfigViewModel mouse)
        {
            Navigation.ActiveSection = MainSection.Mouse;
            SelectMouseButton(mouse.Button);
        }
    }

    private void OnMacroLibraryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MacroLibraryViewModel.SelectedMacro))
        {
            OnPropertyChanged(nameof(SelectedMacroEditor));
            OnPropertyChanged(nameof(HasSelectedMacro));
            OnPropertyChanged(nameof(CanEditSelectedMacro));
            OnPropertyChanged(nameof(CanToggleRecording));
        }

        if (args.PropertyName is nameof(MacroLibraryViewModel.HasDirtyDrafts) or
            nameof(MacroLibraryViewModel.PendingDeletions))
        {
            RaiseWorkspaceState();
        }
    }

    private void OnMacroCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        var liveIds = MacroLibrary.Macros
            .Select(macro => macro.MacroId)
            .ToHashSet();
        _pendingMacroBindingIntents.RemoveAll(intent =>
            !liveIds.Contains(intent.MacroId));
        RaiseWorkspaceState();
    }

    private void RefreshDraftReference(
        Guid profileId,
        TriggerConfigViewModel editor)
    {
        if (!editor.IsDirty)
        {
            _macroReferenceIndex.RemoveDraftReference(
                profileId,
                editor.TriggerIdentity);
            return;
        }

        var profileName = Profiles.FirstOrDefault(profile =>
            profile.Profile.ProfileId == profileId)?.DisplayName ?? string.Empty;
        _macroReferenceIndex.UpsertDraftReference(
            profileId,
            profileName,
            editor.TriggerIdentity,
            editor.TriggerDisplayName,
            editor.SelectedMacroId);
    }

    private void RaiseWorkspaceState()
    {
        RaisePathRecordingOptions();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSaveSelectedTriggerConfig));
        OnPropertyChanged(nameof(PendingMacroBindingIntents));
        OnPropertyChanged(nameof(PreparedConfigurationImportSummary));
        OnPropertyChanged(nameof(PendingConfigurationImport));
        OnPropertyChanged(nameof(CanToggleRecording));
        OnPropertyChanged(nameof(CanEditSelectedMacro));
        OnPropertyChanged(nameof(CanEditProfileWorkspace));
        OnPropertyChanged(nameof(CanEditTargetProcess));
        OnPropertyChanged(nameof(CanDeleteSelectedProfile));
    }

    private void DisposeProfileEditors(Guid profileId)
    {
        if (_profileEditors.Remove(profileId, out var keys))
        {
            foreach (var editor in keys.Values)
            {
                _macroReferenceIndex.RemoveDraftReference(
                    profileId,
                    editor.TriggerIdentity);
                editor.Dispose();
            }
        }

        if (_mouseProfileEditors.Remove(profileId, out var mouseButtons))
        {
            foreach (var editor in mouseButtons.Values)
            {
                _macroReferenceIndex.RemoveDraftReference(
                    profileId,
                    editor.TriggerIdentity);
                editor.Dispose();
            }
        }

        // 触发器随配置文件消失后，延迟绑定不再有合法目标，但复制宏草稿仍保留。
        _pendingMacroBindingIntents.RemoveAll(intent =>
            intent.ProfileId == profileId);
    }

    private void DisposeAllTriggerEditors()
    {
        var editors = new HashSet<TriggerConfigViewModel>(
            ReferenceEqualityComparer.Instance);
        foreach (var cache in _profileEditors.Values)
            editors.UnionWith(cache.Values);
        foreach (var cache in _mouseProfileEditors.Values)
            editors.UnionWith(cache.Values);
        editors.UnionWith(_unboundEditors.Values);
        editors.UnionWith(_unboundMouseEditors.Values);
        editors.Add(SelectedKey);
        editors.Add(SelectedMouseButton);

        foreach (var editor in editors)
            editor.Dispose();

        _profileEditors.Clear();
        _mouseProfileEditors.Clear();
        _unboundEditors.Clear();
        _unboundMouseEditors.Clear();
    }

    private sealed record TriggerEditorEntry(
        TriggerDraftIdentity Identity,
        TriggerConfigViewModel Editor);
}
