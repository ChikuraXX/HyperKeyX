namespace HyperKeyX.Application.Platform;

public readonly record struct ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
