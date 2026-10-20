using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    private IUpdatePackageDownloader? _updateDownloader;
    private CancellationTokenSource? _downloadCancellation;
    private PreparedUpdate? _preparedUpdate;
    private string? _downloadStatus;
    private bool _isPreparingUpdate;
    private double _updateDownloadPercent;

    /// <summary>由主窗口处理安装确认、未保存内容和退出交接；取消确认可返回成功而不退出。</summary>
    public Func<PreparedUpdate, Task<OperationResult>>? InstallUpdateRequested { get; set; }
    /// <summary>下载期间锁定版本与通道，用户仍可取消下载。</summary>
    public bool IsDownloadingUpdate => _downloadCancellation is not null;
    /// <summary>确认或准备退出期间禁止重复安装。</summary>
    public bool IsPreparingUpdate => _isPreparingUpdate;
    /// <summary>已接收字节占 GitHub 声明大小的百分比。</summary>
    public double UpdateDownloadPercent => _updateDownloadPercent;
    /// <summary>只有校验通过的本地安装包允许进入用户确认。</summary>
    public bool CanInstallUpdate => CanCheckForUpdates && _preparedUpdate is not null;
    /// <summary>取消操作名称。</summary>
    public string CancelUpdateDownloadText => Localize("取消下载", "Cancel download");
    /// <summary>安装操作明确告知即将退出应用。</summary>
    public string InstallUpdateText => Localize("退出并安装", "Exit and install");

    /// <summary>用户主动下载，成功后只准备安装包，不自动执行安装。</summary>
    public async Task DownloadUpdateAsync()
    {
        if (!CanDownloadUpdate || _updateResult is not { } release) return;
        if (_updateDownloader is null)
        {
            ShowError(Localize("更新下载服务不可用。", "Update download is unavailable."));
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleStopping.Token);
        _downloadCancellation = cancellation;
        _downloadStatus = Localize("正在下载更新…", "Downloading update…");
        _updateDownloadPercent = 0;
        RaiseDownloadStateChanged();
        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            if (_isDisposed || _downloadCancellation != cancellation) return;
            _updateDownloadPercent = value.Percent;
            OnPropertyChanged(nameof(UpdateDownloadPercent));
        });
        try
        {
            var result = await _updateDownloader.DownloadAsync(release, progress, cancellation.Token);
            if (_isDisposed) return;
            if (result.Succeeded)
            {
                _preparedUpdate = result.Value;
                _downloadStatus = Localize("更新已下载并通过校验，可以安装。", "Update downloaded and verified. Ready to install.");
            }
            else
                _downloadStatus = Localize("下载或校验失败，请重试。", "Download or verification failed. Please try again.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isDisposed) _downloadStatus = Localize("已取消下载", "Download canceled");
        }
        finally
        {
            _downloadCancellation = null;
            if (!_isDisposed) RaiseDownloadStateChanged();
        }
    }

    /// <summary>取消当前下载，保留发布信息以便重试。</summary>
    public void CancelUpdateDownload() => _downloadCancellation?.Cancel();

    /// <summary>将已校验安装包交给窗口协调，失败时保持应用运行并显示可操作的错误。</summary>
    public async Task InstallDownloadedUpdateAsync()
    {
        if (!CanInstallUpdate || _preparedUpdate is not { } package) return;
        if (InstallUpdateRequested is not { } install)
        {
            ShowError(Localize("无法启动安装。", "Could not start installation."));
            return;
        }
        _isPreparingUpdate = true;
        RaiseDownloadStateChanged();
        try
        {
            var result = await install(package);
            if (!_isDisposed && !result.Succeeded)
                ShowError(Localize("无法启动安装，请重试。", "Could not start installation. Please try again."));
        }
        finally
        {
            _isPreparingUpdate = false;
            if (!_isDisposed) RaiseDownloadStateChanged();
        }
    }

    /// <summary>新查询或通道切换后废弃旧包状态；已验证的缓存由下载器安全复用。</summary>
    private void ResetUpdateDownload()
    {
        _preparedUpdate = null;
        _downloadStatus = null;
        _updateDownloadPercent = 0;
        RaiseDownloadStateChanged();
    }

    /// <summary>集中刷新操作互斥与可访问的下载状态。</summary>
    private void RaiseDownloadStateChanged()
    {
        foreach (var name in new[] { nameof(IsDownloadingUpdate), nameof(IsPreparingUpdate),
            nameof(CanCheckForUpdates), nameof(CanDownloadUpdate), nameof(CanInstallUpdate),
            nameof(UpdateDownloadPercent), nameof(UpdateStatusText), nameof(HasUpdateStatus) })
            OnPropertyChanged(name);
    }

    /// <summary>语言切换时清除临时提示，保留安装和下载状态的本地化含义。</summary>
    private void RaiseDownloadLocalizationChanged()
    {
        _downloadStatus = IsDownloadingUpdate ? Localize("正在下载更新…", "Downloading update…")
            : _preparedUpdate is not null ? Localize("更新已下载并通过校验，可以安装。", "Update downloaded and verified. Ready to install.") : null;
        OnPropertyChanged(nameof(CancelUpdateDownloadText));
        OnPropertyChanged(nameof(InstallUpdateText));
    }
}
