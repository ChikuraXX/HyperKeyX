namespace HyperKeyX.Application.Storage;

public sealed record StorageAreaUsage(
    StorageAreaId Id,
    string Path,
    long Bytes,
    bool Exists,
    string? Warning);
