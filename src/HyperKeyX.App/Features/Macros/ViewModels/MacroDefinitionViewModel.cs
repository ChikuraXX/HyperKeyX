using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Features.Automation.ViewModels;

namespace HyperKeyX.Features.Macros.ViewModels;

/// <summary>
/// 管理单个可复用宏的元数据草稿、步骤编辑器和已保存基线。
/// </summary>
public sealed class MacroDefinitionViewModel : ObservableObject
{
    private MacroDefinition _savedBaseline;
    private string _name;
    private string _description;
    private bool _isNew;
    private bool _isDeleted;
    private bool _isUpdatingBaseline;
    private bool _isEnglish;
    private int _referenceCount;
    private bool _hasNameConflict;

    public MacroDefinitionViewModel(
        MacroDefinition source,
        bool isNew = false,
        bool isEnglish = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        _savedBaseline = Normalize(source);
        MacroId = _savedBaseline.MacroId;
        _name = _savedBaseline.Name;
        _description = _savedBaseline.Description;
        _isNew = isNew;
        _isEnglish = isEnglish;

        Editor = new MacroEditorViewModel(isEnglish);
        Editor.Load(_savedBaseline, isMacroEnabled: true);
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public Guid MacroId { get; }
    public MacroEditorViewModel Editor { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
                RaiseDraftState();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value ?? string.Empty))
                RaiseDraftState();
        }
    }

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (SetProperty(ref _isNew, value))
                RaiseDraftState();
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (SetProperty(ref _isDeleted, value))
                RaiseDraftState();
        }
    }

    public int StepCount => Editor.Steps.Count;
    public int ReferenceCount
    {
        get => _referenceCount;
        private set => SetProperty(ref _referenceCount, Math.Max(0, value));
    }

    public bool HasNameConflict => _hasNameConflict;
    public bool IsDirty =>
        IsNew ||
        IsDeleted ||
        !MetadataEqualsSavedBaseline() ||
        Editor.IsDirty;

    public bool IsValid =>
        MacroId != Guid.Empty &&
        NameValidationText.Length == 0 &&
        DescriptionValidationText.Length == 0 &&
        Editor.IsValid && MousePathValidator.Validate(ToModel()).IsValid;

    public bool CanSave => IsDeleted ? !IsNew : IsValid;
    public bool HasValidationErrors => !IsValid;
    public string DirtyStatusText => IsDirty ? L("未保存", "Unsaved") : string.Empty;
    public string ValidationStatusText => HasValidationErrors
        ? L("需要修正", "Needs attention")
        : string.Empty;
    public string DeletionStatusText => IsDeleted
        ? L("等待删除", "Pending deletion")
        : string.Empty;

    public string NameValidationText
    {
        get
        {
            var name = (Name ?? string.Empty).Trim();
            if (name.Length == 0)
                return L("宏名称不能为空。", "Macro name is required.");
            if (name.Length > AutomationConfigurationValidator.MaximumNameLength)
            {
                return L(
                    "宏名称不能超过 100 个字符。",
                    "Macro name cannot exceed 100 characters.");
            }
            if (HasNameConflict)
            {
                return L(
                    "宏名称必须在宏库中保持唯一。",
                    "Macro names must be unique in the library.");
            }

            return string.Empty;
        }
    }

    public string DescriptionValidationText =>
        (Description ?? string.Empty).Trim().Length >
        AutomationConfigurationValidator.MaximumDescriptionLength
            ? L(
                "宏说明不能超过 500 个字符。",
                "Macro description cannot exceed 500 characters.")
            : string.Empty;

    public MacroDefinition ToModel()
    {
        var model = Editor.ToMacroDefinition();
        model.MacroId = MacroId;
        model.Name = (Name ?? string.Empty).Trim();
        model.Description = (Description ?? string.Empty).Trim();
        return model;
    }

    public MacroDefinition? GetSavedModel() =>
        IsNew ? null : _savedBaseline.DeepClone();

    public void MarkSaved(MacroDefinition saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var normalized = Normalize(saved);
        if (normalized.MacroId != MacroId)
        {
            throw new ArgumentException(
                "The saved macro identity does not match this draft.",
                nameof(saved));
        }

        _isUpdatingBaseline = true;
        try
        {
            Name = normalized.Name;
            Description = normalized.Description;

            if (MacroContentComparer.Equals(ToModel(), normalized))
            {
                // 接受新基线但保留撤销/重做历史，保存后仍可回看此前编辑。
                Editor.AcceptSavedBaseline();
            }
            else
            {
                // 仅当持久化结果实际改写步骤时重载，避免重复订阅状态通知。
                Editor.PropertyChanged -= OnEditorPropertyChanged;
                try
                {
                    Editor.Load(normalized, isMacroEnabled: true);
                }
                finally
                {
                    Editor.PropertyChanged += OnEditorPropertyChanged;
                }
            }

            // 磁盘事务成功后才替换深复制基线，失败路径不会污染恢复目标。
            _savedBaseline = normalized.DeepClone();
            IsNew = false;
            IsDeleted = false;
        }
        finally
        {
            _isUpdatingBaseline = false;
        }

        RaiseDraftState();
    }

    public void RestoreSaved()
    {
        _isUpdatingBaseline = true;
        try
        {
            Name = _savedBaseline.Name;
            Description = _savedBaseline.Description;
            IsDeleted = false;
            Editor.RestoreSaved();
        }
        finally
        {
            _isUpdatingBaseline = false;
        }

        RaiseDraftState();
    }

    public void MarkDeleted()
    {
        if (!IsNew)
            IsDeleted = true;
    }

    public void RestoreDeletion() => IsDeleted = false;

    public void UpdateReferenceCount(int referenceCount) =>
        ReferenceCount = referenceCount;

    public void UpdateNameConflict(bool hasConflict)
    {
        if (!SetProperty(ref _hasNameConflict, hasConflict, nameof(HasNameConflict)))
            return;

        RaiseDraftState();
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        Editor.UpdateLocalization(isEnglish);
        RaiseDraftState();
    }

    private void OnEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (_isUpdatingBaseline)
            return;

        if (args.PropertyName is nameof(MacroEditorViewModel.IsDirty) or
            nameof(MacroEditorViewModel.IsValid) or
            nameof(MacroEditorViewModel.CanSave))
        {
            RaiseDraftState();
        }
    }

    private bool MetadataEqualsSavedBaseline() =>
        string.Equals(
            (Name ?? string.Empty).Trim(),
            _savedBaseline.Name,
            StringComparison.Ordinal) &&
        string.Equals(
            (Description ?? string.Empty).Trim(),
            _savedBaseline.Description,
            StringComparison.Ordinal);

    private void RaiseDraftState()
    {
        if (_isUpdatingBaseline)
            return;

        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(DirtyStatusText));
        OnPropertyChanged(nameof(ValidationStatusText));
        OnPropertyChanged(nameof(DeletionStatusText));
        OnPropertyChanged(nameof(NameValidationText));
        OnPropertyChanged(nameof(DescriptionValidationText));
        OnPropertyChanged(nameof(HasNameConflict));
    }

    private string L(string chinese, string english) =>
        _isEnglish ? english : chinese;

    private static MacroDefinition Normalize(MacroDefinition source)
    {
        var normalized = source.DeepClone();
        normalized.Name = (normalized.Name ?? string.Empty).Trim();
        normalized.Description = (normalized.Description ?? string.Empty).Trim();
        return normalized;
    }

}
