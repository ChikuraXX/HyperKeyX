using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Infrastructure.Updates;

/// <summary>将官方 x64 MSI 流式下载到按摘要隔离的应用缓存，并在发布前核对长度与 SHA-256。</summary>
public sealed class MsiUpdateDownloader : IUpdatePackageDownloader
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private const int BufferSize = 81920;
    private readonly HttpClient _client;
    private readonly IAppPaths _paths;

    /// <summary>创建只写入应用缓存的 MSI 下载器。</summary>
    public MsiUpdateDownloader(HttpClient client, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(paths);
        _client = client;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PreparedUpdate>> DownloadAsync(
        UpdateCheckResult release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryValidateRelease(release, out var package, out var validationError))
        {
            return OperationResult<PreparedUpdate>.Failure(validationError);
        }

        var cacheDirectory = Path.Combine(_paths.CacheDirectory, "Updates", package.Sha256);
        var installerPath = Path.Combine(cacheDirectory, package.FileName);
        string? partialPath = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadTimeout);

        try
        {
            Directory.CreateDirectory(cacheDirectory);
            if (File.Exists(installerPath))
            {
                if (await IsVerifiedFileAsync(installerPath, package.Bytes, package.Sha256, deadline.Token)
                    .ConfigureAwait(false))
                {
                    progress?.Report(new(package.Bytes, package.Bytes));
                    return Success(release.Version!, installerPath, package);
                }

                File.Delete(installerPath);
            }

            partialPath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}.part");
            using var request = new HttpRequestMessage(HttpMethod.Get, release.InstallerDownload);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HyperKeyX", package.UserAgentVersion));
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"安装包下载失败，HTTP 状态码为 {(int)response.StatusCode}。";
                LogFailure(message);
                return OperationResult<PreparedUpdate>.Failure(message);
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength != package.Bytes)
            {
                return OperationResult<PreparedUpdate>.Failure("安装包响应长度与发布信息不一致。");
            }

            progress?.Report(new(0, package.Bytes));
            long bytesReceived = 0;
            string actualSha256;
            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (bytesReceived > package.Bytes - read)
                    {
                        return OperationResult<PreparedUpdate>.Failure("安装包内容超过发布信息声明的大小。");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
                    bytesReceived += read;
                    progress?.Report(new(bytesReceived, package.Bytes));
                }

                await destination.FlushAsync(deadline.Token).ConfigureAwait(false);
                actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (bytesReceived != package.Bytes)
            {
                return OperationResult<PreparedUpdate>.Failure("安装包实际长度与发布信息不一致。");
            }

            if (!string.Equals(actualSha256, package.Sha256, StringComparison.Ordinal))
            {
                return OperationResult<PreparedUpdate>.Failure("安装包 SHA-256 校验失败。");
            }

            File.Move(partialPath, installerPath);
            partialPath = null;
            return Success(release.Version!, installerPath, package);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure("下载安装包超时。", exception);
            return OperationResult<PreparedUpdate>.Failure("下载安装包超时。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            HttpRequestException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            LogFailure("下载安装包失败。", exception);
            return OperationResult<PreparedUpdate>.Failure("下载安装包失败，请稍后重试。");
        }
        finally
        {
            DeletePartialFile(partialPath);
        }
    }

    /// <summary>在发起网络请求前收紧发布标签、官方地址、大小和摘要。</summary>
    private static bool TryValidateRelease(
        UpdateCheckResult release,
        out ValidatedPackage package,
        out string error)
    {
        package = default!;
        error = "发布信息无效，无法下载安装包。";
        if (release.Status != UpdateCheckStatus.UpdateAvailable ||
            !ReleaseVersion.TryParse(release.Version, out var version) ||
            release.InstallerDownload is null ||
            !UpdatePackagePolicy.IsSha256(release.InstallerSha256) ||
            release.InstallerBytes <= 0 ||
            release.InstallerBytes > UpdatePackagePolicy.MaximumBytes)
        {
            return false;
        }

        var fileName = $"HyperKeyX-{version.Value}-win-x64.msi";
        if (!ProjectLinks.IsInstallerDownload(release.InstallerDownload, release.Version!, fileName))
        {
            return false;
        }

        package = new(
            fileName,
            release.InstallerSha256!.ToLowerInvariant(),
            release.InstallerBytes,
            version.Number.ToString());
        error = string.Empty;
        return true;
    }

    /// <summary>缓存命中也重新读取整个文件，防止复用残缺或被替换的安装包。</summary>
    private static async Task<bool> IsVerifiedFileAsync(
        string path,
        long expectedBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedBytes) return false;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>构造仅指向已校验缓存文件的成功结果。</summary>
    private static OperationResult<PreparedUpdate> Success(
        string version,
        string installerPath,
        ValidatedPackage package) =>
        OperationResult<PreparedUpdate>.Success(new(
            version,
            installerPath,
            package.Sha256,
            package.Bytes));

    /// <summary>按基础设施层现有约定在恢复边界记录一次预期下载失败。</summary>
    private static void LogFailure(string message, Exception? exception = null) =>
        Debug.WriteLine(exception is null
            ? $"[MsiUpdateDownloader] {message}"
            : $"[MsiUpdateDownloader] {message} {exception}");

    /// <summary>尽力移除本次下载的未完成文件；清理失败不会遮蔽原始结果。</summary>
    private static void DeletePartialFile(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[MsiUpdateDownloader] 清理未完成文件失败: {exception}");
        }
    }

    /// <summary>经过信任边界校验、可安全用于缓存路径的安装包元数据。</summary>
    private sealed record ValidatedPackage(
        string FileName,
        string Sha256,
        long Bytes,
        string UserAgentVersion);
}
