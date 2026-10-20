namespace HyperKeyX.Application.Updates;

/// <summary>所有下载及安装边界共用的安装包大小与 SHA-256 格式约束。</summary>
public static class UpdatePackagePolicy
{
    /// <summary>单个 MSI 最大 512 MiB，防止错误发布信息导致无界下载。</summary>
    public const long MaximumBytes = 512L * 1024 * 1024;
    /// <summary>接受 64 位 ASCII 十六进制 SHA-256，不接受缺失哈希。</summary>
    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// <summary>安装包下载进度，单位为字节。</summary>
public readonly record struct UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>用于界面的百分比，范围 0..100。</summary>
    public double Percent => TotalBytes > 0 ? Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100) : 0;
}
