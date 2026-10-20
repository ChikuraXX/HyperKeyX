using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface IPathLauncher
{
    OperationResult OpenFolder(string path);
}
