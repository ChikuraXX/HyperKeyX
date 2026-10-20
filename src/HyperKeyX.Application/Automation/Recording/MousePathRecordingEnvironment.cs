using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Application.Automation.Paths;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public sealed class MousePathRecordingEnvironment(IWindowTargetService windows, IPointerInputService pointer,
    IPhysicalInputStateService physicalState, RecordingControlRegions regions) : IMousePathRecordingEnvironment
{
    public bool AreAllInputsReleased() => physicalState.AreAllInputsReleased();
    public RecordingInputDisposition Classify(MouseInputEvent input) => regions.Classify(input);

    public IMousePathRecordingContextLease CaptureContext(MousePathRecordingRequest request)
    {
        request = request.DeepClone();
        var monitors = Ordered(windows.GetMonitors());
        if (monitors.Length == 0 || monitors.Any(r => !r.IsValid))
            throw new InvalidOperationException("无法读取有效显示器布局。");
        var position = pointer.GetPosition();
        var left = monitors.Min(r => r.Left);
        var top = monitors.Min(r => r.Top);
        var frame = new MousePathRecordedFrame { Left = left, Top = top,
            Width = monitors.Max(r => r.Right) - left + 1, Height = monitors.Max(r => r.Bottom) - top + 1 };
        WindowTargetSession? session = null;
        WindowGroupGuard? guard = null;
        try
        {
            if (request.CoordinateMode == MousePathCoordinateMode.WindowClient)
            {
                if (request.Target is null) throw new InvalidOperationException("客户区坐标录制需要明确的目标窗口。");
                var resolved = new WindowTargetResolver(windows).BindAll([request.Target], CancellationToken.None);
                if (!resolved.IsValid) throw new InvalidOperationException("录制目标窗口缺失、存在歧义或无法绑定。");
                session = resolved.Session!;
                var binding = session.Bindings[request.Target.TargetId];
                var geometry = windows.GetGeometry(binding);
                if (geometry is not { IsUsable: true } || !windows.IsForeground(binding))
                    throw new InvalidOperationException("请将录制目标置于前台并释放输入后开始。");
                frame = new() { Left = geometry.Client.Left, Top = geometry.Client.Top,
                    Width = geometry.Client.Width, Height = geometry.Client.Height };
                guard = new(session, windows, request.Target.TargetId, binding, geometry);
            }
            else if (request.Target is not null)
                throw new InvalidOperationException("当前坐标模式不能附带客户区窗口目标。");
            var context = new MousePathRecordingContext(position, request.CoordinateMode, frame, request.Target,
                request.Mode, request.Processing, request.Timing, request.Randomization);
            var lease = new Lease(context, monitors, windows, pointer, session, guard);
            session = null;
            return lease;
        }
        finally { session?.Dispose(); }
    }

    private static PathPixelRectangle[] Ordered(IEnumerable<PathPixelRectangle> source) =>
        source.OrderBy(r => r.Left).ThenBy(r => r.Top).ThenBy(r => r.Width).ThenBy(r => r.Height).ToArray();

    private sealed class Lease(MousePathRecordingContext context, PathPixelRectangle[] monitors,
        IWindowTargetService windows, IPointerInputService pointer, WindowTargetSession? session, WindowGroupGuard? guard)
        : IMousePathRecordingContextLease
    {
        private bool _disposed;
        public MousePathRecordingContext Context { get; } = context.DeepClone();
        public PathIssue? Validate()
        {
            if (_disposed) return new(PathIssueCode.CaptureInterrupted);
            if (guard?.Validate() is { } issue) return issue;
            if (!monitors.SequenceEqual(Ordered(windows.GetMonitors()))) return new(PathIssueCode.TargetChanged);
            if (guard is not null && !guard.Geometry.Client.Contains(pointer.GetPosition()))
                return new(PathIssueCode.InvalidCoordinate, TargetId: guard.TargetId);
            return null;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            session?.Dispose();
            if (session?.CleanupIssues.Count > 0) throw new InvalidOperationException("释放录制窗口监视失败。");
        }
    }
}
