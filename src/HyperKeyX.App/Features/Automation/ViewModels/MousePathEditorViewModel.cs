using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Application.Automation.Paths;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

public sealed record PathPointRow(int Index, MousePathPoint Point)
{
    public Guid PointId => Point.PointId;
    public double X => Point.X;
    public double Y => Point.Y;
    public double TimeMs => Point.OffsetUs / 1_000d;
    public string Label => $"{Index + 1} · ({X:0.###}, {Y:0.###}) · {TimeMs:0.###} ms";
}
public sealed record PathTargetChoice(Guid Id, string Name);

/// <summary>轨迹详情只编辑完整事务和生成数据预览，不持有输入服务。</summary>
public sealed partial class MousePathEditorViewModel : ObservableObject, IDisposable
{
    private readonly MacroEditorViewModel _editor;
    private readonly Guid _stepId;
    private readonly IMousePathPreviewBuilder _previewBuilder;
    private MacroDefinition _snapshot = new();
    private MousePathDefinition? _path;
    private MousePathGroup? _group;
    private IReadOnlyList<MousePathPoint> _effective = [];
    private int[]? _keyPointIndices;
    private CancellationTokenSource? _previewStop;
    private long _revision;
    private bool _isEnglish;
    private bool _updating;
    private bool _gesture;
    private bool _disposed;
    private int _page;
    private Guid? _selectedTargetId;
    private PathPointRow? _selectedPoint;
    private MousePathPreview _preview = new([], null, []);

    public MousePathEditorViewModel(MacroEditorViewModel editor, Guid stepId, bool isEnglish = false,
        IMousePathPreviewBuilder? previewBuilder = null)
    {
        _editor = editor; _stepId = stepId; _isEnglish = isEnglish;
        _previewBuilder = previewBuilder ?? new MousePathPreviewBuilder();
        _editor.PropertyChanged += OnEditorChanged;
        Refresh();
    }

    public Guid StepId => _stepId;
    public bool IsAvailable => _path is not null && _group is not null;
    public bool IsDirty => _editor.IsDirty;
    public Task PreviewTask { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<PathPosition> PreviewPoints => _preview.Points;
    public IReadOnlyList<PathPosition> RawPreviewPoints => _preview.RawPoints ?? [];
    public double? TrajectoryDurationMs => _preview.DurationUs / 1_000d;
    public double? EntryDurationMs => null;
    public ObservableCollection<PathPointRow> Points { get; } = [];
    public IReadOnlyList<PathTargetChoice> TargetChoices { get; private set; } = [];
    public MacroWindowTargetViewModel? TargetEditor { get; private set; }
    public int TotalPointCount => _effective.Count;
    private int VisiblePointCount => _keyPointIndices?.Length ?? TotalPointCount;
    public bool HasPreviousPage => _page > 0;
    public bool HasNextPage => (_page + 1) * 100 < VisiblePointCount;
    public string PageText => $"{_page + 1} / {Math.Max(1, (int)Math.Ceiling(VisiblePointCount / 100d))}";
    public string ValidationText => string.Join(Environment.NewLine,
        _editor.PathIssues.Concat(_preview.Issues).Distinct().Select(i => MacroValidationMessageFormatter.Format(i, _isEnglish)));
    public bool HasValidationErrors => ValidationText.Length > 0;
    public PathPointRow? SelectedPoint
    {
        get => _selectedPoint;
        set { if (SetProperty(ref _selectedPoint, value)) OnPropertyChanged(string.Empty); }
    }
    public double PointX { get => SelectedPoint?.X ?? 0; set => EditPoint(value, PointY, null); }
    public double PointY { get => SelectedPoint?.Y ?? 0; set => EditPoint(PointX, value, null); }
    public double PointTimeMs { get => SelectedPoint?.TimeMs ?? 0; set => EditPoint(PointX, PointY, ToUs(value)); }
    public bool CanAddPoint => SelectedPoint is not null && SelectedPoint.Index + 1 < _effective.Count;
    public bool CanDeletePoint => SelectedPoint is { Point.AnchorId: null } && SelectedPoint.Index > 0 && SelectedPoint.Index + 1 < _effective.Count;
    public bool CanEditPointTime => SelectedPoint is { Index: > 0 };

    public MousePathMode Mode { get => _path?.Mode ?? MousePathMode.Continuous; set { if (Mode != value) Change((p, _, _) => p.Mode = value); } }
    public int ModeIndex { get => (int)Mode; set { if (value >= 0) Mode = (MousePathMode)value; } }
    public MousePathTimingMode TimingMode { get => _path?.Timing.Mode ?? MousePathTimingMode.Original; set { if (TimingMode != value) Change((p, _, _) => p.Timing.Mode = value); } }
    public int TimingModeIndex { get => (int)TimingMode; set { if (value >= 0) TimingMode = (MousePathTimingMode)value; } }
    public MousePathCoordinateMode CoordinateMode
    {
        get => _group?.CoordinateMode ?? MousePathCoordinateMode.RelativeStart;
        set { if (CoordinateMode != value) Change((_, g, _) => { g.CoordinateMode = value; g.TargetId = value == MousePathCoordinateMode.WindowClient ? _selectedTargetId : null; }); }
    }
    public int CoordinateModeIndex { get => (int)CoordinateMode; set { if (value >= 0) CoordinateMode = (MousePathCoordinateMode)value; } }
    public bool UsesWindow => CoordinateMode == MousePathCoordinateMode.WindowClient;
    public int SelectedTargetIndex
    {
        get => TargetChoices.ToList().FindIndex(t => t.Id == _selectedTargetId);
        set
        {
            if (value < 0 || value >= TargetChoices.Count) return;
            if (_selectedTargetId == TargetChoices[value].Id) return;
            _selectedTargetId = TargetChoices[value].Id;
            if (UsesWindow) Change((_, g, _) => g.TargetId = _selectedTargetId);
            RefreshTargetEditor(); OnPropertyChanged(string.Empty);
        }
    }

    public void BeginGesture() { if (_gesture) return; _gesture = true; _editor.BeginPathEdit(); }
    public void EndGesture()
    {
        if (!_gesture) return;
        _updating = true;
        try { _editor.CommitPathEdit(); }
        finally { _gesture = false; _updating = false; }
        Refresh();
    }
    public void CancelGesture()
    {
        _updating = true;
        try { _editor.CancelPathEdit(); }
        finally { _gesture = false; _updating = false; }
        Refresh();
    }

    private void Change(Action<MousePathDefinition, MousePathGroup, MacroDefinition> change)
    {
        if (_disposed || !IsAvailable || _updating) return;
        _updating = true;
        try
        {
            var index = FindStep();
            if (index < 0) return;
            _editor.PreviewPathEdit(macro =>
            {
                var path = macro.Steps[index].MousePath!;
                change(path, macro.PathGroups.Single(g => g.GroupId == path.GroupId), macro);
            });
            if (!_gesture) _editor.CommitPathEdit();
        }
        finally { _updating = false; }
        Refresh();
    }

    private void EditPoint(double x, double y, long? time)
    {
        if (SelectedPoint is not { } selected || (x == selected.X && y == selected.Y && (time is null || time == selected.Point.OffsetUs))) return;
        Change((path, group, macro) =>
        {
            foreach (var other in macro.Steps.Where(s => s.MousePath?.GroupId == group.GroupId).Select(s => s.MousePath!))
            {
                var ids = selected.Point.AnchorId is { } anchor
                    ? other.Source.Points.Where(p => p.AnchorId == anchor).Select(p => p.PointId)
                    : ReferenceEquals(other, path) ? [selected.PointId] : Enumerable.Empty<Guid>();
                foreach (var id in ids)
                {
                    var edit = other.Edits.FirstOrDefault(e => e.PointId == id);
                    if (edit is null) { edit = new() { PointId = id }; other.Edits.Add(edit); }
                    edit.X = x; edit.Y = y;
                    if (ReferenceEquals(other, path) && id == selected.PointId && time is not null) edit.OffsetUs = time;
                }
            }
        });
    }

    public bool AddPoint()
    {
        if (!CanAddPoint || SelectedPoint is not { } first) return false;
        // 新点紧跟所选点插入；隐藏采样点仍参与时间顺序，插值必须使用实际相邻点。
        var next = _effective[first.Index + 1];
        var id = Guid.NewGuid();
        Change((path, _, _) => path.Edits.Add(new()
        {
            PointId = id, InsertAfterPointId = first.PointId, X = (first.X + next.X) / 2,
            Y = (first.Y + next.Y) / 2, OffsetUs = first.Point.OffsetUs + (next.OffsetUs - first.Point.OffsetUs) / 2
        }));
        GoToPoint(id); return true;
    }
    public bool DeletePoint()
    {
        if (!CanDeletePoint || SelectedPoint is not { } point) return false;
        if (_path!.Edits.Any(e => e.InsertAfterPointId == point.PointId) &&
            !_path.Edits.Any(e => e.PointId == point.PointId && e.InsertAfterPointId is not null)) return false;
        Change((path, _, _) =>
        {
            var edit = path.Edits.FirstOrDefault(e => e.PointId == point.PointId);
            if (edit?.InsertAfterPointId is { } parent)
            {
                foreach (var child in path.Edits.Where(e => e.InsertAfterPointId == point.PointId)) child.InsertAfterPointId = parent;
                path.Edits.Remove(edit);
            }
            else if (!path.Edits.Any(e => e.InsertAfterPointId == point.PointId))
            {
                if (edit is null) { edit = new() { PointId = point.PointId }; path.Edits.Add(edit); }
                edit.IsDeleted = true;
            }
        });
        return true;
    }
    public bool RestoreSource() => _editor.RestorePathSource(_stepId);
    public void PreviousPage() { if (HasPreviousPage) { _page--; RefreshPage(); } }
    public void NextPage() { if (HasNextPage) { _page++; RefreshPage(); } }
    public void GoToPoint(Guid pointId)
    {
        var index = -1;
        for (var i = 0; i < _effective.Count; i++)
            if (_effective[i].PointId == pointId) { index = i; break; }
        if (index < 0) return;
        if (_keyPointIndices is not null)
        {
            var visible = Array.IndexOf(_keyPointIndices, index);
            if (visible < 0)
            {
                _keyPointIndices = _keyPointIndices.Append(index).Order().ToArray();
                visible = Array.IndexOf(_keyPointIndices, index);
            }
            index = visible;
        }
        _page = index / 100; RefreshPage(pointId);
    }
    public void GoToFirstIssue()
    {
        var point = _editor.PathIssues.Concat(_preview.Issues).FirstOrDefault(i => i.PointId is not null)?.PointId;
        if (point is { } id) GoToPoint(id);
    }

    public void AddTarget()
    {
        var id = Guid.NewGuid();
        Change((_, group, macro) =>
        {
            macro.WindowTargets.Add(new() { TargetId = id, Name = L("新目标", "New target"), ProcessName = "" });
            if (group.CoordinateMode == MousePathCoordinateMode.WindowClient) group.TargetId = id;
        });
        _selectedTargetId = id; RefreshTargetEditor(); OnPropertyChanged(string.Empty);
    }
    public bool RemoveTarget()
    {
        if (_selectedTargetId is not { } id) return false;
        // 只计算仍有步骤引用的组，删除轨迹后遗留的空组不能继续占用目标。
        var usedGroups = _snapshot.Steps.Where(s => s.MousePath is not null).Select(s => s.MousePath!.GroupId).ToHashSet();
        if (_snapshot.PathGroups.Any(g => usedGroups.Contains(g.GroupId) && g.TargetId == id)) return false;
        Change((_, _, macro) =>
        {
            // 这些组均已无步骤引用，与目标在同一事务中移除，避免留下悬空引用。
            macro.PathGroups.RemoveAll(g => g.TargetId == id);
            macro.WindowTargets.RemoveAll(t => t.TargetId == id);
        });
        return true;
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        _editor.UpdateLocalization(isEnglish);
        TargetEditor?.Refresh(isEnglish);
        OnPropertyChanged(string.Empty);
    }
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_disposed && !_updating && args.PropertyName == nameof(MacroEditorViewModel.PathIssues)) Refresh();
    }
    private int FindStep() => _editor.Steps.ToList().FindIndex(s => s.StepId == _stepId);
    private void Refresh()
    {
        if (_disposed) return;
        var index = FindStep();
        var snapshot = _editor.ToMacroDefinition();
        var changed = !MacroContentComparer.Equals(_snapshot, snapshot);
        var oldPath = _path;
        _snapshot = snapshot;
        _path = index >= 0 ? snapshot.Steps[index].MousePath : null;
        _group = snapshot.PathGroups.FirstOrDefault(g => g.GroupId == _path?.GroupId);
        if (_path is not null && (oldPath is null || !ReferenceEquals(oldPath.Source, _path.Source) || !oldPath.Edits.SequenceEqual(_path.Edits)))
        {
            try { _effective = MousePathValidator.ApplyEdits(_path); }
            catch (Exception) { _effective = _path.Source.Points; }
            _keyPointIndices = null;
            RefreshPage(SelectedPoint?.PointId);
        }
        if (oldPath?.Mode != _path?.Mode) { _keyPointIndices = null; RefreshPage(SelectedPoint?.PointId); }
        TargetChoices = snapshot.WindowTargets.Select(t => new PathTargetChoice(t.TargetId, string.IsNullOrEmpty(t.Name) ? t.ProcessName : t.Name)).ToArray();
        if (_group?.TargetId is { } target) _selectedTargetId = target;
        if (!TargetChoices.Any(t => t.Id == _selectedTargetId)) _selectedTargetId = TargetChoices.FirstOrDefault()?.Id;
        RefreshTargetEditor();
        OnPropertyChanged(string.Empty);
        if (changed && index >= 0) PreviewTask = RefreshPreviewAsync(snapshot, index);
    }
    private void RefreshPage(Guid? selected = null)
    {
        _page = Math.Clamp(_page, 0, Math.Max(0, (VisiblePointCount - 1) / 100));
        Points.Clear();
        for (var i = _page * 100; i < Math.Min((_page + 1) * 100, VisiblePointCount); i++)
        {
            var index = _keyPointIndices?[i] ?? i;
            Points.Add(new(index, _effective[index]));
        }
        SelectedPoint = Points.FirstOrDefault(p => p.PointId == selected) ?? Points.FirstOrDefault();
        OnPropertyChanged(string.Empty);
    }
    private void RefreshTargetEditor()
    {
        if (_selectedTargetId is not { } id) { TargetEditor = null; return; }
        if (TargetEditor?.TargetId != id)
            TargetEditor = new(id, () => _snapshot.WindowTargets.FirstOrDefault(t => t.TargetId == id),
                action => Change((_, _, macro) => { if (macro.WindowTargets.FirstOrDefault(t => t.TargetId == id) is { } target) action(target); }), _isEnglish);
        TargetEditor.Refresh(_isEnglish);
    }
    private async Task RefreshPreviewAsync(MacroDefinition snapshot, int index)
    {
        var revision = ++_revision;
        _previewStop?.Cancel(); _previewStop?.Dispose();
        _previewStop = new();
        var token = _previewStop.Token;
        try
        {
            var preview = await _previewBuilder.BuildAsync(snapshot, index, token);
            // 即使旧构建器未响应取消，也不能覆盖较新参数的预览。
            if (_disposed || token.IsCancellationRequested || revision != _revision) return;
            _preview = preview;
            if (Mode == MousePathMode.KeyPoints && preview.KeyPointIds is { Count: > 0 } ids)
            {
                var visible = ids.ToHashSet();
                _keyPointIndices = _effective.Select((p, i) => (p, i)).Where(p => visible.Contains(p.p.PointId)).Select(p => p.i).ToArray();
            }
            else _keyPointIndices = null;
            RefreshPage(SelectedPoint?.PointId);
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_disposed || revision != _revision) return;
            _preview = new([], null, [new(PathIssueCode.InvalidStructure)]); OnPropertyChanged(string.Empty);
        }
    }
    private static long ToUs(double ms) => double.IsFinite(ms) && ms >= 0 && ms <= MousePathLimits.MaximumRecordingDurationUs / 1_000d
        ? (long)Math.Round(ms * 1_000, MidpointRounding.AwayFromZero) : -1;
    private string L(string chinese, string english) => _isEnglish ? english : chinese;
    public void Dispose()
    {
        if (_disposed) return;
        if (_gesture) EndGesture();
        _disposed = true; _revision++;
        _editor.PropertyChanged -= OnEditorChanged;
        _previewStop?.Cancel(); _previewStop?.Dispose();
    }
}
