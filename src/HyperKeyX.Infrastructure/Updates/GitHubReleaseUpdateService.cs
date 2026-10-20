using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Infrastructure.Updates;

/// <summary>查询官方稳定版或预览发布，以有界响应校验语义版本及 x64 MSI 资产。</summary>
public sealed class GitHubReleaseUpdateService(HttpClient client) : IReleaseUpdateService
{
    /// <summary>兼容原有稳定版检查入口。</summary>
    public Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default) =>
        CheckAsync(currentVersion.ToString(), includePreviewUpdates: false, cancellationToken);

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, bool includePreviewUpdates,
        CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current)) return new(UpdateCheckStatus.InvalidRelease);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                includePreviewUpdates ? ProjectLinks.ReleaseListApi : ProjectLinks.LatestReleaseApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HyperKeyX", current.Number.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateCheckStatus.NoRelease);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) return new(UpdateCheckStatus.RateLimited);
            if (!response.IsSuccessStatusCode) return new(UpdateCheckStatus.NetworkError);
            await response.Content.LoadIntoBufferAsync(1024 * 1024, deadline.Token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false));
            var root = json.RootElement;
            if (includePreviewUpdates && root.ValueKind != JsonValueKind.Array ||
                !includePreviewUpdates && root.ValueKind != JsonValueKind.Object) return new(UpdateCheckStatus.InvalidRelease);
            var releases = includePreviewUpdates ? root.EnumerateArray().ToArray() : new[] { root };
            Candidate? newest = null;
            var invalid = false;
            foreach (var release in releases)
            {
                if (release.ValueKind != JsonValueKind.Object ||
                    !TryBoolean(release, "draft", out var draft) || !TryBoolean(release, "prerelease", out var preview))
                { invalid = true; continue; }
                if (draft || !includePreviewUpdates && preview) continue;
                var tag = GetString(release, "tag_name");
                if (!ReleaseVersion.TryParse(tag, out var version) ||
                    !includePreviewUpdates && version.IsPrerelease ||
                    !Uri.TryCreate(GetString(release, "html_url"), UriKind.Absolute, out var page) || !ProjectLinks.IsReleasePage(page))
                { invalid = true; continue; }
                if (tag is null) { invalid = true; continue; }
                var candidate = new Candidate(tag, version, page, FindInstaller(release, tag, version.Value));
                if (newest is null || version.CompareTo(newest.Version) > 0) newest = candidate;
            }
            if (newest is null) return new(invalid ? UpdateCheckStatus.InvalidRelease : UpdateCheckStatus.NoRelease);
            var status = newest.Version.CompareTo(current) <= 0 ? UpdateCheckStatus.UpToDate
                : newest.Installer is null ? UpdateCheckStatus.PackageUnavailable : UpdateCheckStatus.UpdateAvailable;
            return new(status, newest.Tag, newest.Page, newest.Installer?.Download,
                newest.Installer?.Sha256, newest.Installer?.Bytes ?? 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(UpdateCheckStatus.NetworkError); }
        catch (HttpRequestException) { return new(UpdateCheckStatus.NetworkError); }
        catch (JsonException) { return new(UpdateCheckStatus.InvalidRelease); }
        catch (IOException) { return new(UpdateCheckStatus.NetworkError); }
    }

    /// <summary>下载只接受正大小且文件名、标签、仓库均匹配的 x64 MSI，不回退到其他架构或任意资产。</summary>
    private static InstallerAsset? FindInstaller(JsonElement release, string tag, string version)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var expected = $"HyperKeyX-{version}-win-x64.msi";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || GetString(asset, "name") != expected ||
                !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes <= 0 || bytes > UpdatePackagePolicy.MaximumBytes ||
                !Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var download) ||
                !ProjectLinks.IsInstallerDownload(download, tag, expected)) continue;
            var digest = GetString(asset, "digest");
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                !UpdatePackagePolicy.IsSha256(digest[7..])) continue;
            return new(download, digest[7..].ToLowerInvariant(), bytes);
        }
        return null;
    }

    /// <summary>拒绝类型错误的字符串字段。</summary>
    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>GitHub 发布标记必须是明确的布尔值。</summary>
    private static bool TryBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var field) || field.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = field.GetBoolean();
        return true;
    }

    /// <summary>经过仓库、版本及安装包边界校验的候选发布。</summary>
    private sealed record Candidate(string Tag, ReleaseVersion Version, Uri Page, InstallerAsset? Installer);

    /// <summary>必须同时提供官方地址、受限大小和 GitHub SHA-256 摘要。</summary>
    private sealed record InstallerAsset(Uri Download, string Sha256, long Bytes);
}
