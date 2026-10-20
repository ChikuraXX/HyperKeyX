using System.Security;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Abstractions.Storage;
using HyperKeyX.Application.Storage;

namespace HyperKeyX.Infrastructure.Storage;

public sealed class StorageInspector : IStorageInspector
{
    private readonly IAppPaths _paths;
    private readonly IStorageFileSystem _fileSystem;

    public StorageInspector(IAppPaths paths)
        : this(paths, new PhysicalStorageFileSystem())
    {
    }

    internal StorageInspector(IAppPaths paths, IStorageFileSystem fileSystem)
    {
        _paths = paths;
        _fileSystem = fileSystem;
    }

    public Task<IReadOnlyList<StorageAreaUsage>> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<StorageAreaUsage>>(
            () => InspectCore(cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<StorageAreaUsage> InspectCore(CancellationToken cancellationToken) =>
    [
        InspectArea(
            StorageAreaId.AppData,
            _paths.RootDirectory,
            recursive: false,
            cancellationToken),
        InspectArea(
            StorageAreaId.Assets,
            _paths.AssetsDirectory,
            recursive: true,
            cancellationToken),
        InspectArea(
            StorageAreaId.Cache,
            _paths.CacheDirectory,
            recursive: true,
            cancellationToken),
        InspectArea(
            StorageAreaId.Logs,
            _paths.LogsDirectory,
            recursive: true,
            cancellationToken)
    ];

    private StorageAreaUsage InspectArea(
        StorageAreaId id,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exists = _fileSystem.DirectoryExists(path);
        var bytes = exists
            ? GetDirectorySize(path, recursive, cancellationToken)
            : 0;
        return new StorageAreaUsage(id, path, bytes, exists, Warning: null);
    }

    private long GetDirectorySize(
        string root,
        bool recursive,
        CancellationToken cancellationToken)
    {
        if (IsReparsePoint(root))
            return 0;

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var file in SafeEnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(file))
                    continue;

                try
                {
                    total = checked(total + _fileSystem.GetFileLength(file));
                }
                catch (Exception exception) when (IsTransientFileSystemException(exception))
                {
                    // 枚举后文件可能被删除或暂时无法访问，跳过该文件并继续统计。
                }
                catch (OverflowException)
                {
                    return long.MaxValue;
                }
            }

            if (!recursive)
                continue;

            foreach (var child in SafeEnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsReparsePoint(child))
                    pending.Push(child);
            }
        }

        return total;
    }

    private IReadOnlyList<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return _fileSystem.EnumerateFiles(directory);
        }
        catch (Exception exception) when (IsTransientFileSystemException(exception))
        {
            return [];
        }
    }

    private IReadOnlyList<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return _fileSystem.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (IsTransientFileSystemException(exception))
        {
            return [];
        }
    }

    private bool IsReparsePoint(string path)
    {
        try
        {
            return (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (IsTransientFileSystemException(exception))
        {
            // 属性读取失败时采用保守策略，不进入该路径。
            return true;
        }
    }

    private static bool IsTransientFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}
