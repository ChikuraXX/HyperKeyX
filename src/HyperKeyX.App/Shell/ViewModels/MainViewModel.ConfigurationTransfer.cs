using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Macros.Services;
using HyperKeyX.Features.Macros.ViewModels;
using HyperKeyX.Features.Profiles.ViewModels;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    public async Task<OperationResult<ConfigurationImportSummary>>
        PrepareConfigurationImportAsync(
            Stream source,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        // 每次解析都是独立尝试；失败结果绝不能留下上一次可被误确认的候选。
        CancelPreparedConfigurationImport();
        if (HasUnsavedChanges)
        {
            return OperationResult<ConfigurationImportSummary>.Failure(
                "Unsaved workspace changes must be resolved before importing.");
        }

        var imported = await _configurationTransferService.ImportAsync(
            source,
            cancellationToken);
        if (!imported.Succeeded)
        {
            return OperationResult<ConfigurationImportSummary>.Failure(
                imported.ErrorMessage ?? "The configuration could not be imported.");
        }

        var candidate = imported.Value.Document.DeepClone();
        if (!AutomationConfigurationValidator.Validate(candidate).IsValid)
        {
            return OperationResult<ConfigurationImportSummary>.Failure(
                "The imported configuration is invalid.");
        }

        var summary = CreateImportSummary(
            candidate,
            imported.Value.WasMigrated);
        _preparedConfigurationImport = new PreparedConfigurationImport(
            candidate,
            summary);
        OnPropertyChanged(nameof(PreparedConfigurationImportSummary));
        return OperationResult<ConfigurationImportSummary>.Success(summary);
    }

    public bool ConfirmPreparedConfigurationImport()
    {
        if (_preparedConfigurationImport is null ||
            PendingConfigurationImport is not null ||
            HasUnsavedChanges)
        {
            return false;
        }

        var candidate = _preparedConfigurationImport.Candidate.DeepClone();
        _pendingConfigurationImport = new PendingConfigurationImport(
            candidate,
            _publishedDocument,
            SelectedProfile?.Profile.ProfileId,
            MacroLibrary.SelectedMacro?.MacroId);
        _preparedConfigurationImport = null;

        // 确认仅把替换内容装入编辑工作区；磁盘与运行时必须等到“保存全部”成功。
        LoadWorkspaceFromDocument(candidate, preferredProfileId: null, preferredMacroId: null);
        RaiseWorkspaceState();
        OnPropertyChanged(nameof(PreparedConfigurationImportSummary));
        OnPropertyChanged(nameof(PendingConfigurationImport));
        return true;
    }

    public async Task<bool> ConfirmPreparedConfigurationImportWithPromptAsync()
    {
        var summary = PreparedConfigurationImportSummary;
        if (summary is null)
            return false;

        var confirmed = await _configurationImportPrompt.ShowAsync(summary, IsEnglish);
        if (!confirmed)
        {
            CancelPreparedConfigurationImport();
            return false;
        }

        // 对话框确认只授权装入替换草稿，真正发布仍必须经过后续“保存全部”事务。
        return ConfirmPreparedConfigurationImport();
    }

    public void CancelPreparedConfigurationImport()
    {
        if (_preparedConfigurationImport is null)
            return;

        _preparedConfigurationImport = null;
        OnPropertyChanged(nameof(PreparedConfigurationImportSummary));
    }

    public bool DiscardPendingConfigurationImport()
    {
        var pending = _pendingConfigurationImport;
        if (pending is null)
            return false;

        var previous = pending.CreatePreviousSnapshot();
        _pendingConfigurationImport = null;
        _pendingMacroBindingIntents.Clear();

        // 放弃暂存替换时按原选择重建草稿，已发布文档和运行时从未被替换。
        LoadWorkspaceFromDocument(
            previous,
            pending.SelectedProfileId,
            pending.SelectedMacroId);
        RaiseWorkspaceState();
        OnPropertyChanged(nameof(PendingConfigurationImport));
        return true;
    }

    public Task<OperationResult<int>> ExportPublishedConfigurationAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return _configurationTransferService.ExportAsync(
            destination,
            _publishedDocument.DeepClone(),
            cancellationToken);
    }

    public async Task<OperationResult<MacroDefinitionViewModel>>
        ImportMacroPackageAsync(
            Stream source,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (PendingConfigurationImport is not null)
        {
            return OperationResult<MacroDefinitionViewModel>.Failure(
                "A full configuration replacement is pending.");
        }

        var imported = await _macroPackageTransferService.ImportAsync(
            source,
            _publishedDocument.Macros,
            cancellationToken);
        if (!imported.Succeeded)
        {
            return OperationResult<MacroDefinitionViewModel>.Failure(
                imported.ErrorMessage ?? "The macro package could not be imported.");
        }

        var draft = MacroLibrary.ImportMacro(imported.Value);
        RaiseWorkspaceState();
        return OperationResult<MacroDefinitionViewModel>.Success(draft);
    }

    public async Task<OperationResult> ExportSelectedMacroAsync(
        DirtyMacroExportDecision decision,
        Func<CancellationToken, Task<Stream?>> openDestinationAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openDestinationAsync);
        if (!Enum.IsDefined(decision))
            return OperationResult.Failure("The macro export decision is invalid.");
        if (decision == DirtyMacroExportDecision.Cancel)
            return OperationResult.Failure("Macro export was cancelled.");
        if (PendingConfigurationImport is not null)
        {
            return OperationResult.Failure(
                "A full configuration replacement is pending.");
        }

        var selected = MacroLibrary.SelectedMacro;
        if (selected is null)
            return OperationResult.Failure("No macro is selected.");

        // 先取得用户选择，取消不能隐式保存草稿。公开失败契约保持不变。
        var destination = await openDestinationAsync(cancellationToken);
        if (destination is null)
            return OperationResult.Failure("Macro export was cancelled.");

        await using (destination)
        {
            if (!ReferenceEquals(selected, MacroLibrary.SelectedMacro) || PendingConfigurationImport is not null)
                return OperationResult.Failure("The export selection changed before the destination was ready.");
            if (decision == DirtyMacroExportDecision.SaveThenExport && selected.IsDirty)
            {
                if (selected.IsDeleted)
                    return OperationResult.Failure("A deleted macro cannot be exported after saving.");

                // 草稿必须先通过完整文档事务，导出内容才可成为新的已发布基线。
                if (!SaveCurrentMacro())
                    return OperationResult.Failure("The macro could not be saved before export.");
            }

            // 即使当前草稿无效，“导出已保存版本”也只读取最近一次成功保存的深复制基线。
            var macro = selected.GetSavedModel();
            if (macro is null)
            {
                return OperationResult.Failure(
                    "This macro has no saved version to export.");
            }

            return await _macroPackageTransferService.ExportAsync(
                destination,
                macro,
                cancellationToken);
        }
    }

    private void LoadWorkspaceFromDocument(
        AutomationConfigurationDocument document,
        Guid? preferredProfileId,
        Guid? preferredMacroId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var keyId = SelectedKey.KeyId > 0 ? SelectedKey.KeyId : DefaultKeyId;
        var mouseButton = SelectedMouseButton.Button;

        DisposeAllTriggerEditors();
        _hasPendingProfileWorkspaceChanges = false;
        _macroReferenceIndex.ClearDraftReferences();
        _macroReferenceIndex.RebuildPublished(document, IsEnglish);
        ReplaceMacroLibrary(document.Macros, preferredMacroId);

        _profiles = document.Profiles
            .Select(profile => profile.DeepClone())
            .ToList();
        Profiles.Clear();
        foreach (var profile in _profiles)
            Profiles.Add(new AppProfileViewModel(profile));

        SelectedProfile = preferredProfileId is Guid profileId
            ? Profiles.FirstOrDefault(profile => profile.Profile.ProfileId == profileId)
            : null;
        SelectedProfile ??= Profiles.FirstOrDefault(profile => profile.IsGlobal)
            ?? Profiles.FirstOrDefault();

        InitializeProcessList();
        SelectKey(keyId);
        SelectMouseButton(mouseButton);

        // 触发器重建会同步宏选择，因此必须在最后恢复用户进入导入流程前的宏位置。
        if (preferredMacroId is Guid macroId)
        {
            MacroLibrary.SelectedMacro = MacroLibrary.Macros.FirstOrDefault(
                macro => macro.MacroId == macroId);
        }

        RefreshKeyHighlights();
        RefreshMouseButtonHighlights();
    }

    private void ReplaceMacroLibrary(
        IEnumerable<MacroDefinition> macros,
        Guid? preferredMacroId)
    {
        var previousLibrary = MacroLibrary;
        MacroLibrary.PropertyChanged -= OnMacroLibraryPropertyChanged;
        MacroLibrary.Macros.CollectionChanged -= OnMacroCollectionChanged;

        MacroLibrary = new MacroLibraryViewModel(
            macros,
            _macroReferenceIndex,
            IsEnglish);
        MacroLibrary.PropertyChanged += OnMacroLibraryPropertyChanged;
        MacroLibrary.Macros.CollectionChanged += OnMacroCollectionChanged;
        previousLibrary.Dispose();
        if (preferredMacroId is Guid macroId)
        {
            MacroLibrary.SelectedMacro = MacroLibrary.Macros.FirstOrDefault(
                macro => macro.MacroId == macroId);
        }

        OnPropertyChanged(nameof(MacroLibrary));
        OnPropertyChanged(nameof(SelectedMacroEditor));
        OnPropertyChanged(nameof(HasSelectedMacro));
        OnPropertyChanged(nameof(CanEditSelectedMacro));
    }

    private static ConfigurationImportSummary CreateImportSummary(
        AutomationConfigurationDocument document,
        bool wasMigrated) => new(
        document.Profiles.Count,
        document.Macros.Count,
        document.Profiles.Sum(profile => profile.KeyConfigs.Values.Count(config =>
            config.MacroId is Guid)),
        document.Profiles.Sum(profile => profile.MouseButtonConfigs.Values.Count(config =>
            config.MacroId is Guid)),
        wasMigrated);

    private sealed record PreparedConfigurationImport(
        AutomationConfigurationDocument Candidate,
        ConfigurationImportSummary Summary);
}
