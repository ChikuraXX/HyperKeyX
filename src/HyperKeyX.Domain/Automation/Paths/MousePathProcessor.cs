namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>纯轨迹处理：只生成派生点，绝不覆盖源块或自动移动受保护锚点。</summary>
public static class MousePathProcessor
{
    public static MousePathProcessingResult Process(MousePathDefinition path,
        MousePathGroup group, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = MousePathValidator.ValidatePayload(path);
        if (!validation.IsValid)
            return new([], validation.Issues);
        if (group.GroupId != path.GroupId || !Enum.IsDefined(group.CoordinateMode) ||
            group.RecordedFrame is null || !double.IsFinite(group.RecordedFrame.Width) ||
            !double.IsFinite(group.RecordedFrame.Height) || group.RecordedFrame.Width < 1 ||
            group.RecordedFrame.Height < 1)
            return new([], [new(PathIssueCode.InvalidGroup)]);

        var effective = MousePathValidator.ApplyEdits(path);
        var points = new List<ProcessedPathPoint>(effective.Count);
        var anchorCoordinates = new Dictionary<Guid, PathPosition>();
        var explicitPoints = path.Edits.Select(e => e.PointId).ToHashSet();
        for (var i = 0; i < effective.Count; i++)
        {
            if ((i & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var point = effective[i];
            var position = ToPhysical(new(point.X, point.Y), group);
            if (!PathGeometry.IsFinite(position))
                return new([], [new(PathIssueCode.InvalidCoordinate, PointId: point.PointId)]);
            // 用户明确编辑或手工指定的关键点参与插值，不能再次被自动简化吞掉。
            var protect = point.AnchorId is not null || explicitPoints.Contains(point.PointId) ||
                path.Mode == MousePathMode.KeyPoints && path.SourceKind == MousePathSourceKind.Manual;
            points.Add(new(point.PointId, point.OffsetUs, position, point.AnchorId, protect));
            if (point.AnchorId is not null)
                anchorCoordinates[point.PointId] = new(point.X, point.Y);
        }

        var excluded = new List<Guid>();
        var issues = FilterOutliers(points, path.Processing, excluded, cancellationToken);
        if (issues.Count > 0)
            return new([], issues, excluded);
        MarkHolds(points, path.Processing, cancellationToken);
        if (path.Processing.SmoothingEnabled)
            Smooth(points, path.Processing.SmoothingMaxDeviationPx, cancellationToken);

        var reductionSource = points;
        // 采样窗口只优化派生表示，转折/时间误差不允许时保留更多点。
        points = ReduceSamplingWindows(points, path.Processing, cancellationToken);
        if (path.Processing.SimplificationEnabled)
            points = Simplify(points, path.Processing, cancellationToken);
        if (!ReductionWithinBudget(reductionSource, points, path.Processing, cancellationToken))
            points = reductionSource;

        // 最终插值仍需原始进度证据；简化预览不能成为丢弃时间约束的依据。
        var detailedPoints = reductionSource.Select(point => point with
        {
            Position = anchorCoordinates.TryGetValue(point.PointId, out var anchor)
                ? anchor : FromPhysical(point.Position, group)
        }).ToArray();

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            // 原生锚点直接取有效源值，避免正反坐标转换带来舍入漂移。
            var position = anchorCoordinates.TryGetValue(point.PointId, out var anchor)
                ? anchor : FromPhysical(point.Position, group);
            points[i] = point with { Position = position };
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(points, [], excluded, detailedPoints);
    }

    private static List<PathIssue> FilterOutliers(List<ProcessedPathPoint> points,
        MousePathProcessingSettings settings, List<Guid> excluded, CancellationToken token)
    {
        var candidates = new List<int>();
        var near = Math.Max(settings.MovementThresholdPx, 1);
        var far = Math.Max(near * 4, 8);
        var window = Math.Max(settings.SamplingWindowUs * 4, 32_000);
        for (var i = 1; i + 1 < points.Count; i++)
        {
            if ((i & 1023) == 0)
                token.ThrowIfCancellationRequested();
            if (points[i + 1].OffsetUs - points[i - 1].OffsetUs <= window &&
                PathGeometry.Distance(points[i - 1].Position, points[i + 1].Position) <= near &&
                PathGeometry.Distance(points[i - 1].Position, points[i].Position) > far &&
                PathGeometry.Distance(points[i].Position, points[i + 1].Position) > far)
                candidates.Add(i);
        }
        var issues = new List<PathIssue>();
        for (var j = 0; j < candidates.Count; j++)
        {
            var index = candidates[j];
            if (points[index].IsProtected || (j > 0 && index - candidates[j - 1] <= 2) ||
                (j + 1 < candidates.Count && candidates[j + 1] - index <= 2))
                issues.Add(new(PathIssueCode.UnresolvedOutlier, PointId: points[index].PointId));
        }
        if (issues.Count != 0)
            return issues;

        var rejected = candidates.ToHashSet();
        var retained = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (rejected.Contains(i))
                excluded.Add(points[i].PointId);
            else
                points[retained++] = points[i];
        }
        points.RemoveRange(retained, points.Count - retained);
        return issues;
    }

    private static void MarkHolds(List<ProcessedPathPoint> points,
        MousePathProcessingSettings settings, CancellationToken token)
    {
        for (var start = 0; start + 1 < points.Count; start++)
        {
            if ((start & 1023) == 0)
                token.ThrowIfCancellationRequested();
            var reference = points[start].Position;
            var end = start;
            while (end + 1 < points.Count)
            {
                if (((end - start) & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                var next = points[end + 1];
                var distance = PathGeometry.Distance(reference, next.Position);
                if (distance > settings.MovementThresholdPx || (next.IsProtected && distance != 0))
                    break;
                end++;
                if (next.IsProtected)
                    break;
            }
            if (end > start && points[end].OffsetUs - points[start].OffsetUs >= settings.StillnessDurationUs)
            {
                // 固定参考点而非逐样本漂移；未达静止时间的慢移完整保留。
                points[start] = points[start] with { IsProtected = true, IsHoldBoundary = true };
                for (var i = start + 1; i <= end; i++)
                    points[i] = points[i] with { Position = reference };
                points[end] = points[end] with { IsProtected = true, IsHoldBoundary = true };
                start = end - 1;
            }
            else if (end > start)
            {
                // 不确认的候选不删除起步样本；继续从距离阈值外的下一点检测。
                start = end;
            }
        }
    }

    private static void Smooth(List<ProcessedPathPoint> points, double maximumDeviation, CancellationToken token)
    {
        var original = points.ToArray();
        for (var i = 1; i + 1 < points.Count; i++)
        {
            if ((i & 1023) == 0)
                token.ThrowIfCancellationRequested();
            if (original[i].IsProtected)
                continue;
            var span = original[i + 1].OffsetUs - original[i - 1].OffsetUs;
            if (span <= 0)
                continue;
            var progress = (double)(original[i].OffsetUs - original[i - 1].OffsetUs) / span;
            var interpolated = PathGeometry.Lerp(original[i - 1].Position, original[i + 1].Position, progress);
            var candidate = PathGeometry.Lerp(original[i].Position, interpolated, .5);
            var distance = PathGeometry.Distance(original[i].Position, candidate);
            if (distance > maximumDeviation)
                candidate = PathGeometry.Lerp(original[i].Position, candidate, maximumDeviation / distance);
            points[i] = original[i] with { Position = candidate };
        }
    }

    private static List<ProcessedPathPoint> ReduceSamplingWindows(List<ProcessedPathPoint> points,
        MousePathProcessingSettings settings, CancellationToken token)
    {
        if (points.Count < 3)
            return points;
        var output = new List<ProcessedPathPoint> { points[0] };
        var start = 0;
        while (start + 1 < points.Count)
        {
            token.ThrowIfCancellationRequested();
            var end = start + 1;
            while (end + 1 < points.Count && !points[end].IsProtected && end - start < 64 &&
                points[end + 1].OffsetUs - points[start].OffsetUs <= settings.SamplingWindowUs)
                end++;
            if (end == start + 1)
                output.Add(points[end]);
            else
                output.AddRange(Simplify(points.GetRange(start, end - start + 1), settings, token).Skip(1));
            start = end;
        }
        return output;
    }

    private static List<ProcessedPathPoint> Simplify(List<ProcessedPathPoint> points,
        MousePathProcessingSettings settings, CancellationToken token)
    {
        if (points.Count < 3)
            return points;
        var keep = new bool[points.Count];
        var ranges = new Stack<(int First, int Last)>();
        var previous = 0;
        keep[0] = true;
        for (var i = 1; i < points.Count; i++)
        {
            if (!points[i].IsProtected && i != points.Count - 1)
                continue;
            keep[i] = true;
            ranges.Push((previous, i));
            previous = i;
        }

        long workBudget = Math.Max(points.Count * 128L, 4096);
        while (ranges.TryPop(out var range))
        {
            token.ThrowIfCancellationRequested();
            if (workBudget < range.Last - range.First)
            {
                // 病态折线的优化预算耗尽时保留所有点，不能为省时扩大误差。
                Array.Fill(keep, true, range.First, range.Last - range.First + 1);
                continue;
            }
            var worst = -1;
            var worstError = 1d;
            for (var i = range.First + 1; i < range.Last; i++)
            {
                workBudget--;
                if ((i & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                var error = ReplacementError(points[range.First], points[i], points[range.Last], settings);
                if (error > worstError)
                {
                    worstError = error;
                    worst = i;
                }
            }
            if (worst < 0)
                continue;
            keep[worst] = true;
            ranges.Push((range.First, worst));
            ranges.Push((worst, range.Last));
        }
        return points.Where((_, index) => keep[index]).ToList();
    }

    private static bool ReductionWithinBudget(IReadOnlyList<ProcessedPathPoint> source,
        IReadOnlyList<ProcessedPathPoint> reduced, MousePathProcessingSettings settings, CancellationToken token)
    {
        var start = 0;
        for (var segment = 1; segment < reduced.Count; segment++)
        {
            token.ThrowIfCancellationRequested();
            var end = start + 1;
            while (end < source.Count && source[end].PointId != reduced[segment].PointId)
                end++;
            if (end == source.Count)
                return false;
            var previousProgress = 0d;
            for (var i = start + 1; i < end; i++)
            {
                if ((i & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                // 两轮优化必须回看同一份输入，不能各自消耗一次完整误差预算。
                if (source[i].IsProtected || ReplacementError(reduced[segment - 1], source[i], reduced[segment], settings) > 1)
                    return false;
                var progress = ProjectionProgress(reduced[segment - 1].Position, source[i].Position, reduced[segment].Position);
                // 时间容差不是抹去折返的许可，否则恒速模式会得到错误弧长。
                if (progress + 1e-12 < previousProgress)
                    return false;
                previousProgress = progress;
            }
            start = end;
        }
        return start == source.Count - 1;
    }

    private static double ReplacementError(ProcessedPathPoint first, ProcessedPathPoint point,
        ProcessedPathPoint last, MousePathProcessingSettings settings)
    {
        var dx = last.Position.X - first.Position.X;
        var dy = last.Position.Y - first.Position.Y;
        var length = PathGeometry.Distance(first.Position, last.Position);
        if (length == 0)
            return first.Position == point.Position ? 0 : double.PositiveInfinity;
        if (!double.IsFinite(length))
            return double.PositiveInfinity;
        var progress = ((point.Position.X - first.Position.X) * (dx / length) +
                        (point.Position.Y - first.Position.Y) * (dy / length)) / length;
        if (progress < 0 || progress > 1)
            return double.PositiveInfinity;
        var spatial = PathGeometry.Distance(point.Position, PathGeometry.Lerp(first.Position, last.Position, progress));
        var expectedTime = first.OffsetUs + (last.OffsetUs - first.OffsetUs) * progress;
        return Math.Max(Ratio(spatial, settings.SimplificationMaxDeviationPx),
            Ratio(Math.Abs(point.OffsetUs - expectedTime), settings.SimplificationMaxTimeErrorUs));
    }

    private static double Ratio(double value, double tolerance) =>
        tolerance > 0 ? value / tolerance : value == 0 ? 0 : double.PositiveInfinity;

    private static double ProjectionProgress(PathPosition first, PathPosition point, PathPosition last)
    {
        var length = PathGeometry.Distance(first, last);
        if (length == 0)
            return 0;
        return ((point.X - first.X) * ((last.X - first.X) / length) +
                (point.Y - first.Y) * ((last.Y - first.Y) / length)) / length;
    }

    private static PathPosition ToPhysical(PathPosition point, MousePathGroup group) =>
        group.CoordinateMode == MousePathCoordinateMode.WindowClient
            ? new(point.X * (group.RecordedFrame.Width - 1), point.Y * (group.RecordedFrame.Height - 1)) : point;

    private static PathPosition FromPhysical(PathPosition point, MousePathGroup group) =>
        group.CoordinateMode == MousePathCoordinateMode.WindowClient
            ? new(group.RecordedFrame.Width == 1 ? 0 : point.X / (group.RecordedFrame.Width - 1),
                group.RecordedFrame.Height == 1 ? 0 : point.Y / (group.RecordedFrame.Height - 1)) : point;
}
