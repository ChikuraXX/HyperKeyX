using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Features.Macros.Services;

namespace HyperKeyX.Features.Macros.ViewModels;

/// <summary>
/// 管理宏库草稿、搜索结果、发布选择投影和删除保护。
/// </summary>
public sealed class MacroLibraryViewModel : ObservableObject, IDisposable
{
    private readonly MacroReferenceIndex _referenceIndex;
    private string _searchText = string.Empty;
    private MacroDefinitionViewModel? _selectedMacro;
    private bool _isEnglish;
    private bool _disposed;

    public MacroLibraryViewModel(
        IEnumerable<MacroDefinition> publishedMacros,
        MacroReferenceIndex? referenceIndex = null,
        bool isEnglish = false)
    {
        ArgumentNullException.ThrowIfNull(publishedMacros);
        _referenceIndex = referenceIndex ?? new MacroReferenceIndex();
        _referenceIndex.Changed += OnReferenceIndexChanged;
        _isEnglish = isEnglish;

        foreach (var macro in publishedMacros.Where(macro => macro is not null))
            AddDraft(new MacroDefinitionViewModel(macro, isNew: false, isEnglish));

        CreateCommand = new RelayCommand(() => CreateMacro());
        CopyCommand = new RelayCommand<MacroDefinitionViewModel?>(macro =>
        {
            if (macro is not null)
                CopyMacro(macro);
        });
        ImportCommand = new RelayCommand<MacroPackageImportResult?>(result =>
        {
            if (result is not null)
                ImportMacro(result);
        });
        DeleteCommand = new RelayCommand<MacroDefinitionViewModel?>(macro =>
        {
            if (macro is not null)
                RequestDelete(macro);
        });

        RefreshNameConflicts();
        RefreshFilter();
        RefreshPublishedChoices();
    }

    public ObservableCollection<MacroDefinitionViewModel> Macros { get; } = [];
    public ObservableCollection<MacroDefinitionViewModel> FilteredMacros { get; } = [];
    public ObservableCollection<MacroChoiceViewModel> PublishedChoices { get; } = [];
    public ObservableCollection<MacroReferenceViewModel> BlockingReferences { get; } = [];
    public ObservableCollection<MacroReferenceViewModel> SelectedMacroReferences { get; } = [];

    public IRelayCommand CreateCommand { get; }
    public IRelayCommand<MacroDefinitionViewModel?> CopyCommand { get; }
    public IRelayCommand<MacroPackageImportResult?> ImportCommand { get; }
    public IRelayCommand<MacroDefinitionViewModel?> DeleteCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                RefreshFilter();
        }
    }

    public MacroDefinitionViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (SetProperty(ref _selectedMacro, value))
            {
                BlockingReferences.Clear();
                RefreshSelectedMacroReferences();
            }
        }
    }

    public bool HasDirtyDrafts => Macros.Any(macro => macro.IsDirty);
    public IReadOnlyList<MacroDefinitionViewModel> PendingDeletions =>
        Macros.Where(macro => macro.IsDeleted).ToArray();

    public MacroDefinitionViewModel CreateMacro(string? requestedName = null)
    {
        var macro = new MacroDefinition
        {
            MacroId = CreateUniqueId(),
            Name = AllocateName(requestedName ?? L("新建宏", "New macro")),
            Steps = []
        };
        var draft = new MacroDefinitionViewModel(macro, isNew: true, _isEnglish);
        AddDraft(draft);
        RefreshAll(draft);
        return draft;
    }

    public MacroDefinitionViewModel CopyMacro(MacroDefinitionViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var model = source.ToModel();
        model.MacroId = CreateUniqueId();
        model.Name = AllocateName(model.Name);
        var draft = new MacroDefinitionViewModel(model, isNew: true, _isEnglish);
        draft.Editor.CopyRecordingDiagnosticsFrom(source.Editor);
        AddDraft(draft);
        RefreshAll(draft);
        return draft;
    }

    public MacroDefinitionViewModel ImportMacro(MacroDefinition imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        var model = imported.DeepClone();
        if (model.MacroId == Guid.Empty ||
            Macros.Any(macro => macro.MacroId == model.MacroId))
        {
            model.MacroId = CreateUniqueId();
        }

        model.Name = AllocateName(model.Name);
        var draft = new MacroDefinitionViewModel(model, isNew: true, _isEnglish);
        AddDraft(draft);
        RefreshAll(draft);
        return draft;
    }

    public MacroDefinitionViewModel ImportMacro(MacroPackageImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ReusedExisting)
        {
            var existingId = result.ExistingMacroId ?? result.Macro.MacroId;
            var existing = Macros.FirstOrDefault(macro =>
                macro.MacroId == existingId && !macro.IsDeleted);
            if (existing is not null)
            {
                SelectedMacro = existing;
                return existing;
            }
        }

        return ImportMacro(result.Macro);
    }

    public bool MarkMacroSaved(Guid macroId, MacroDefinition saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var draft = Macros.FirstOrDefault(macro => macro.MacroId == macroId);
        if (draft is null || saved.MacroId != macroId)
            return false;

        draft.MarkSaved(saved);
        RefreshPublishedChoices();
        OnPropertyChanged(nameof(HasDirtyDrafts));
        OnPropertyChanged(nameof(PendingDeletions));
        return true;
    }

    public bool RequestDelete(MacroDefinitionViewModel macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (!Macros.Contains(macro))
            return false;

        BlockingReferences.Clear();
        foreach (var reference in _referenceIndex.GetReferences(macro.MacroId))
            BlockingReferences.Add(reference);
        OnPropertyChanged(nameof(BlockingReferences));

        if (BlockingReferences.Count > 0)
            return false;

        if (macro.IsNew)
        {
            RemoveDraft(macro);
        }
        else
        {
            macro.MarkDeleted();
            RefreshAll(macro);
        }

        return true;
    }

    public IReadOnlyList<MacroReferenceViewModel> GetReferences(
        MacroDefinitionViewModel macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        return Macros.Contains(macro)
            ? _referenceIndex.GetReferences(macro.MacroId)
            : [];
    }

    public void RefreshReferences()
    {
        foreach (var macro in Macros)
            macro.UpdateReferenceCount(_referenceIndex.GetReferenceCount(macro.MacroId));

        RefreshSelectedMacroReferences();
    }

    public bool RestoreMacroDeletion(Guid macroId)
    {
        var macro = Macros.FirstOrDefault(item => item.MacroId == macroId);
        if (macro is null || !macro.IsDeleted)
            return false;

        macro.RestoreDeletion();
        RefreshAll(macro);
        return true;
    }

    public bool CommitMacroDeletion(Guid macroId)
    {
        var macro = Macros.FirstOrDefault(item =>
            item.MacroId == macroId && item.IsDeleted);
        if (macro is null)
            return false;

        RemoveDraft(macro);
        return true;
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        foreach (var macro in Macros)
            macro.UpdateLocalization(isEnglish);
        RefreshAll(SelectedMacro);
    }

    private void AddDraft(MacroDefinitionViewModel draft)
    {
        draft.PropertyChanged += OnDraftPropertyChanged;
        draft.UpdateReferenceCount(_referenceIndex.GetReferenceCount(draft.MacroId));
        Macros.Add(draft);
    }

    private void RemoveDraft(MacroDefinitionViewModel draft)
    {
        draft.PropertyChanged -= OnDraftPropertyChanged;
        Macros.Remove(draft);
        RefreshAll(SelectedMacro == draft ? null : SelectedMacro);
    }

    private void OnDraftPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MacroDefinitionViewModel.Name) or
            nameof(MacroDefinitionViewModel.Description))
        {
            if (args.PropertyName == nameof(MacroDefinitionViewModel.Name))
                RefreshNameConflicts();
            RefreshFilter();
        }

        if (args.PropertyName is nameof(MacroDefinitionViewModel.IsNew) or
            nameof(MacroDefinitionViewModel.IsDeleted))
        {
            RefreshNameConflicts();
            RefreshPublishedChoices();
        }

        if (args.PropertyName is nameof(MacroDefinitionViewModel.IsDirty) or
            nameof(MacroDefinitionViewModel.IsNew) or
            nameof(MacroDefinitionViewModel.IsDeleted))
        {
            OnPropertyChanged(nameof(HasDirtyDrafts));
            OnPropertyChanged(nameof(PendingDeletions));
        }
    }

    private void RefreshAll(MacroDefinitionViewModel? preferredSelection)
    {
        RefreshNameConflicts();
        RefreshFilter(preferredSelection);
        RefreshPublishedChoices();
        RefreshReferences();
        OnPropertyChanged(nameof(HasDirtyDrafts));
        OnPropertyChanged(nameof(PendingDeletions));
    }

    private void RefreshFilter(MacroDefinitionViewModel? preferredSelection = null)
    {
        var previous = preferredSelection ?? SelectedMacro;
        var query = SearchText.Trim();
        var matches = Macros
            .Where(macro => query.Length == 0 ||
                macro.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                macro.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(macro => macro.MacroId)
            .ToArray();

        FilteredMacros.Clear();
        foreach (var macro in matches)
            FilteredMacros.Add(macro);

        SelectedMacro = previous is not null && matches.Contains(previous)
            ? previous
            : matches.FirstOrDefault();
    }

    private void RefreshPublishedChoices()
    {
        var choices = Macros
            .Where(macro => !macro.IsNew && !macro.IsDeleted)
            .Select(macro => new
            {
                Draft = macro,
                Saved = macro.GetSavedModel()
            })
            .Where(item => item.Saved is not null)
            .Select(item => new MacroChoiceViewModel(
                item.Draft.MacroId,
                item.Saved!.Name,
                item.Draft))
            .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.MacroId)
            .ToArray();

        PublishedChoices.Clear();
        foreach (var choice in choices)
            PublishedChoices.Add(choice);
        OnPropertyChanged(nameof(PublishedChoices));
    }

    private string AllocateName(string requestedName)
    {
        var root = (requestedName ?? string.Empty).Trim();
        if (root.Length == 0)
            root = L("新建宏", "New macro");
        if (root.Length > AutomationConfigurationValidator.MaximumNameLength)
            root = root[..AutomationConfigurationValidator.MaximumNameLength];

        var occupied = Macros
            .Select(macro => macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!occupied.Contains(root))
            return root;

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" ({suffix})";
            var rootLength = AutomationConfigurationValidator.MaximumNameLength -
                             suffixText.Length;
            var candidate = root[..Math.Min(root.Length, rootLength)] + suffixText;
            if (!occupied.Contains(candidate))
                return candidate;
        }
    }

    private Guid CreateUniqueId()
    {
        Guid macroId;
        do
        {
            macroId = Guid.NewGuid();
        }
        while (Macros.Any(macro => macro.MacroId == macroId));

        return macroId;
    }

    private void RefreshNameConflicts()
    {
        var duplicateNames = Macros
            .Where(macro => !macro.IsDeleted)
            .Select(macro => (macro.Name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in Macros)
        {
            var normalized = (macro.Name ?? string.Empty).Trim();
            macro.UpdateNameConflict(
                !macro.IsDeleted && duplicateNames.Contains(normalized));
        }
    }

    private string L(string chinese, string english) =>
        _isEnglish ? english : chinese;

    private void OnReferenceIndexChanged(object? sender, EventArgs args)
    {
        RefreshReferences();
        if (BlockingReferences.Count == 0)
            return;

        var references = SelectedMacro is null
            ? []
            : _referenceIndex.GetReferences(SelectedMacro.MacroId);
        BlockingReferences.Clear();
        foreach (var reference in references)
            BlockingReferences.Add(reference);
    }

    private void RefreshSelectedMacroReferences()
    {
        SelectedMacroReferences.Clear();
        if (SelectedMacro is null)
            return;

        foreach (var reference in _referenceIndex.GetReferences(SelectedMacro.MacroId))
            SelectedMacroReferences.Add(reference);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _referenceIndex.Changed -= OnReferenceIndexChanged;
        foreach (var macro in Macros)
            macro.PropertyChanged -= OnDraftPropertyChanged;
        _disposed = true;
    }
}
