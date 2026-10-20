namespace HyperKeyX.Application.Abstractions.Processes;

public interface IProcessNameResolver
{
    bool TryGetProcessName(int processId, out string processName);
}
