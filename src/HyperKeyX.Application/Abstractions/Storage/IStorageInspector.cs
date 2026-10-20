using HyperKeyX.Application.Storage;

namespace HyperKeyX.Application.Abstractions.Storage;

public interface IStorageInspector
{
    Task<IReadOnlyList<StorageAreaUsage>> InspectAsync(
        CancellationToken cancellationToken = default);
}
