namespace HyperKeyX.Domain.Automation.Paths;

public static class MousePathPlanner
{
    public static PathPlan Plan(MousePathProcessingResult processed, MousePathDefinition path,
        MousePathGroup group, PathCoordinateContext context, IPathRandomSource random,
        CancellationToken cancellationToken)
    {
        var result = PlanGroup([new(processed, path)], group, context, random, cancellationToken);
        return result.IsValid ? result.Plans[0] : new([], [], result.Issues);
    }

    public static PathGroupPlan PlanGroup(IReadOnlyList<MousePathPlanningInput> inputs, MousePathGroup group,
        PathCoordinateContext context, IPathRandomSource random, CancellationToken cancellationToken,
        Func<IReadOnlyList<PathPlan>, IReadOnlyList<PathIssue>>? validateCandidate = null,
        int maximumDerivedPoints = MousePathLimits.MaximumMacroPoints)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumDerivedPoints is <= 0 or > MousePathLimits.MaximumMacroPoints) throw new ArgumentOutOfRangeException(nameof(maximumDerivedPoints));
        if (inputs.Count == 0 || !ValidRandomSettings(group.Randomization))
            return new([], [new(PathIssueCode.InvalidSettings)]);
        var baselines = new List<PathPlan>();
        var anchors = new Dictionary<Guid, PathPosition>();
        long baselinePoints = 0;
        foreach (var input in inputs)
        {
            baselinePoints += input.Definition.Timing.Mode == MousePathTimingMode.ConstantSpeed
                ? input.Processed.Points.Count : input.Processed.DetailedPoints.Count;
            if (baselinePoints > maximumDerivedPoints) return new([], [new(PathIssueCode.TooManyPoints)]);
            var baseline = PlanBaseline(input.Processed, input.Definition, group, context, cancellationToken);
            if (!baseline.IsValid) return new([], baseline.Issues);
            foreach (var point in baseline.Points)
            {
                if (point.AnchorId is not { } id) continue;
                if (anchors.TryGetValue(id, out var position) && position != point.Position)
                    return new([], [new(PathIssueCode.ConflictingAnchor, PointId: point.PointId)]);
                anchors[id] = point.Position;
            }
            baselines.Add(baseline);
        }
        var randomized = group.Randomization.TranslationEnabled || group.Randomization.JitterEnabled;
        if (!randomized)
        {
            var issues = validateCandidate?.Invoke(baselines) ?? [];
            return issues.Count == 0 ? new(baselines, []) : new([], issues);
        }
        IReadOnlyList<PathIssue> lastIssues = [];
        for (var attempt = 0; attempt < MousePathLimits.MaximumRandomCandidates; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = PathRandomCandidate.Create(group.Randomization, random);
            var plans = new List<PathPlan>();
            var distance = 0d;
            var remainingPoints = maximumDerivedPoints;
            for (var i = 0; i < inputs.Count; i++)
            {
                var plan = Randomize(baselines[i], inputs[i].Definition, group, context,
                    candidate, ref distance, cancellationToken, remainingPoints);
                if (!plan.IsValid) { lastIssues = plan.Issues; break; }
                remainingPoints -= plan.Points.Count;
                plans.Add(plan);
            }
            if (plans.Count == inputs.Count)
            {
                // 最终量化/发送计划也属于同一次候选检查，不能再套一层十六次重试。
                lastIssues = validateCandidate?.Invoke(plans) ?? [];
                if (lastIssues.Count == 0) return new(plans, []);
            }
        }
        return new([], lastIssues.Append(new(PathIssueCode.RandomizationExhausted)));
    }

    private static PathPlan PlanBaseline(MousePathProcessingResult processed, MousePathDefinition path,
        MousePathGroup group, PathCoordinateContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!processed.IsValid)
            return new([], [], processed.Issues);
        var validation = MousePathValidator.ValidatePayload(path);
        if (!validation.IsValid)
            return new([], [], validation.Issues);
        if (processed.Points.Count == 0)
            return PathPlan.Failure(PathIssueCode.EmptySource);
        if (group.GroupId != path.GroupId)
            return PathPlan.Failure(PathIssueCode.MissingGroup);

        // 原始/固定时间用未简化的进度节点证明到达时间；恒速才采用简化后的有序弧长。
        var evidence = path.Timing.Mode == MousePathTimingMode.ConstantSpeed ? processed.Points : processed.DetailedPoints;
        var points = new ProcessedPathPoint[evidence.Count];
        for (var i = 0; i < points.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var resolved = MousePathCoordinateResolver.Resolve(evidence[i].Position, group, context);
            if (!resolved.IsValid)
                return new([], [], [resolved.Issue! with { PointId = evidence[i].PointId }]);
            points[i] = evidence[i] with { Position = resolved.Position };
        }
        return BuildTimedPlan(points, path, group, context, cancellationToken);
    }

    private static PathPlan BuildTimedPlan(ProcessedPathPoint[] source, MousePathDefinition path,
        MousePathGroup group, PathCoordinateContext context, CancellationToken token,
        PathPosition[]? prescribedVelocities = null)
    {
        var points = source.ToArray();
        var timing = path.Timing;
        var totalSourceTime = source[^1].OffsetUs;
        var totalLength = 0d;
        for (var i = 1; i < source.Length; i++)
            totalLength += PathGeometry.Distance(source[i - 1].Position, source[i].Position);
        if (!double.IsFinite(totalLength))
            return PathPlan.Failure(PathIssueCode.InvalidCoordinate);
        var traversed = 0d;
        long clock = 0;
        for (var i = 1; i < points.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var distance = PathGeometry.Distance(points[i - 1].Position, points[i].Position);
            traversed += distance;
            switch (timing.Mode)
            {
                case MousePathTimingMode.Original:
                    clock = source[i].OffsetUs;
                    break;
                case MousePathTimingMode.FixedDuration:
                    var progress = totalSourceTime > 0 ? (double)source[i].OffsetUs / totalSourceTime
                        : totalLength > 0 ? traversed / totalLength : (double)i / (points.Length - 1);
                    clock = (long)Math.Round(timing.FixedDurationUs * progress, MidpointRounding.AwayFromZero);
                    break;
                case MousePathTimingMode.ConstantSpeed:
                    var speed = Math.Min(timing.SpeedPxPerSecond, timing.MaxSpeedPxPerSecond);
                    var acceleration = timing.MaxAccelerationPxPerSecondSquared;
                    var seconds = distance <= speed * speed / acceleration
                        ? 2 * Math.Sqrt(distance / acceleration) : distance / speed + speed / acceleration;
                    var elapsed = distance == 0 ? source[i].OffsetUs - source[i - 1].OffsetUs : Math.Ceiling(seconds * 1_000_000);
                    if (!double.IsFinite(elapsed) || elapsed > MousePathLimits.MaximumRecordingDurationUs - clock)
                        return PathPlan.Failure(PathIssueCode.MotionLimitExceeded, points[i].PointId);
                    clock += (long)elapsed;
                    break;
            }
            if (clock < points[i - 1].OffsetUs || (clock == points[i - 1].OffsetUs && distance != 0))
                return PathPlan.Failure(PathIssueCode.MotionLimitExceeded, points[i].PointId);
            points[i] = points[i] with { OffsetUs = clock };
        }

        var velocities = prescribedVelocities ?? new PathPosition[points.Length];
        if (timing.Mode != MousePathTimingMode.ConstantSpeed && prescribedVelocities is null)
        {
            for (var i = 1; i + 1 < points.Length; i++)
            {
                var before = points[i].OffsetUs - points[i - 1].OffsetUs;
                var after = points[i + 1].OffsetUs - points[i].OffsetUs;
                if (before <= 0 || after <= 0 || points[i].Position == points[i - 1].Position ||
                    points[i].Position == points[i + 1].Position || points[i].IsHoldBoundary)
                    continue;
                var incoming = PathMotionSpan.Scale(PathMotionSpan.Subtract(points[i].Position, points[i - 1].Position), 1_000_000d / before);
                var outgoing = PathMotionSpan.Scale(PathMotionSpan.Subtract(points[i + 1].Position, points[i].Position), 1_000_000d / after);
                // 共用节点速度保持 C1 连续；反向时停下，避免 Hermite 曲线制造过冲。
                if (incoming.X * outgoing.X + incoming.Y * outgoing.Y <= 0)
                    continue;
                velocities[i] = PathGeometry.Lerp(incoming, outgoing, (double)before / (before + after));
                var scale = Math.Min(TangentScale(velocities[i], points[i - 1], points[i],
                    path.Processing.SimplificationMaxDeviationPx), TangentScale(velocities[i], points[i], points[i + 1],
                    path.Processing.SimplificationMaxDeviationPx));
                velocities[i] = PathMotionSpan.Scale(velocities[i], scale);
            }
            // 同一段的两端控制点保持进度单调，缩小共享速度只会收紧相邻段的约束。
            for (var i = 1; i < points.Length; i++)
            {
                var direction = PathMotionSpan.Subtract(points[i].Position, points[i - 1].Position);
                var length = PathGeometry.Distance(default, direction);
                if (length == 0) continue;
                var seconds = (points[i].OffsetUs - points[i - 1].OffsetUs) / 1_000_000d;
                var sum = (Dot(velocities[i - 1], direction) + Dot(velocities[i], direction)) * seconds / (3 * length * length);
                if (sum > 1)
                {
                    velocities[i - 1] = PathMotionSpan.Scale(velocities[i - 1], 1 / sum);
                    velocities[i] = PathMotionSpan.Scale(velocities[i], 1 / sum);
                }
            }
        }
        if (points.Any(p => !context.ContainsPosition(p.Position) ||
            (group.CoordinateMode == MousePathCoordinateMode.WindowClient &&
                (context.Client is not { } client || !client.Contains(p.Position)))))
            return PathPlan.Failure(PathIssueCode.UnreachablePosition);
        var segments = new List<PathMotionSpan>();
        for (var i = 1; i < points.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (points[i].OffsetUs == points[i - 1].OffsetUs)
                continue;
            var span = timing.Mode == MousePathTimingMode.ConstantSpeed
                ? PathMotionSpan.Cruise(points[i - 1], points[i], Math.Min(timing.SpeedPxPerSecond, timing.MaxSpeedPxPerSecond),
                    timing.MaxAccelerationPxPerSecondSquared)
                : PathMotionSpan.Cubic(points[i - 1], points[i], velocities[i - 1], velocities[i]);
            if (!span.SatisfiesMotion(timing.MaxSpeedPxPerSecond, timing.MaxAccelerationPxPerSecondSquared))
                return PathPlan.Failure(PathIssueCode.MotionLimitExceeded, points[i].PointId);
            if (!Reachable(span.Controls, group, context, 0, token))
                return PathPlan.Failure(PathIssueCode.UnreachablePosition, points[i].PointId);
            segments.Add(span);
        }
        return new(points, segments, []);
    }

    private static bool Reachable(IReadOnlyList<PathPosition> controls, MousePathGroup group,
        PathCoordinateContext context, int depth, CancellationToken token)
    {
        if (group.CoordinateMode == MousePathCoordinateMode.WindowClient &&
            (context.Client is not { } client || controls.Any(p => !client.Contains(p))))
            return false;
        if (context.Monitors.Any(m => controls.All(p => m.Contains(p))))
            return true;
        if (controls.All(p => OnSegment(controls[0], p, controls[3])))
            return context.ContainsSegment(controls[0], controls[3]);
        if (depth >= 16)
            return false;
        token.ThrowIfCancellationRequested();
        var a = PathGeometry.Lerp(controls[0], controls[1], .5);
        var b = PathGeometry.Lerp(controls[1], controls[2], .5);
        var c = PathGeometry.Lerp(controls[2], controls[3], .5);
        var d = PathGeometry.Lerp(a, b, .5);
        var e = PathGeometry.Lerp(b, c, .5);
        var middle = PathGeometry.Lerp(d, e, .5);
        return Reachable([controls[0], a, d, middle], group, context, depth + 1, token) &&
            Reachable([middle, e, c, controls[3]], group, context, depth + 1, token);
    }

    private static bool OnSegment(PathPosition first, PathPosition point, PathPosition last)
    {
        var length = PathGeometry.Distance(first, last);
        return Math.Abs(PathGeometry.Distance(first, point) + PathGeometry.Distance(point, last) - length) <= 1e-10;
    }

    private static double TangentScale(PathPosition velocity, ProcessedPathPoint first,
        ProcessedPathPoint last, double deviation)
    {
        var direction = PathMotionSpan.Subtract(last.Position, first.Position);
        var length = PathGeometry.Distance(default, direction);
        if (length == 0) return 0;
        var seconds = (last.OffsetUs - first.OffsetUs) / 1_000_000d;
        var projection = Dot(velocity, direction) / length;
        if (projection <= 0) return 0;
        var perpendicular = Math.Abs(velocity.X * direction.Y - velocity.Y * direction.X) / length * seconds / 3;
        return Math.Min(1, Math.Min(length * 3 / (projection * seconds), perpendicular > deviation ? deviation / perpendicular : 1));
    }

    private static double Dot(PathPosition first, PathPosition last) => first.X * last.X + first.Y * last.Y;

    private static bool ValidRandomSettings(MousePathRandomizationSettings? settings) => settings is not null &&
        double.IsFinite(settings.TranslationXPx) && settings.TranslationXPx >= 0 &&
        double.IsFinite(settings.TranslationYPx) && settings.TranslationYPx >= 0 &&
        double.IsFinite(settings.JitterAmplitudePx) && settings.JitterAmplitudePx >= 0 &&
        double.IsFinite(settings.JitterCorrelationDistancePx) && settings.JitterCorrelationDistancePx > 0;

    private static PathPlan Randomize(PathPlan baseline, MousePathDefinition definition, MousePathGroup group,
        PathCoordinateContext context, PathRandomCandidate random, ref double groupDistance, CancellationToken token, int maximumPoints)
    {
        if (maximumPoints <= 0) return PathPlan.Failure(PathIssueCode.TooManyPoints);
        var settings = group.Randomization;
        var points = new List<ProcessedPathPoint> { baseline.Points[0] };
        for (var i = 1; i < baseline.Points.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var first = baseline.Points[i - 1];
            var last = baseline.Points[i];
            var length = PathGeometry.Distance(first.Position, last.Position);
            var count = settings.JitterEnabled && settings.JitterAmplitudePx > 0
                ? Math.Max(1, Math.Ceiling(length / (settings.JitterCorrelationDistancePx / 4))) : 1;
            if (!double.IsFinite(count) || count > maximumPoints - points.Count)
                return PathPlan.Failure(PathIssueCode.TooManyPoints);
            for (var j = 1; j < count; j++)
            {
                if ((j & 1023) == 0) token.ThrowIfCancellationRequested();
                var time = first.OffsetUs + (long)((last.OffsetUs - first.OffsetUs) * j / count);
                if (time <= points[^1].OffsetUs || time >= last.OffsetUs) continue;
                points.Add(new(Guid.Empty, time, baseline.PositionAt(time), null, false));
            }
            points.Add(last);
        }

        var distances = new double[points.Count];
        distances[0] = groupDistance;
        for (var i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + PathGeometry.Distance(points[i - 1].Position, points[i].Position);
        groupDistance = distances[^1];
        var offsets = new PathPosition[points.Count];
        var previousAnchor = 0;
        for (var nextAnchor = 1; nextAnchor < points.Count; nextAnchor++)
        {
            if (!points[nextAnchor].IsProtected && nextAnchor != points.Count - 1) continue;
            for (var i = previousAnchor + 1; i < nextAnchor; i++)
                offsets[i] = random.Offset(distances[i], distances[i] - distances[previousAnchor],
                    distances[nextAnchor] - distances[i], settings);
            previousAnchor = nextAnchor;
        }

        var velocities = new PathPosition[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var noiseVelocity = default(PathPosition);
            if (i > 0 && i + 1 < points.Count && !points[i].IsProtected)
            {
                var duration = points[i + 1].OffsetUs - points[i - 1].OffsetUs;
                if (duration > 0)
                    noiseVelocity = PathMotionSpan.Scale(PathMotionSpan.Subtract(offsets[i + 1], offsets[i - 1]), 1_000_000d / duration);
                // 限制噪声控制向量而非夹位置；共享导数保持连续，整个插值偏移仍在幅度圆盘内。
                var scale = 1d;
                for (var shrink = 0; shrink < 32; shrink++)
                {
                    var before = PathMotionSpan.Subtract(offsets[i], PathMotionSpan.Scale(noiseVelocity,
                        scale * (points[i].OffsetUs - points[i - 1].OffsetUs) / 3_000_000d));
                    var after = PathMotionSpan.Add(offsets[i], PathMotionSpan.Scale(noiseVelocity,
                        scale * (points[i + 1].OffsetUs - points[i].OffsetUs) / 3_000_000d));
                    if (PathGeometry.Distance(default, before) <= settings.JitterAmplitudePx &&
                        PathGeometry.Distance(default, after) <= settings.JitterAmplitudePx) break;
                    scale *= .5;
                }
                noiseVelocity = PathMotionSpan.Scale(noiseVelocity, scale);
            }
            velocities[i] = PathMotionSpan.Add(baseline.VelocityAt(points[i].OffsetUs), noiseVelocity);
        }
        for (var i = 0; i < points.Count; i++)
            points[i] = points[i] with { Position = PathMotionSpan.Add(PathMotionSpan.Add(points[i].Position,
                random.Translation), offsets[i]) };
        return BuildTimedPlan(points.ToArray(), definition, group, context, token,
            definition.Timing.Mode == MousePathTimingMode.ConstantSpeed ? null : velocities);
    }
}
