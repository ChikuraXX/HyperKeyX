namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>校验源数据和宏内引用；不查询窗口，也不修正损坏数据。</summary>
public static class MousePathValidator
{
    public static PathValidationResult Validate(MacroDefinition macro, int maximumPoints = MousePathLimits.MaximumMacroPoints)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (maximumPoints is <= 0 or > MousePathLimits.MaximumMacroPoints) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        var issues = new List<PathIssue>();
        var targets = new Dictionary<Guid, MacroWindowTarget>();
        var groups = new Dictionary<Guid, MousePathGroup>();
        if (macro.WindowTargets is null || macro.PathGroups is null || macro.Steps is null)
            return new([new(PathIssueCode.InvalidStructure)]);

        foreach (var target in macro.WindowTargets)
        {
            if (target is null || target.TargetId == Guid.Empty ||
                string.IsNullOrWhiteSpace(target.ProcessName) ||
                !Enum.IsDefined(target.TitleMatchMode))
            {
                issues.Add(new(PathIssueCode.InvalidTarget, TargetId: target?.TargetId));
                continue;
            }
            if (!targets.TryAdd(target.TargetId, target))
                issues.Add(new(PathIssueCode.DuplicateTarget, TargetId: target.TargetId));
        }

        foreach (var group in macro.PathGroups)
        {
            if (group is null || group.GroupId == Guid.Empty)
            {
                issues.Add(new(PathIssueCode.InvalidGroup));
                continue;
            }
            if (!groups.TryAdd(group.GroupId, group))
                issues.Add(new(PathIssueCode.DuplicateGroup));
            if (!ValidGroup(group))
                issues.Add(new(PathIssueCode.InvalidGroup, TargetId: group.TargetId));
            if (group.CoordinateMode == MousePathCoordinateMode.WindowClient &&
                (group.TargetId is not Guid targetId || !targets.ContainsKey(targetId)))
                issues.Add(new(PathIssueCode.MissingTarget, TargetId: group.TargetId));
        }

        var anchors = new Dictionary<(Guid Group, Guid Anchor), PathPosition>();
        var endedGroups = new HashSet<Guid>();
        var heldKeys = new HashSet<int>();
        var heldButtons = new HashSet<MouseButtonKind>();
        MousePathGroup? previousGroup = null;
        MousePathPoint? previousEnd = null;
        long pointCount = 0;

        for (var index = 0; index < macro.Steps.Count; index++)
        {
            var step = macro.Steps[index];
            if (step is null)
            {
                issues.Add(new(PathIssueCode.InvalidStructure, index));
                continue;
            }
            TrackHeld(step, heldKeys, heldButtons);
            if (step.ActionType != MacroActionType.MousePath)
            {
                if (step.MousePath is not null)
                    issues.Add(new(PathIssueCode.UnexpectedPayload, index));
                continue;
            }

            var payload = ValidatePayload(step.MousePath, index);
            issues.AddRange(payload.Issues);
            if (step.DurationMs != 0)
                issues.Add(new(PathIssueCode.InvalidSettings, index));
            var path = step.MousePath;
            pointCount += PointCost(path);
            if (pointCount > maximumPoints)
            {
                issues.Add(new(PathIssueCode.TooManyPoints, index));
                break;
            }
            if (path is null || !groups.TryGetValue(path.GroupId, out var group))
            {
                issues.Add(new(PathIssueCode.MissingGroup, index));
                continue;
            }
            if (!payload.IsValid || !ValidGroup(group))
                continue;

            var effective = ApplyEdits(path);
            foreach (var point in effective)
            {
                if (!ValidCoordinate(point, group))
                    issues.Add(new(PathIssueCode.InvalidCoordinate, index, point.PointId, group.TargetId));
                if (point.AnchorId is not Guid anchorId)
                    continue;
                var position = new PathPosition(point.X, point.Y);
                var key = (group.GroupId, anchorId);
                if (anchors.TryGetValue(key, out var existing) && existing != position)
                    issues.Add(new(PathIssueCode.ConflictingAnchor, index, point.PointId));
                else
                    anchors[key] = position;
            }

            if (previousGroup is not null && previousGroup.GroupId != group.GroupId)
            {
                endedGroups.Add(previousGroup.GroupId);
                if (endedGroups.Contains(group.GroupId))
                    issues.Add(new(PathIssueCode.CrossedGroups, index));
                if ((heldKeys.Count > 0 || heldButtons.Count > 0) &&
                    previousGroup.TargetId != group.TargetId)
                    issues.Add(new(PathIssueCode.TargetSwitchWhileHeld, index));
            }
            else if (previousEnd is MousePathPoint end && previousGroup is not null &&
                (end.AnchorId != effective[0].AnchorId || end.X != effective[0].X || end.Y != effective[0].Y))
            {
                issues.Add(new(PathIssueCode.DiscontinuousGroup, index, effective[0].PointId));
            }
            previousGroup = group;
            previousEnd = effective[^1];
        }
        return new(issues);
    }

    public static PathValidationResult ValidatePayload(MousePathDefinition? path, int stepIndex = -1)
    {
        if (path is null)
            return new([new(PathIssueCode.MissingPayload, stepIndex)]);
        if (path.Source is null || path.Processing is null || path.Timing is null || path.Edits is null)
            return new([new(PathIssueCode.InvalidStructure, stepIndex)]);

        var issues = new List<PathIssue>();
        if (!Enum.IsDefined(path.Mode) || !Enum.IsDefined(path.SourceKind) || !Enum.IsDefined(path.Timing.Mode))
            issues.Add(new(PathIssueCode.InvalidEnum, stepIndex));
        if (!ValidSettings(path))
            issues.Add(new(PathIssueCode.InvalidSettings, stepIndex));
        if (path.Source.Points.Count == 0)
            return new(issues.Append(new(PathIssueCode.EmptySource, stepIndex)));
        if (PointCost(path) > MousePathLimits.MaximumMacroPoints ||
            path.Edits.Count > MousePathLimits.MaximumMacroPoints)
            return new(issues.Append(new(PathIssueCode.TooManyPoints, stepIndex)));

        ValidatePoints(path.Source.Points, stepIndex, issues);
        if (issues.Count > 0)
            return new(issues);

        var known = path.Source.Points.ToDictionary(point => point.PointId);
        var edited = new HashSet<Guid>();
        foreach (var edit in path.Edits)
        {
            if (edit is null || edit.PointId == Guid.Empty || !edited.Add(edit.PointId) ||
                (edit.X is double x && !double.IsFinite(x)) ||
                (edit.Y is double y && !double.IsFinite(y)) || edit.OffsetUs < 0)
            {
                issues.Add(new(PathIssueCode.InvalidEdit, stepIndex, edit?.PointId));
                continue;
            }
            if (edit.InsertAfterPointId is Guid after)
            {
                if (known.ContainsKey(edit.PointId) || !known.ContainsKey(after) || edit.IsDeleted ||
                    edit.X is null || edit.Y is null || edit.OffsetUs is null)
                    issues.Add(new(PathIssueCode.InvalidEdit, stepIndex, edit.PointId));
                else
                    known.Add(edit.PointId, new(edit.PointId, edit.OffsetUs.Value, 0,
                        edit.X.Value, edit.Y.Value, null));
            }
            else if (!known.TryGetValue(edit.PointId, out var original) ||
                (edit.IsDeleted && original.AnchorId is not null))
            {
                issues.Add(new(PathIssueCode.InvalidEdit, stepIndex, edit.PointId));
            }
            else if (edit.IsDeleted)
            {
                // 后续插入不能再引用已经删除的节点，避免重放时访问脱链节点。
                known.Remove(edit.PointId);
            }
        }
        if (issues.Count == 0)
        {
            var effective = ApplyEdits(path);
            if (effective.Count > MousePathLimits.MaximumMacroPoints)
                issues.Add(new(PathIssueCode.TooManyPoints, stepIndex));
            else
                ValidatePoints(effective, stepIndex, issues);
        }
        return new(issues);
    }

    /// <summary>返回独立的有效点序列；调用方需先保证编辑引用在结构上有效。</summary>
    public static IReadOnlyList<MousePathPoint> ApplyEdits(MousePathDefinition path)
    {
        if (path.Edits.Count == 0)
            return path.Source.Points;

        var points = new LinkedList<MousePathPoint>(path.Source.Points);
        var nodes = new Dictionary<Guid, LinkedListNode<MousePathPoint>>();
        for (var node = points.First; node is not null; node = node.Next)
            nodes.Add(node.Value.PointId, node);
        foreach (var edit in path.Edits)
        {
            if (edit.InsertAfterPointId is Guid after)
            {
                var point = new MousePathPoint(edit.PointId, edit.OffsetUs!.Value, 0,
                    edit.X!.Value, edit.Y!.Value, null);
                nodes.Add(edit.PointId, points.AddAfter(nodes[after], point));
            }
            else if (edit.IsDeleted)
            {
                points.Remove(nodes[edit.PointId]);
            }
            else
            {
                var node = nodes[edit.PointId];
                node.Value = node.Value with
                {
                    X = edit.X ?? node.Value.X,
                    Y = edit.Y ?? node.Value.Y,
                    OffsetUs = edit.OffsetUs ?? node.Value.OffsetUs
                };
            }
        }
        return points.Select((point, index) => point with { Sequence = index }).ToArray();
    }

    private static void ValidatePoints(IReadOnlyList<MousePathPoint> points, int stepIndex, List<PathIssue> issues)
    {
        var ids = new HashSet<Guid>();
        MousePathPoint? previous = null;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.PointId == Guid.Empty || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                issues.Add(new(PathIssueCode.InvalidPoint, stepIndex, point.PointId));
            if (!ids.Add(point.PointId))
                issues.Add(new(PathIssueCode.DuplicatePoint, stepIndex, point.PointId));
            if (point.OffsetUs < 0 || point.OffsetUs > MousePathLimits.MaximumRecordingDurationUs ||
                point.Sequence < 0 || (index == 0 && point.OffsetUs != 0) ||
                (previous is MousePathPoint prior && (point.OffsetUs < prior.OffsetUs ||
                    (point.OffsetUs == prior.OffsetUs && point.Sequence <= prior.Sequence))))
                issues.Add(new(PathIssueCode.InvalidTimeOrder, stepIndex, point.PointId));
            if (point.AnchorId == Guid.Empty ||
                ((index == 0 || index == points.Count - 1) && point.AnchorId is null))
                issues.Add(new(PathIssueCode.InvalidAnchor, stepIndex, point.PointId));
            previous = point;
        }
    }

    private static bool ValidSettings(MousePathDefinition path)
    {
        var p = path.Processing;
        var t = path.Timing;
        return p.SamplingWindowUs is > 0 and <= MousePathLimits.MaximumRecordingDurationUs &&
            p.StillnessDurationUs is >= 0 and <= MousePathLimits.MaximumRecordingDurationUs &&
            p.SimplificationMaxTimeErrorUs is >= 0 and <= MousePathLimits.MaximumRecordingDurationUs &&
            Nonnegative(p.MovementThresholdPx) && Nonnegative(p.SmoothingMaxDeviationPx) &&
            Nonnegative(p.SimplificationMaxDeviationPx) && Positive(t.SpeedPxPerSecond) &&
            Positive(t.MaxSpeedPxPerSecond) && Positive(t.MaxAccelerationPxPerSecondSquared) &&
            t.FixedDurationUs is >= 0 and <= MousePathLimits.MaximumRecordingDurationUs &&
            (t.Mode != MousePathTimingMode.FixedDuration || t.FixedDurationUs > 0);
    }

    private static bool ValidGroup(MousePathGroup group)
    {
        var frame = group.RecordedFrame;
        var random = group.Randomization;
        return Enum.IsDefined(group.CoordinateMode) && frame is not null && random is not null &&
            double.IsFinite(frame.Left) && double.IsFinite(frame.Top) &&
            double.IsFinite(frame.Width) && frame.Width >= 1 && double.IsFinite(frame.Height) && frame.Height >= 1 &&
            double.IsFinite(group.Origin.X) && double.IsFinite(group.Origin.Y) &&
            Nonnegative(random.TranslationXPx) && Nonnegative(random.TranslationYPx) &&
            Nonnegative(random.JitterAmplitudePx) && Positive(random.JitterCorrelationDistancePx) &&
            (group.CoordinateMode == MousePathCoordinateMode.WindowClient || group.TargetId is null);
    }

    private static bool ValidCoordinate(MousePathPoint point, MousePathGroup group) =>
        group.CoordinateMode != MousePathCoordinateMode.WindowClient ||
        (point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1 &&
         (group.RecordedFrame.Width != 1 || point.X == 0) &&
         (group.RecordedFrame.Height != 1 || point.Y == 0));

    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    /// <summary>原始点仍被保留，插入点另外计费；删除编辑不能释放原始数据容量。</summary>
    public static long PointCost(MousePathDefinition? path) => (long)(path?.Source?.Points.Count ?? 0) +
        (path?.Edits?.Count(edit => edit?.InsertAfterPointId is not null) ?? 0);

    private static void TrackHeld(MacroStep step, HashSet<int> keys, HashSet<MouseButtonKind> buttons)
    {
        switch (step.ActionType)
        {
            case MacroActionType.KeyDown: keys.Add(KeyboardKeyIdentity.FromMacroStep(step).StorageKey); break;
            case MacroActionType.KeyUp: keys.Remove(KeyboardKeyIdentity.FromMacroStep(step).StorageKey); break;
            case MacroActionType.MouseButtonDown: buttons.Add(step.MouseButton); break;
            case MacroActionType.MouseButtonUp: buttons.Remove(step.MouseButton); break;
        }
    }
}
