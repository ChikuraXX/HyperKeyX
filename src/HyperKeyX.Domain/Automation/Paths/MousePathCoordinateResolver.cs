namespace HyperKeyX.Domain.Automation.Paths;

public readonly record struct PathCoordinateResult(PathPosition Position, PathIssue? Issue)
{
    public bool IsValid => Issue is null;
}

public static class MousePathCoordinateResolver
{
    public static PathCoordinateResult Resolve(PathPosition source, MousePathGroup group,
        PathCoordinateContext context)
    {
        if (!PathGeometry.IsFinite(source) || !context.IsValid)
            return new(default, new(PathIssueCode.InvalidCoordinate));

        PathPosition position;
        switch (group.CoordinateMode)
        {
            case MousePathCoordinateMode.RelativeStart:
                position = new(context.Origin.X + source.X, context.Origin.Y + source.Y);
                break;
            case MousePathCoordinateMode.VirtualDesktop:
                position = source;
                break;
            case MousePathCoordinateMode.WindowClient:
                if (context.Client is not { IsValid: true } client)
                    return new(default, new(PathIssueCode.TargetUnavailable, TargetId: group.TargetId));
                if (source.X is < 0 or > 1 || source.Y is < 0 or > 1 ||
                    (client.Width == 1 && source.X != 0) || (client.Height == 1 && source.Y != 0))
                    return new(default, new(PathIssueCode.InvalidCoordinate, TargetId: group.TargetId));
                position = new(client.Left + source.X * (client.Width - 1),
                    client.Top + source.Y * (client.Height - 1));
                break;
            default:
                return new(default, new(PathIssueCode.InvalidEnum));
        }
        return context.ContainsPosition(position)
            ? new(position, null) : new(default, new(PathIssueCode.UnreachablePosition));
    }

    // 预览、计划与原生发送统一使用中点远离零，避免负坐标出现不同舍入结果。
    public static PathPosition Quantize(PathPosition position) => new(
        Math.Round(position.X, MidpointRounding.AwayFromZero),
        Math.Round(position.Y, MidpointRounding.AwayFromZero));
}
