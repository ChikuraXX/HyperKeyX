using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 管理隔离的宏草稿、序列校验和有界事务历史。
/// </summary>
public sealed partial class MacroEditorViewModel : ObservableObject
{
    public const int MaximumHistoryDepth = 50;
    public const int DefaultClickHoldMs = 50;
    public const int DefaultDelayMs = 100;
    public const int DefaultWheelDelta = 120;

    private readonly List<MacroEditTransaction> _undo = [];
    private readonly List<MacroEditTransaction> _redo = [];
    private IReadOnlyList<MacroStep> _savedBaseline = [];
    private MacroStepSnapshot? _pendingEditBefore;
    private bool _isEnglish;
    private bool _isMacroEnabled;
    private bool _isReplayingSelection;

    [ObservableProperty]
    private MacroStepEditorViewModel? _selectedStep;

    public MacroEditorViewModel(bool isEnglish = false) : this(isEnglish, MousePathLimits.MaximumHistoryBytes) { }

    internal MacroEditorViewModel(bool isEnglish, long historyByteLimit, int maximumPathPoints = MousePathLimits.MaximumMacroPoints)
    {
        if (historyByteLimit < 0) throw new ArgumentOutOfRangeException(nameof(historyByteLimit));
        _historyByteLimit = historyByteLimit;
        if (maximumPathPoints is <= 0 or > MousePathLimits.MaximumMacroPoints) throw new ArgumentOutOfRangeException(nameof(maximumPathPoints));
        _maximumPathPoints = maximumPathPoints;
        _isEnglish = isEnglish;
        RefreshDocumentState();
    }

    public ObservableCollection<MacroStepEditorViewModel> Steps { get; } = [];
    public MacroStepEditorViewModel? FirstInvalidStep { get; private set; }
    public bool IsDirty { get; private set; }
    public bool IsValid { get; private set; }
    public bool CanSave => IsValid;
    public bool HasValidationErrors => !IsValid;
    public bool CanFocusFirstError => FirstInvalidStep is not null;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool CanEditSelection => SelectedStep is not null;
    public bool CanMoveUp =>
        SelectedStep is not null && Steps.IndexOf(SelectedStep) > 0;
    public bool CanMoveDown =>
        SelectedStep is not null &&
        Steps.IndexOf(SelectedStep) is var index &&
        index >= 0 && index < Steps.Count - 1;
    public string ValidationSummaryText { get; private set; } = string.Empty;
    public string CommandErrorText { get; private set; } = string.Empty;
    public bool HasCommandError => CommandErrorText.Length > 0;
    public string AddText => L("添加步骤", "Add step");
    public string AddKeyboardClickText => L("键盘单击", "Keyboard click");
    public string AddMouseClickText => L("鼠标单击", "Mouse click");
    public string AddDelayText => L("延迟", "Delay");
    public string WheelUpText => L("滚轮向上", "Wheel up");
    public string WheelDownText => L("滚轮向下", "Wheel down");
    public string AdvancedText => L("高级原始步骤", "Advanced raw steps");
    public string KeyboardDownText => L("键盘按下", "Keyboard down");
    public string KeyboardUpText => L("键盘弹起", "Keyboard up");
    public string MouseDownText => L("鼠标按下", "Mouse down");
    public string MouseUpText => L("鼠标弹起", "Mouse up");
    public string LeftMouseButtonText => L("鼠标左键", "Left mouse button");
    public string RightMouseButtonText => L("鼠标右键", "Right mouse button");
    public string MiddleMouseButtonText => L("鼠标中键", "Middle mouse button");
    public string MouseButton4Text => L("鼠标侧键 1", "Mouse button 4");
    public string MouseButton5Text => L("鼠标侧键 2", "Mouse button 5");
    public string UndoText => L("撤销", "Undo");
    public string RedoText => L("重做", "Redo");
    public string MoveUpText => L("上移", "Move up");
    public string MoveDownText => L("下移", "Move down");
    public string DuplicateText => L("复制步骤", "Duplicate step");
    public string DeleteText => L("删除步骤", "Delete step");
    public string ClearText => L("清空宏", "Clear macro");
    public string RestoreSavedText => L("恢复已保存宏", "Restore saved macro");
    public string EmptyText => L("暂无宏步骤", "No macro steps");
    public string FocusFirstErrorText => L("定位第一个错误", "Go to first error");
    public string PropertiesText => L("步骤属性", "Step properties");
    public string KeyboardKeyText => L("键盘按键", "Keyboard key");
    public string ChooseKeyboardKeyText => L("选择键盘按键", "Choose keyboard key");
    public string MouseButtonText => L("鼠标按键", "Mouse button");
    public string WheelDeltaText => L("滚轮增量", "Wheel delta");
    public string DelayDurationText => L("延迟时长", "Delay duration");
    public string RandomDelayText => L("随机延时", "Random delay");
    public string MinimumDelayText => L("最小延时（毫秒）", "Minimum delay (ms)");
    public string MaximumDelayText => L("最大延时（毫秒）", "Maximum delay (ms)");
    public string DelayDistributionText => L("分布方式", "Distribution");
    public string UniformDelayText => L("均匀分布", "Uniform");
    public string NormalDelayText => L("正态分布", "Normal");
    public string NormalDelayHint => L("以区间中点为中心，较少取到两端。", "Centered on the range midpoint; values near either end are less frequent.");
    public string PostActionDelayText => L("动作后延迟", "Post-action delay");
    public string ClearConfirmationTitle => L("清空当前宏？", "Clear this macro?");
    public string ClearConfirmationMessage => L(
        "所有步骤将被移除，此操作可以撤销。",
        "All steps will be removed. You can undo this action.");
    public string ConfirmClearText => L("清空", "Clear");
    public string CancelText => L("取消", "Cancel");

    partial void OnSelectedStepChanging(MacroStepEditorViewModel? value)
    {
        if (!_isReplayingSelection)
            CommitSelectedStepEdit();
    }

    partial void OnSelectedStepChanged(MacroStepEditorViewModel? value)
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanEditSelection));
    }

    public void Load(IEnumerable<MacroStep> steps, bool isMacroEnabled)
        => LoadCore(steps, new([], []), isMacroEnabled);

    private void LoadCore(IEnumerable<MacroStep> steps, MacroPathContextSnapshot context, bool isMacroEnabled)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var models = steps.Select(MacroStepSnapshot.CopyModel).ToArray();
        _pendingEditBefore = null;
        _pendingPathEditBefore = null;
        _pathContext = _savedPathContext = context;
        _historyTrimmed = false;
        HistoryBytes = 0;
        _undo.Clear();
        _redo.Clear();
        _isMacroEnabled = isMacroEnabled;
        Steps.Clear();
        foreach (var model in models)
            Steps.Add(new MacroStepEditorViewModel(model, _isEnglish));

        _savedBaseline = models.Select(MacroStepSnapshot.CopyModel).ToArray();
        SetSelectionWithoutCommitting(Steps.FirstOrDefault());
        ClearCommandError();
        RefreshDocumentState();
        RaiseHistoryProperties();
    }

    public void SetMacroEnabled(bool isMacroEnabled)
    {
        if (_isMacroEnabled == isMacroEnabled)
            return;

        _isMacroEnabled = isMacroEnabled;
        RefreshDocumentState();
    }

    public IReadOnlyList<MacroStep> ToModels() =>
        Steps
            .Select(row => MacroStepSnapshot.CopyModel(row.ToModel()))
            .ToArray();

    public void AcceptSavedBaseline()
    {
        CommitSelectedStepEdit();
        _savedBaseline = ToModels()
            .Select(MacroStepSnapshot.CopyModel)
            .ToArray();
        _savedPathContext = _pathContext;
        PruneHistory();
        RefreshDocumentState();
        RaiseHistoryProperties();
    }

    public bool RestoreSaved()
    {
        CommitSelectedStepEdit();
        if (ModelsEqual(ToModels(), _savedBaseline) && _pathContext.ContentEquals(_savedPathContext))
            return false;

        var restored = _savedBaseline
            .Select(model => new MacroStepSnapshot(Guid.NewGuid(), model))
            .ToArray();
        return ApplyReplacement(
            0,
            Steps.Count,
            restored,
            restored.FirstOrDefault()?.StepId, contextAfter: _savedPathContext);
    }

    public bool ReplaceFromRecording(IEnumerable<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var models = steps.Select(MacroStepSnapshot.CopyModel).ToArray();
        if (models.Length > MacroSequenceValidator.MaximumStepCount)
            return RejectCapacity();
        if (ModelsEqual(ToModels(), models))
            return false;

        var snapshots = models
            .Select(model => new MacroStepSnapshot(Guid.NewGuid(), model))
            .ToArray();
        return ApplyReplacement(
            0,
            Steps.Count,
            snapshots,
            snapshots.FirstOrDefault()?.StepId);
    }

    public bool AddKeyboardClick(KeyboardKeyIdentity key)
    {
        if (!key.IsValid)
            return RejectCommand(L("键盘按键无效。", "The keyboard key is invalid."));

        return InsertModels(
            [
                CreateKeyboardStep(key, MacroActionType.KeyDown),
                new MacroStep
                {
                    ActionType = MacroActionType.Delay,
                    DurationMs = DefaultClickHoldMs
                },
                CreateKeyboardStep(key, MacroActionType.KeyUp)
            ],
            selectedOffset: 1);
    }

    public bool AddKeyboardStep(KeyboardKeyIdentity key, bool isDown)
    {
        if (!key.IsValid)
            return RejectCommand(L("键盘按键无效。", "The keyboard key is invalid."));

        return InsertModels(
            [CreateKeyboardStep(
                key,
                isDown ? MacroActionType.KeyDown : MacroActionType.KeyUp)],
            selectedOffset: 0);
    }

    public bool AddMouseClick(MouseButtonKind button)
    {
        if (!InputTriggerIdentity.FromMouse(button).IsValid)
            return RejectCommand(L("鼠标按键无效。", "The mouse button is invalid."));

        return InsertModels(
            [
                CreateMouseStep(button, MacroActionType.MouseButtonDown),
                new MacroStep
                {
                    ActionType = MacroActionType.Delay,
                    DurationMs = DefaultClickHoldMs
                },
                CreateMouseStep(button, MacroActionType.MouseButtonUp)
            ],
            selectedOffset: 1);
    }

    public bool AddMouseStep(MouseButtonKind button, bool isDown)
    {
        if (!InputTriggerIdentity.FromMouse(button).IsValid)
            return RejectCommand(L("鼠标按键无效。", "The mouse button is invalid."));

        return InsertModels(
            [CreateMouseStep(
                button,
                isDown ? MacroActionType.MouseButtonDown : MacroActionType.MouseButtonUp)],
            selectedOffset: 0);
    }

    public bool AddDelay(int durationMs = DefaultDelayMs)
    {
        if (durationMs is < 0 or > MacroSequenceValidator.MaximumDurationMs)
            return RejectCommand(L("延迟时长无效。", "The delay duration is invalid."));

        return InsertModels(
            [new MacroStep
            {
                ActionType = MacroActionType.Delay,
                DurationMs = durationMs
            }],
            selectedOffset: 0);
    }

    public bool AddWheel(int delta)
    {
        if (delta == 0 ||
            delta is < -MacroSequenceValidator.MaximumWheelMagnitude or
                > MacroSequenceValidator.MaximumWheelMagnitude)
        {
            return RejectCommand(L("滚轮增量无效。", "The wheel delta is invalid."));
        }

        return InsertModels(
            [new MacroStep
            {
                ActionType = MacroActionType.MouseWheel,
                WheelDelta = delta
            }],
            selectedOffset: 0);
    }

    public bool MoveSelected(int offset)
    {
        CommitSelectedStepEdit();
        if (SelectedStep is null || offset is not (-1 or 1))
            return false;

        var current = Steps.IndexOf(SelectedStep);
        var target = current + offset;
        if (target < 0 || target >= Steps.Count)
            return false;

        var start = Math.Min(current, target);
        var after = Steps
            .Skip(start)
            .Take(2)
            .Select(MacroStepSnapshot.FromRow)
            .Reverse()
            .ToArray();
        return ApplyReplacement(start, 2, after, SelectedStep.StepId);
    }

    public bool DuplicateSelected()
    {
        CommitSelectedStepEdit();
        if (SelectedStep is null)
            return false;
        if (Steps.Count >= MacroSequenceValidator.MaximumStepCount)
            return RejectCapacity();

        if (SelectedStep.ActionType == MacroActionType.MousePath) return DuplicateSelectedPath();

        var index = Steps.IndexOf(SelectedStep) + 1;
        var copy = new MacroStepSnapshot(Guid.NewGuid(), SelectedStep.ToModel());
        return ApplyReplacement(index, 0, [copy], copy.StepId);
    }

    public bool DeleteSelected()
    {
        CommitSelectedStepEdit();
        if (SelectedStep is null)
            return false;

        var index = Steps.IndexOf(SelectedStep);
        var nextId = index + 1 < Steps.Count
            ? Steps[index + 1].StepId
            : index > 0
                ? Steps[index - 1].StepId
                : (Guid?)null;
        return ApplyReplacement(index, 1, [], nextId);
    }

    public bool Clear()
    {
        CommitSelectedStepEdit();
        if (Steps.Count == 0)
            return false;

        return ApplyReplacement(0, Steps.Count, [], null);
    }

    public bool BeginSelectedStepEdit()
    {
        if (SelectedStep is null)
            return false;
        if (_pendingEditBefore?.StepId == SelectedStep.StepId)
            return true;

        CommitSelectedStepEdit();
        _pendingEditBefore = MacroStepSnapshot.FromRow(SelectedStep);
        return true;
    }

    public bool PreviewSelectedStep(MacroStep value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (SelectedStep is null || !BeginSelectedStepEdit())
            return false;

        SelectedStep.ReplaceWith(value);
        ClearCommandError();
        RefreshDocumentState();
        return true;
    }

    public bool CommitSelectedStepEdit()
    {
        CommitPathEdit();
        if (_pendingEditBefore is null)
            return false;

        var before = _pendingEditBefore;
        _pendingEditBefore = null;
        var row = Steps.FirstOrDefault(step => step.StepId == before.StepId);
        if (row is null)
            return false;

        var after = MacroStepSnapshot.FromRow(row);
        if (ModelsEqual([before.Model], [after.Model]))
            return false;

        var index = Steps.IndexOf(row);
        ClearCommandError();
        RecordApplied(new MacroEditTransaction(
            index,
            [before],
            [after],
            row.StepId,
            row.StepId));
        return true;
    }

    public void CancelSelectedStepEdit()
    {
        if (_pendingEditBefore is null)
            return;

        var before = _pendingEditBefore;
        _pendingEditBefore = null;
        var row = Steps.FirstOrDefault(step => step.StepId == before.StepId);
        row?.ReplaceWith(before.Model);
        RefreshDocumentState();
    }

    public void Undo()
    {
        CommitSelectedStepEdit();
        if (_undo.Count == 0)
            return;

        var transaction = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Replay(transaction, useAfter: false);
        _redo.Add(transaction);
        PruneHistory();
        RaiseHistoryProperties();
    }

    public void Redo()
    {
        CommitSelectedStepEdit();
        if (_redo.Count == 0)
            return;

        var transaction = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Replay(transaction, useAfter: true);
        _undo.Add(transaction);
        PruneHistory();
        RaiseHistoryProperties();
    }

    public void FocusFirstIssue()
    {
        if (FirstInvalidStep is not null)
            SelectedStep = FirstInvalidStep;
    }

    public void ReportUnexpectedCommandError()
    {
        CommandErrorText = L(
            "无法完成宏编辑操作，当前步骤已保持一致。",
            "The macro edit could not be completed; the current steps remain consistent.");
        OnPropertyChanged(nameof(CommandErrorText));
        OnPropertyChanged(nameof(HasCommandError));
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        foreach (var step in Steps)
            step.UpdateLocalization(isEnglish);

        foreach (var propertyName in LocalizedPropertyNames)
            OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(HistoryTrimmedText));
        OnPropertyChanged(nameof(AddPathText));
        RefreshDocumentState();
    }

    private bool InsertModels(IReadOnlyList<MacroStep> models, int selectedOffset)
    {
        CommitSelectedStepEdit();
        if (Steps.Count + models.Count > MacroSequenceValidator.MaximumStepCount)
            return RejectCapacity();

        var startIndex = SelectedStep is null
            ? Steps.Count
            : Steps.IndexOf(SelectedStep) + 1;
        var selectionBefore = SelectedStep?.StepId;
        var inserted = models
            .Select(model => new MacroStepSnapshot(Guid.NewGuid(), model))
            .ToArray();
        return ApplyReplacement(
            startIndex,
            removeCount: 0,
            inserted,
            inserted[selectedOffset].StepId,
            selectionBefore);
    }

    private bool ApplyReplacement(
        int startIndex,
        int removeCount,
        IReadOnlyList<MacroStepSnapshot> inserted,
        Guid? selectionAfter,
        Guid? selectionBefore = null,
        MacroPathContextSnapshot? contextAfter = null)
    {
        CommitSelectedStepEdit();
        var pointCount = Steps.Select((s, i) => (s, i)).Where(p => p.i < startIndex || p.i >= startIndex + removeCount)
            .Sum(p => MousePathValidator.PointCost(p.s.ToModel().MousePath))
            + inserted.Sum(s => MousePathValidator.PointCost(s.Model.MousePath));
        if (pointCount > _maximumPathPoints)
            return RejectCommand(L("宏中的轨迹点超过容量限制。", "The macro exceeds the path point limit."));
        var contextBefore = contextAfter is null ? null : _pathContext;
        if (contextAfter is not null) _pathContext = contextAfter;
        selectionBefore ??= SelectedStep?.StepId;
        var removed = Steps
            .Skip(startIndex)
            .Take(removeCount)
            .Select(MacroStepSnapshot.FromRow)
            .ToArray();

        for (var count = 0; count < removeCount; count++)
            Steps.RemoveAt(startIndex);
        for (var index = 0; index < inserted.Count; index++)
            Steps.Insert(startIndex + index, inserted[index].CreateRow(_isEnglish));

        SetSelectionWithoutCommitting(selectionAfter is Guid id
            ? Steps.FirstOrDefault(step => step.StepId == id)
            : null);
        ClearCommandError();
        RecordApplied(new MacroEditTransaction(
            startIndex,
            removed,
            inserted.ToArray(),
            selectionBefore,
            selectionAfter, contextBefore, contextAfter));
        return true;
    }

    private void RecordApplied(MacroEditTransaction transaction)
    {
        _undo.Add(transaction);
        _redo.Clear();
        PruneHistory();
        RefreshDocumentState();
        RaiseHistoryProperties();
    }

    private void Replay(MacroEditTransaction transaction, bool useAfter)
    {
        var remove = useAfter ? transaction.Before : transaction.After;
        var insert = useAfter ? transaction.After : transaction.Before;
        var selection = useAfter
            ? transaction.SelectionAfter
            : transaction.SelectionBefore;
        if ((useAfter ? transaction.ContextAfter : transaction.ContextBefore) is { } context)
            _pathContext = context;

        // 历史只重放受影响的连续片段，未改动行及其稳定 ID 保持不变。
        for (var count = 0; count < remove.Count; count++)
            Steps.RemoveAt(transaction.StartIndex);
        for (var index = 0; index < insert.Count; index++)
        {
            Steps.Insert(
                transaction.StartIndex + index,
                insert[index].CreateRow(_isEnglish));
        }

        SetSelectionWithoutCommitting(selection is Guid id
            ? Steps.FirstOrDefault(step => step.StepId == id)
            : null);
        ClearCommandError();
        RefreshDocumentState();
    }

    private void RefreshDocumentState()
    {
        var models = ToModels();
        var result = MacroSequenceValidator.Validate(models, _isMacroEnabled);
        // 捕获缺口不能靠调整参数变成完整轨迹；仅删除对应组或撤销录制才能移除诊断。
        var capturedGroups = models.Where(s => s.MousePath is not null).Select(s => s.MousePath!.GroupId).ToHashSet();
        PathIssues = MousePathValidator.Validate(ToMacroDefinition()).Issues
            .Concat(_pathContext.RecordingIssues.Where(i => capturedGroups.Contains(i.GroupId)).Select(i => i.Issue)).Distinct().ToArray();
        var issuesByIndex = result.Issues
            .Where(issue => issue.StepIndex >= 0 && issue.StepIndex < Steps.Count)
            .GroupBy(issue => issue.StepIndex)
            .ToDictionary(
                group => group.Key,
                group => (IEnumerable<MacroValidationIssueCode>)group.Select(issue => issue.Code).ToArray());

        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].SetSequenceNumber(index + 1);
            if (models[index].MousePath is { } path)
            {
                var group = _pathContext.Groups.FirstOrDefault(g => g.GroupId == path.GroupId);
                var target = _pathContext.Targets.FirstOrDefault(t => t.TargetId == group?.TargetId);
                Steps[index].SetPathContext(group, target?.Name ?? target?.ProcessName);
            }
            var codes = issuesByIndex.TryGetValue(index, out var rowIssues)
                ? rowIssues
                : [];
            if (PathIssues.Any(issue => issue.StepIndex == index ||
                issue.StepIndex < 0 && models[index].MousePath is not null))
                codes = codes.Append(MacroValidationIssueCode.InvalidMousePath).Distinct();
            Steps[index].SetValidationIssues(codes, _isEnglish);
        }

        FirstInvalidStep = result.Issues
            .Select(issue => issue.StepIndex)
            .Where(index => index >= 0 && index < Steps.Count)
            .Select(index => Steps[index])
            .FirstOrDefault();
        FirstInvalidStep ??= PathIssues.Where(i => i.StepIndex >= 0 && i.StepIndex < Steps.Count)
            .Select(i => Steps[i.StepIndex]).FirstOrDefault();
        if (PathIssues.Count > 0) FirstInvalidStep ??= Steps.FirstOrDefault(s => s.IsPathAction);
        IsValid = result.IsValid && PathIssues.Count == 0;
        IsDirty = !ModelsEqual(models, _savedBaseline) || !_pathContext.ContentEquals(_savedPathContext);
        ValidationSummaryText = IsValid
            ? string.Empty
            : CreateValidationSummary(result.Issues.Count + PathIssues.Count);
        RaiseDocumentProperties();
    }

    private void RaiseDocumentProperties()
    {
        OnPropertyChanged(nameof(FirstInvalidStep));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanFocusFirstError));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(PathIssues));
        OnPropertyChanged(nameof(ValidationSummaryText));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanEditSelection));
    }

    private void RaiseHistoryProperties()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HistoryBytes));
        OnPropertyChanged(nameof(HistoryTrimmedText));
    }

    private void SetSelectionWithoutCommitting(MacroStepEditorViewModel? selection)
    {
        _isReplayingSelection = true;
        try
        {
            SelectedStep = selection;
        }
        finally
        {
            _isReplayingSelection = false;
        }
    }

    private bool RejectCapacity() => RejectCommand(_isEnglish
        ? "A macro cannot exceed 512 steps."
        : "宏步骤不能超过 512 步。");

    private bool RejectCommand(string text)
    {
        CommandErrorText = text;
        OnPropertyChanged(nameof(CommandErrorText));
        OnPropertyChanged(nameof(HasCommandError));
        return false;
    }

    private void ClearCommandError()
    {
        if (CommandErrorText.Length == 0)
            return;

        CommandErrorText = string.Empty;
        OnPropertyChanged(nameof(CommandErrorText));
        OnPropertyChanged(nameof(HasCommandError));
    }

    private string CreateValidationSummary(int issueCount) => _isEnglish
        ? $"{issueCount} macro error{(issueCount == 1 ? string.Empty : "s")} must be fixed before saving."
        : $"宏中有 {issueCount} 个错误，修正后才能保存。";

    private string L(string chinese, string english) =>
        _isEnglish ? english : chinese;

    private static MacroStep CreateKeyboardStep(
        KeyboardKeyIdentity key,
        MacroActionType action) => new()
    {
        ActionType = action,
        VirtualKeyCode = key.VirtualKeyCode,
        ScanCode = key.ScanCode,
        IsExtendedKey = key.IsExtendedKey
    };

    private static MacroStep CreateMouseStep(
        MouseButtonKind button,
        MacroActionType action) => new()
    {
        ActionType = action,
        MouseButton = button
    };

    private static bool ModelsEqual(
        IReadOnlyList<MacroStep> left,
        IReadOnlyList<MacroStep> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            var leftStep = left[index];
            var rightStep = right[index];
            if (leftStep.ActionType != rightStep.ActionType ||
                leftStep.VirtualKeyCode != rightStep.VirtualKeyCode ||
                leftStep.ScanCode != rightStep.ScanCode ||
                leftStep.IsExtendedKey != rightStep.IsExtendedKey ||
                leftStep.DurationMs != rightStep.DurationMs ||
                leftStep.MouseButton != rightStep.MouseButton ||
                leftStep.WheelDelta != rightStep.WheelDelta ||
                !MacroContentComparer.StepEquals(leftStep, rightStep))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] LocalizedPropertyNames =
    [
        nameof(AddText),
        nameof(AddKeyboardClickText),
        nameof(AddMouseClickText),
        nameof(AddDelayText),
        nameof(WheelUpText),
        nameof(WheelDownText),
        nameof(AdvancedText),
        nameof(KeyboardDownText),
        nameof(KeyboardUpText),
        nameof(MouseDownText),
        nameof(MouseUpText),
        nameof(LeftMouseButtonText),
        nameof(RightMouseButtonText),
        nameof(MiddleMouseButtonText),
        nameof(MouseButton4Text),
        nameof(MouseButton5Text),
        nameof(UndoText),
        nameof(RedoText),
        nameof(MoveUpText),
        nameof(MoveDownText),
        nameof(DuplicateText),
        nameof(DeleteText),
        nameof(ClearText),
        nameof(RestoreSavedText),
        nameof(EmptyText),
        nameof(FocusFirstErrorText),
        nameof(PropertiesText),
        nameof(KeyboardKeyText),
        nameof(ChooseKeyboardKeyText),
        nameof(MouseButtonText),
        nameof(WheelDeltaText),
        nameof(DelayDurationText),
        nameof(RandomDelayText),
        nameof(MinimumDelayText),
        nameof(MaximumDelayText),
        nameof(DelayDistributionText),
        nameof(UniformDelayText),
        nameof(NormalDelayText),
        nameof(NormalDelayHint),
        nameof(PostActionDelayText),
        nameof(ClearConfirmationTitle),
        nameof(ClearConfirmationMessage),
        nameof(ConfirmClearText),
        nameof(CancelText)
    ];
}
