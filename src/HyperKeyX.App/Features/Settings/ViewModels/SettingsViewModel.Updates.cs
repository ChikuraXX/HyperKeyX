using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Updates;
using System.Reflection;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    private readonly bool _updatesEnabled;
    private IReleaseUpdateService? _releaseUpdates;
    private IExternalLinkLauncher? _externalLinks;
    private UpdateCheckResult? _updateResult;
    private bool _isCheckingForUpdates;
    private bool _startupUpdateChecked;

    /// <summary>为更新功能注入网络查询与浏览器边界，也用于独立的设置测试。</summary>
    public SettingsViewModel(IAppPreferencesStore store, IReleaseUpdateService updates, IExternalLinkLauncher links,
        IUpdatePackageDownloader? downloader = null, bool enableUpdates = false)
        : this(store)
    {
        _releaseUpdates = updates;
        _externalLinks = links;
        _updateDownloader = downloader;
        _updatesEnabled = enableUpdates;
    }

    /// <summary>是否在启动时检查一次稳定版，默认关闭。</summary>
    public bool AutomaticUpdatesEnabled => AreUpdateFeaturesEnabled && _preferences.AutomaticUpdatesEnabled;
    /// <summary>启用后同时查询预览版与稳定版，默认关闭。</summary>
    public bool IncludePreviewUpdates => AreUpdateFeaturesEnabled && _preferences.IncludePreviewUpdates;
    /// <summary>发布开关由组合根控制；ZIP 分发阶段保留实现但默认关闭所有应用内更新入口。</summary>
    public bool AreUpdateFeaturesEnabled => _updatesEnabled;
    /// <summary>检查期间禁止重复提交。</summary>
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (!SetProperty(ref _isCheckingForUpdates, value)) return;
            OnPropertyChanged(nameof(CanCheckForUpdates));
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(CanDownloadUpdate));
            OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }
    /// <summary>只有未释放且空闲时允许检查。</summary>
    public bool CanCheckForUpdates => AreUpdateFeaturesEnabled && !_isDisposed && !IsCheckingForUpdates && !IsDownloadingUpdate && !IsPreparingUpdate;
    /// <summary>只有正在检查或已有结果时才展示版本检查状态。</summary>
    public bool HasUpdateStatus => !AreUpdateFeaturesEnabled || IsCheckingForUpdates || _updateResult is not null || _downloadStatus is not null;
    /// <summary>只有新稳定版的官方链接可用于下载入口。</summary>
    public bool CanDownloadUpdate => CanCheckForUpdates && _preparedUpdate is null &&
        _updateResult is { Status: UpdateCheckStatus.UpdateAvailable, Version: { } tag, InstallerDownload: { } installer } &&
        UpdatePackagePolicy.IsSha256(_updateResult.InstallerSha256) &&
        _updateResult.InstallerBytes is > 0 and <= UpdatePackagePolicy.MaximumBytes &&
        ReleaseVersion.TryParse(tag, out var release) &&
        ProjectLinks.IsInstallerDownload(installer, tag, $"HyperKeyX-{release.Value}-win-x64.msi");
    /// <summary>当前执行文件的版本，用于发布版本比较和显示。</summary>
    public Version CurrentVersion => typeof(SettingsViewModel).Assembly.GetName().Version ?? new(1, 0, 0, 0);
    /// <summary>保留程序集信息版本中的预览标识；提交哈希不参与发布版本比较。</summary>
    public string CurrentReleaseVersion => typeof(SettingsViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? CurrentVersion.ToString(3);

    /// <summary>自动检查开关标题。</summary>
    public string AutomaticUpdatesText => Localize("自动检查更新", "Automatically check for updates");
    /// <summary>自动检查的触发时机说明。</summary>
    public string AutomaticUpdatesDescriptionText => Localize("启动时检查所选更新通道", "Check the selected release channel on startup");
    /// <summary>预览版通道开关文本。</summary>
    public string PreviewUpdatesText => Localize("检查预览版更新", "Check for preview updates");
    /// <summary>手动检查按钮文本。</summary>
    public string CheckForUpdatesText => Localize("检查更新", "Check for updates");
    /// <summary>发布页下载入口文本。</summary>
    public string DownloadUpdateText => Localize("下载 MSI 更新", "Download MSI update");
    /// <summary>仓库设置项标题。</summary>
    public string RepositoryText => Localize("GitHub 仓库", "GitHub repository");
    /// <summary>仓库按钮文本。</summary>
    public string OpenRepositoryText => Localize("打开仓库", "Open repository");
    /// <summary>许可证入口文本。</summary>
    public string LicenseText => Localize("查看许可证", "View license");
    /// <summary>随界面语言切换的软件介绍。</summary>
    public string SoftwareDescriptionText => Localize(
        "Windows 键盘与鼠标宏工具，支持按键连发、可复用宏、鼠标轨迹和按进程切换配置。",
        "A Windows keyboard and mouse macro tool with turbo input, reusable macros, mouse paths, and process-specific profiles.");
    /// <summary>软件名称、当前版本及许可证摘要。</summary>
    public string SoftwareVersionText => $"HyperKeyX {CurrentVersion.ToString(3)} · LGPL-3.0";
    /// <summary>关于页的版本和平台摘要。</summary>
    public string VersionCaptionText => Localize($"版本 {CurrentReleaseVersion} | x64", $"Version {CurrentReleaseVersion} | x64");
    /// <summary>更新设置分组标题。</summary>
    public string UpdateSectionText => Localize("更新设置", "Update settings");
    /// <summary>项目链接分组标题。</summary>
    public string SupportSectionText => Localize("反馈", "Feedback");
    /// <summary>其他链接分组标题。</summary>
    public string OtherLinksText => Localize("其他链接", "Other links");
    /// <summary>错误反馈操作名称。</summary>
    public string ReportBugText => Localize("报告错误", "Report a bug");
    /// <summary>功能建议操作名称。</summary>
    public string FeatureRequestText => Localize("建议功能", "Suggest a feature");
    /// <summary>使用文档链接名称。</summary>
    public string UsageGuideText => Localize("使用说明", "User guide");
    /// <summary>历史发布链接名称。</summary>
    public string ReleaseHistoryText => Localize("更新记录", "Release notes");
    /// <summary>查看代码入口说明。</summary>
    public string RepositoryDescriptionText => Localize("查看源代码、项目介绍与开发进展", "Source code, project information, and development");
    /// <summary>发布记录入口标题。</summary>
    public string ReleasesText => Localize("版本发布与更新记录", "Releases & changelog");
    /// <summary>发布记录入口说明。</summary>
    public string ReleasesDescriptionText => Localize("查看历史版本、更新说明与下载文件", "Browse release notes, previous versions, and downloads");
    /// <summary>反馈入口标题。</summary>
    public string FeedbackText => Localize("问题反馈与功能建议", "Report an issue or suggest a feature");
    /// <summary>反馈入口说明。</summary>
    public string FeedbackDescriptionText => Localize("在 GitHub Issues 中反馈问题或提出建议", "Share bugs and feature requests on GitHub Issues");
    /// <summary>根据当前请求或结果生成本地化检查状态。</summary>
    public string UpdateStatusText => !AreUpdateFeaturesEnabled ? Localize("应用内更新暂未启用", "In-app updates are currently disabled")
        : IsPreparingUpdate ? Localize("正在准备安装…", "Preparing installation…")
        : _downloadStatus ?? (IsCheckingForUpdates
        ? Localize("正在检查更新…", "Checking for updates…")
        : _updateResult?.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => Localize($"发现新版本 {_updateResult.Version}", $"Update available: {_updateResult.Version}"),
            UpdateCheckStatus.UpToDate => Localize("当前已是最新版", "You are up to date"),
            UpdateCheckStatus.NoRelease => Localize("暂无可访问的发布版本", "No accessible release is available"),
            UpdateCheckStatus.RateLimited => Localize("GitHub 请求受限，请稍后重试", "GitHub request restricted; try again later"),
            UpdateCheckStatus.NetworkError => Localize("检查失败，请检查网络后重试", "Update check failed; check your connection and try again"),
            UpdateCheckStatus.InvalidRelease => Localize("发布信息无效，请前往 GitHub 查看", "Invalid release information; please check GitHub"),
            UpdateCheckStatus.PackageUnavailable => Localize($"发现 {_updateResult.Version}，但尚无可用的 x64 MSI 安装包", $"Release {_updateResult.Version} has no available x64 MSI installer"),
            _ => IncludePreviewUpdates ? Localize("从 GitHub 检查稳定版与预览版", "Check GitHub for stable and preview releases")
                : Localize("从 GitHub 检查最新稳定版", "Check GitHub for the latest stable release")
        });

    /// <summary>切换通道后清除旧结果；查询期间不改变通道，避免结果与当前设置不符。</summary>
    public void SetIncludePreviewUpdates(bool enabled)
    {
        if (!CanCheckForUpdates || IncludePreviewUpdates == enabled) return;
        if (!TrySave(_preferences with { IncludePreviewUpdates = enabled })) return;
        ResetUpdateDownload();
        _updateResult = null;
        OnPropertyChanged(nameof(IncludePreviewUpdates));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(HasUpdateStatus));
    }

    /// <summary>保存成功后才更改自动检查状态，不因开关事件立即发起网络请求。</summary>
    public void SetAutomaticUpdatesEnabled(bool enabled)
    {
        if (!AreUpdateFeaturesEnabled || _isDisposed || AutomaticUpdatesEnabled == enabled) return;
        if (TrySave(_preferences with { AutomaticUpdatesEnabled = enabled }))
            OnPropertyChanged(nameof(AutomaticUpdatesEnabled));
    }

    /// <summary>已启用时每次进程启动最多检查一次。</summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_isDisposed || _startupUpdateChecked || !AutomaticUpdatesEnabled) return;
        _startupUpdateChecked = true;
        await CheckForUpdatesAsync();
    }

    /// <summary>检查最新稳定版；保留 UI 上下文并在应用释放时取消。</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!CanCheckForUpdates) return;
        ResetUpdateDownload();
        _updateResult = null;
        IsCheckingForUpdates = true;
        try
        {
            var result = _releaseUpdates is null ? new(UpdateCheckStatus.NetworkError)
                : await _releaseUpdates.CheckAsync(CurrentReleaseVersion, includePreviewUpdates: IncludePreviewUpdates,
                    cancellationToken: _lifecycleStopping.Token);
            if (!_isDisposed) _updateResult = result;
        }
        catch (OperationCanceledException) when (_lifecycleStopping.IsCancellationRequested)
        {
            // 关闭应用时取消请求，不再向已释放的窗口显示错误。
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>打开官方仓库。</summary>
    public void OpenRepository() => OpenExternalPage(ProjectLinks.Repository);
    /// <summary>打开许可证声明。</summary>
    public void OpenLicense() => OpenExternalPage(ProjectLinks.License);
    /// <summary>打开发布历史与更新记录。</summary>
    public void OpenReleases() => OpenExternalPage(ProjectLinks.Releases);
    /// <summary>打开 GitHub 问题与建议列表。</summary>
    public void OpenFeedback() => OpenExternalPage(ProjectLinks.Issues);
    /// <summary>打开错误报告草稿，由用户确认提交。</summary>
    public void OpenBugReport() => OpenExternalPage(ProjectLinks.BugReport);
    /// <summary>打开功能建议草稿，由用户确认提交。</summary>
    public void OpenFeatureRequest() => OpenExternalPage(ProjectLinks.FeatureRequest);
    /// <summary>打开当前界面语言的快速开始文档。</summary>
    public void OpenUsageGuide() => OpenExternalPage(IsEnglish ? ProjectLinks.EnglishGuide : ProjectLinks.ChineseGuide);
    /// <summary>将浏览器启动失败翻译成现有设置错误提示。</summary>
    private void OpenExternalPage(Uri address)
    {
        if (_isDisposed) return;
        if (_externalLinks?.Open(address).Succeeded != true)
            ShowError(Localize("无法打开浏览器，请稍后重试。", "Could not open your browser. Please try again."));
    }

    /// <summary>语言切换后同步更新介绍、按钮和当前检查结果。</summary>
    private void RaiseUpdateLocalizationChanged()
    {
        RaiseDownloadLocalizationChanged();
        foreach (var name in new[] { nameof(AutomaticUpdatesText), nameof(AutomaticUpdatesDescriptionText),
            nameof(PreviewUpdatesText),
            nameof(VersionCaptionText), nameof(UpdateSectionText), nameof(SupportSectionText),
            nameof(RepositoryDescriptionText), nameof(ReleasesText), nameof(ReleasesDescriptionText),
            nameof(FeedbackText), nameof(FeedbackDescriptionText),
            nameof(OtherLinksText), nameof(ReportBugText), nameof(FeatureRequestText), nameof(UsageGuideText), nameof(ReleaseHistoryText),
            nameof(CheckForUpdatesText), nameof(DownloadUpdateText), nameof(RepositoryText), nameof(OpenRepositoryText),
            nameof(LicenseText), nameof(SoftwareDescriptionText), nameof(UpdateStatusText) })
            OnPropertyChanged(name);
    }
}
