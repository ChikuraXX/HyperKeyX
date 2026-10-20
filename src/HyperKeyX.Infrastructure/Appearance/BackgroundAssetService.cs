using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Appearance;

namespace HyperKeyX.Infrastructure.Appearance;

public sealed class BackgroundAssetService : IBackgroundAssetService
{
    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private readonly IAppPaths _paths;
    private readonly SemaphoreSlim _importLock = new(1, 1);

    public BackgroundAssetService(IAppPaths paths)
    {
        _paths = paths;
    }

    public async Task<BackgroundAssetImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var lockTaken = false;
        string? tempPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (IsRemotePath(fullSourcePath))
            {
                return BackgroundAssetImportResult.Failure(
                    "请选择本机磁盘中的背景图片。");
            }

            if (!File.Exists(fullSourcePath))
            {
                return BackgroundAssetImportResult.Failure("未找到所选背景图片。");
            }

            var extension = Path.GetExtension(fullSourcePath);
            if (!SupportedExtensions.Contains(extension))
            {
                return BackgroundAssetImportResult.Failure(
                    "仅支持 PNG、JPG、JPEG 或 WEBP 图片。");
            }

            await _importLock.WaitAsync(cancellationToken);
            lockTaken = true;
            Directory.CreateDirectory(_paths.AssetsDirectory);

            var assetName = $"custom-background{extension.ToLowerInvariant()}";
            var targetPath = Path.Combine(_paths.AssetsDirectory, assetName);
            tempPath = targetPath + ".tmp";

            await using (var source = new FileStream(
                             fullSourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             useAsync: true))
            await using (var target = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            tempPath = null;
            DeleteOtherBackgroundFormats(assetName);
            return BackgroundAssetImportResult.Success(assetName);
        }
        catch (OperationCanceledException)
        {
            return BackgroundAssetImportResult.Failure("已取消导入背景图片。");
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return BackgroundAssetImportResult.Failure(exception.Message);
        }
        finally
        {
            DeleteTemporaryFile(tempPath);
            if (lockTaken)
            {
                _importLock.Release();
            }
        }
    }

    public string? ResolvePath(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) ||
            !string.Equals(assetName, Path.GetFileName(assetName), StringComparison.Ordinal) ||
            !SupportedExtensions.Contains(Path.GetExtension(assetName)))
        {
            return null;
        }

        try
        {
            var assetsDirectory = Path.GetFullPath(_paths.AssetsDirectory);
            var directoryPrefix = Path.TrimEndingDirectorySeparator(assetsDirectory) +
                                  Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(assetsDirectory, assetName));
            return candidate.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return null;
        }
    }

    public bool Exists(string? assetName)
    {
        var path = ResolvePath(assetName);
        return path is not null && File.Exists(path);
    }

    private void DeleteOtherBackgroundFormats(string retainedAssetName)
    {
        foreach (var extension in SupportedExtensions)
        {
            var assetName = $"custom-background{extension}";
            if (string.Equals(assetName, retainedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.Combine(_paths.AssetsDirectory, assetName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static bool IsRemotePath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
            return true;

        try
        {
            var root = Path.GetPathRoot(path);
            return root is null || new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            // 无法确认路径归属时按非本地路径处理，避免意外触发远程访问。
            return true;
        }
    }

    private static void DeleteTemporaryFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BackgroundAssetService] 清理临时文件失败: {exception.Message}");
        }
    }
}
