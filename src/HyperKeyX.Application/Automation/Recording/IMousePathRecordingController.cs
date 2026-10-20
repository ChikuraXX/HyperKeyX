using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Automation.Recording;

public interface IMousePathRecordingController : IMacroRecordingService
{
    Task<OperationResult> StartPathAsync(MousePathRecordingRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult> ChangePathContextAsync(MousePathRecordingRequest request, CancellationToken cancellationToken = default);
}
