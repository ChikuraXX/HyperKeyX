using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Automation.Recording;

public interface IMacroRecordingService
{
    event EventHandler<MacroRecordingStatus>? StatusChanged;
    event EventHandler<MacroRecordingResult>? Completed;

    MacroRecordingStatus CurrentStatus { get; }

    Task<OperationResult> StartAsync(
        CancellationToken cancellationToken = default);

    Task<MacroRecordingResult> StopAsync(
        MacroRecordingStopReason reason,
        CancellationToken cancellationToken = default);
}
