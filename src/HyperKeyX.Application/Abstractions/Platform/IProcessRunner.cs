using HyperKeyX.Application.Platform;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}
