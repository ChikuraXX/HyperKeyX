using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

public sealed partial class MacroEditorViewModel
{
    private MacroPathContextSnapshot _pathContext = new([], []);
    private MacroPathContextSnapshot _savedPathContext = new([], []);
    private MousePathEditTransaction? _pendingPathEditBefore;
    private readonly long _historyByteLimit;
    private readonly int _maximumPathPoints;
    private bool _historyTrimmed;
    public long HistoryBytes { get; private set; }
    public string HistoryTrimmedText => _historyTrimmed
        ? L("较早的撤销历史已因容量限制移除。", "Older undo history was removed to stay within capacity limits.") : string.Empty;
    public IReadOnlyList<PathIssue> PathIssues { get; private set; } = [];

    public string AddPathText => L("鼠标轨迹", "Mouse path");

    public bool AddManualPath()
    {
        var group = new MousePathGroup
        {
            GroupId = Guid.NewGuid(), CoordinateMode = MousePathCoordinateMode.RelativeStart,
            Origin = new(0, 0), RecordedFrame = new() { Width = 1920, Height = 1080 }
        };
        return InsertRecordedMacro(new MacroDefinition
        {
            PathGroups = [group],
            Steps = [new MacroStep
            {
                ActionType = MacroActionType.MousePath,
                MousePath = new()
                {
                    GroupId = group.GroupId, Mode = MousePathMode.KeyPoints,
                    SourceKind = MousePathSourceKind.Manual,
                    Source = new([
                        new(Guid.NewGuid(), 0, 0, 0, 0, Guid.NewGuid()),
                        new(Guid.NewGuid(), 1_000_000, 1, 100, 100, Guid.NewGuid())])
                }
            }]
        });
    }

    public void Load(MacroDefinition macro, bool isMacroEnabled)
    {
        ArgumentNullException.ThrowIfNull(macro);
        LoadCore(macro.Steps, new(macro.PathGroups, macro.WindowTargets), isMacroEnabled);
    }

    public MacroDefinition ToMacroDefinition()
    {
        var model = _pathContext.ToModel();
        model.Steps = ToModels().ToList();
        return model;
    }

    public void BeginPathEdit()
    {
        if (_pendingPathEditBefore is not null) return;
        CommitSelectedStepEdit();
        _pendingPathEditBefore = new(Steps.Select(MacroStepSnapshot.FromRow).ToArray(), _pathContext, SelectedStep?.StepId);
    }

    public bool PreviewPathEdit(Action<MacroDefinition> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        BeginPathEdit();
        var candidate = ToMacroDefinition();
        edit(candidate);
        if (candidate.Steps.Count != Steps.Count || candidate.Steps.Sum(s => MousePathValidator.PointCost(s.MousePath)) > _maximumPathPoints)
            return RejectCommand(L("轨迹编辑超出当前事务的容量。", "The path edit exceeds this transaction's capacity."));
        _pathContext = new(candidate.PathGroups, candidate.WindowTargets, _pathContext.RecordingIssues);
        for (var index = 0; index < Steps.Count; index++)
            if (!ModelsEqual([Steps[index].ToModel()], [candidate.Steps[index]])) Steps[index].ReplaceWith(candidate.Steps[index]);
        ClearCommandError();
        RefreshDocumentState();
        return true;
    }

    public bool CommitPathEdit()
    {
        if (_pendingPathEditBefore is not { } before) return false;
        _pendingPathEditBefore = null;
        var after = Steps.Select(MacroStepSnapshot.FromRow).ToArray();
        var first = 0;
        while (first < after.Length && ModelsEqual([before.Rows[first].Model], [after[first].Model])) first++;
        var end = after.Length;
        while (end > first && ModelsEqual([before.Rows[end - 1].Model], [after[end - 1].Model])) end--;
        var contextChanged = !before.Context.ContentEquals(_pathContext);
        if (first == end && !contextChanged) return false;
        // 一次手势可同时改动多个共享锚点，历史仍保存为一个连续片段和一份上下文事务。
        RecordApplied(new(first, before.Rows.Skip(first).Take(end - first).ToArray(), after.Skip(first).Take(end - first).ToArray(),
            before.Selection, SelectedStep?.StepId, contextChanged ? before.Context : null, contextChanged ? _pathContext : null));
        return true;
    }

    public void CancelPathEdit()
    {
        if (_pendingPathEditBefore is not { } before) return;
        _pendingPathEditBefore = null;
        _pathContext = before.Context;
        for (var index = 0; index < before.Rows.Count; index++) Steps[index].ReplaceWith(before.Rows[index].Model);
        SetSelectionWithoutCommitting(Steps.FirstOrDefault(s => s.StepId == before.Selection));
        RefreshDocumentState();
    }

    public bool MovePathAnchor(Guid groupId, Guid anchorId, PathPosition position)
    {
        if (!PathGeometry.IsFinite(position)) return RejectCommand(L("坐标必须是有限数值。", "Coordinates must be finite."));
        var found = false;
        var ownGesture = _pendingPathEditBefore is null;
        PreviewPathEdit(macro =>
        {
            foreach (var path in macro.Steps.Where(s => s.MousePath?.GroupId == groupId).Select(s => s.MousePath!))
                foreach (var point in path.Source.Points.Where(p => p.AnchorId == anchorId))
                {
                    var edit = path.Edits.FirstOrDefault(e => e.PointId == point.PointId);
                    if (edit is null) { edit = new() { PointId = point.PointId }; path.Edits.Add(edit); }
                    edit.X = position.X; edit.Y = position.Y; found = true;
                }
        });
        if (ownGesture) CommitPathEdit();
        return found;
    }

    public bool InsertRecordedMacro(MacroDefinition macro, IReadOnlyList<PathIssue>? recordingIssues = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        CommitSelectedStepEdit();
        if (Steps.Count + macro.Steps.Count > MacroSequenceValidator.MaximumStepCount) return RejectCapacity();
        var current = ToMacroDefinition();
        var additions = DetachContext(macro, current.WindowTargets);
        if (current.Steps.Concat(additions.Steps).Sum(s => MousePathValidator.PointCost(s.MousePath)) > _maximumPathPoints)
            return RejectCommand(L("宏中的轨迹点超过容量限制。", "The macro exceeds the path point limit."));
        var snapshots = additions.Steps.Select(s => new MacroStepSnapshot(Guid.NewGuid(), s)).ToArray();
        if (snapshots.Length == 0) return false;
        var at = ResolveContextInsertionIndex(SelectedStep is null ? Steps.Count : Steps.IndexOf(SelectedStep) + 1);
        return ApplyReplacement(at, 0, snapshots, snapshots[0].StepId, contextAfter: new(
            current.PathGroups.Concat(additions.PathGroups), current.WindowTargets.Concat(additions.WindowTargets),
            _pathContext.RecordingIssues.Concat(additions.PathGroups.SelectMany(g =>
                (recordingIssues ?? []).Select(issue => new RecordedPathIssue(g.GroupId, issue with { StepIndex = -1 }))))));
    }

    public bool RestorePathSource(Guid stepId)
    {
        CommitSelectedStepEdit();
        var index = Steps.ToList().FindIndex(s => s.StepId == stepId);
        if (index < 0 || Steps[index].ActionType != MacroActionType.MousePath) return false;
        PreviewPathEdit(macro =>
        {
            var restored = macro.Steps[index].MousePath!;
            restored.Edits.Clear();
            var anchors = restored.Source.Points.Where(p => p.AnchorId is not null)
                .GroupBy(p => p.AnchorId!.Value).ToDictionary(g => g.Key, g => g.First());
            foreach (var path in macro.Steps.Where(s => s.MousePath?.GroupId == restored.GroupId).Select(s => s.MousePath!))
            {
                if (ReferenceEquals(path, restored)) continue;
                foreach (var point in path.Source.Points)
                {
                    if (point.AnchorId is not { } anchor || !anchors.TryGetValue(anchor, out var raw)) continue;
                    var edit = path.Edits.FirstOrDefault(e => e.PointId == point.PointId);
                    if (edit is null && point.X == raw.X && point.Y == raw.Y) continue;
                    if (edit is null) { edit = new() { PointId = point.PointId }; path.Edits.Add(edit); }
                    edit.X = point.X == raw.X ? null : raw.X;
                    edit.Y = point.Y == raw.Y ? null : raw.Y;
                    edit.IsDeleted = false;
                    if (edit.X is null && edit.Y is null && edit.OffsetUs is null) path.Edits.Remove(edit);
                }
            }
        });
        return CommitPathEdit();
    }

    public bool ReplaceFromRecording(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        CommitSelectedStepEdit();
        if (macro.Steps.Count > MacroSequenceValidator.MaximumStepCount) return RejectCapacity();
        var snapshots = macro.Steps.Select(s => new MacroStepSnapshot(Guid.NewGuid(), s)).ToArray();
        return ApplyReplacement(0, Steps.Count, snapshots, snapshots.FirstOrDefault()?.StepId,
            contextAfter: new(macro.PathGroups, macro.WindowTargets));
    }

    private bool DuplicateSelectedPath()
    {
        var copy = SelectedStep!.ToModel();
        var current = ToMacroDefinition();
        var original = current.PathGroups.FirstOrDefault(g => g.GroupId == copy.MousePath!.GroupId);
        if (original is null) return RejectCommand(L("轨迹组不存在。", "The path group is missing."));
        var group = original.DeepClone(); group.GroupId = Guid.NewGuid();
        copy.MousePath!.GroupId = group.GroupId;
        var snapshot = new MacroStepSnapshot(Guid.NewGuid(), copy);
        return ApplyReplacement(ResolveContextInsertionIndex(Steps.IndexOf(SelectedStep) + 1), 0, [snapshot], snapshot.StepId,
            contextAfter: new(current.PathGroups.Append(group), current.WindowTargets,
                _pathContext.RecordingIssues.Concat(_pathContext.RecordingIssues.Where(i => i.GroupId == original.GroupId)
                    .Select(i => i with { GroupId = group.GroupId }))));
    }

    public void CopyRecordingDiagnosticsFrom(MacroEditorViewModel source)
    {
        _pathContext = new(_pathContext.Groups, _pathContext.Targets, source._pathContext.RecordingIssues);
        RefreshDocumentState();
    }

    private int ResolveContextInsertionIndex(int requested)
    {
        // 新的独立组应放在当前连续组末尾，不能把原组拆成 A→B→A。
        foreach (var group in ToModels().Select((s, i) => (s, i)).Where(p => p.s.MousePath is not null).GroupBy(p => p.s.MousePath!.GroupId))
            if (requested > group.First().i && requested <= group.Last().i) return group.Last().i + 1;
        return requested;
    }

    private static MacroDefinition DetachContext(MacroDefinition source, IReadOnlyList<MacroWindowTarget> existingTargets)
    {
        var copy = source.DeepClone();
        var groups = copy.PathGroups.ToDictionary(g => g.GroupId, _ => Guid.NewGuid());
        // 同一已选角色再次录制时保留身份；定义冲突或不同 ID 仍作为独立角色插入。
        var reused = copy.WindowTargets.Where(t => existingTargets.Contains(t)).Select(t => t.TargetId).ToHashSet();
        var targets = copy.WindowTargets.ToDictionary(t => t.TargetId, t => reused.Contains(t.TargetId) ? t.TargetId : Guid.NewGuid());
        copy.WindowTargets.RemoveAll(t => reused.Contains(t.TargetId));
        foreach (var target in copy.WindowTargets) target.TargetId = targets[target.TargetId];
        foreach (var group in copy.PathGroups)
        {
            group.GroupId = groups[group.GroupId];
            if (group.TargetId is { } target && targets.TryGetValue(target, out var mapped)) group.TargetId = mapped;
        }
        foreach (var step in copy.Steps)
            if (step.MousePath is { } path && groups.TryGetValue(path.GroupId, out var mapped)) path.GroupId = mapped;
        return copy;
    }

    private void PruneHistory()
    {
        while (true)
        {
            HistoryBytes = MacroHistoryBudget.Estimate(_undo.Concat(_redo), ToModels(), _savedBaseline, _pathContext, _savedPathContext);
            if (_undo.Count + _redo.Count <= MaximumHistoryDepth && HistoryBytes <= _historyByteLimit) break;
            if (_undo.Count > 0) _undo.RemoveAt(0);
            else if (_redo.Count > 0) _redo.Clear(); // 后续重做依赖相邻事务，不能留下断开的重做链。
            else break;
            _historyTrimmed = true;
        }
    }
}
