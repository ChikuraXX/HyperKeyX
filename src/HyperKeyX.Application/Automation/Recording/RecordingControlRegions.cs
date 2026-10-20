using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

/// <summary>UI 预先发布的物理矩形快照；钩子仅做有限次点包含判断。</summary>
public sealed class RecordingControlRegions
{
    private RegionSnapshot _snapshot = new([], []);

    public void Update(IEnumerable<PathPixelRectangle> stopControls, IEnumerable<PathPixelRectangle> excludedControls)
    {
        var snapshot = new RegionSnapshot(stopControls.ToArray(), excludedControls.ToArray());
        if (snapshot.Stop.Concat(snapshot.Excluded).Any(r => !r.IsValid))
            throw new ArgumentException("录制控制区域必须为有效物理矩形。");
        Volatile.Write(ref _snapshot, snapshot);
    }

    public RecordingInputDisposition Classify(MouseInputEvent input)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (input.Position is { } point)
        {
            if (input.Kind == MouseInputKind.ButtonDown && snapshot.Stop.Any(r => r.Contains(point)))
                return RecordingInputDisposition.StopControl;
            if (snapshot.Stop.Concat(snapshot.Excluded).Any(r => r.Contains(point)))
                return RecordingInputDisposition.Excluded;
        }
        return input.TargetProcessId == Environment.ProcessId ? RecordingInputDisposition.Excluded : RecordingInputDisposition.Record;
    }

    private sealed record RegionSnapshot(PathPixelRectangle[] Stop, PathPixelRectangle[] Excluded);
}
