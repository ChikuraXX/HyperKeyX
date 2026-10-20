namespace HyperKeyX.Application.Updates;

/// <summary>官方代码仓库与发布入口，供更新服务和界面共同使用。</summary>
public static class ProjectLinks
{
    /// <summary>官方 GitHub 仓库。</summary>
    public static Uri Repository { get; } = new("https://github.com/ChikuraXX/HyperKeyX");
    /// <summary>最新稳定版 API；公开仓库不需要访问令牌。</summary>
    public static Uri LatestReleaseApi { get; } = new("https://api.github.com/repos/ChikuraXX/HyperKeyX/releases/latest");
    /// <summary>公开发布列表，供包含预览版的通道使用。</summary>
    public static Uri ReleaseListApi { get; } = new("https://api.github.com/repos/ChikuraXX/HyperKeyX/releases?per_page=30");
    /// <summary>仓库中的项目授权声明。</summary>
    public static Uri License { get; } = new("https://github.com/ChikuraXX/HyperKeyX/blob/HEAD/LICENSE");
    /// <summary>全部发布与更新记录。</summary>
    public static Uri Releases { get; } = new("https://github.com/ChikuraXX/HyperKeyX/releases");
    /// <summary>问题反馈与功能建议。</summary>
    public static Uri Issues { get; } = new("https://github.com/ChikuraXX/HyperKeyX/issues");
    /// <summary>预填错误类型的问题草稿，不自动提交。</summary>
    public static Uri BugReport { get; } = new("https://github.com/ChikuraXX/HyperKeyX/issues/new?title=%5BBug%5D%20");
    /// <summary>预填建议类型的问题草稿，不自动提交。</summary>
    public static Uri FeatureRequest { get; } = new("https://github.com/ChikuraXX/HyperKeyX/issues/new?title=%5BFeature%5D%20");
    /// <summary>英文快速开始文档。</summary>
    public static Uri EnglishGuide { get; } = new("https://github.com/ChikuraXX/HyperKeyX/blob/HEAD/README.md#getting-started");
    /// <summary>中文快速开始文档。</summary>
    public static Uri ChineseGuide { get; } = new("https://github.com/ChikuraXX/HyperKeyX/blob/HEAD/README.zh-CN.md#快速开始");

    /// <summary>只接受官方仓库下的 HTTPS 发布标签页。</summary>
    public static bool IsReleasePage(Uri address) =>
        address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps &&
        address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        address.IsDefaultPort && string.IsNullOrEmpty(address.UserInfo) &&
        address.AbsolutePath.StartsWith("/ChikuraXX/HyperKeyX/releases/tag/", StringComparison.Ordinal) &&
        address.AbsolutePath.Length > "/ChikuraXX/HyperKeyX/releases/tag/".Length;

    /// <summary>只接受官方仓库中与标签和文件名精确对应的 HTTPS 安装包地址。</summary>
    public static bool IsInstallerDownload(Uri address, string tag, string fileName) =>
        address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps &&
        address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        address.IsDefaultPort && string.IsNullOrEmpty(address.UserInfo) &&
        string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment) &&
        Uri.UnescapeDataString(address.AbsolutePath) == $"/ChikuraXX/HyperKeyX/releases/download/{tag}/{fileName}";
}
