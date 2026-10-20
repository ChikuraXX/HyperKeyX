namespace HyperKeyX.Domain.Automation.Paths;

public readonly record struct MousePathPoint(
    Guid PointId, long OffsetUs, long Sequence,
    double X, double Y, Guid? AnchorId);

public readonly record struct PathPosition(double X, double Y);

public readonly record struct TimedPathPosition(long OffsetUs, PathPosition Position);

public enum MousePathMode { KeyPoints = 0, Continuous = 1 }
public enum MousePathCoordinateMode { RelativeStart = 0, VirtualDesktop = 1, WindowClient = 2 }
public enum MousePathTimingMode { Original = 0, FixedDuration = 1, ConstantSpeed = 2 }
public enum MousePathSourceKind { Recorded = 0, Manual = 1 }
public enum WindowTitleMatchMode { Exact = 0, Contains = 1 }
