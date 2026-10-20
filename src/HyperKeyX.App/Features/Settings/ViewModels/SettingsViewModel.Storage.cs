using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Abstractions.Storage;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Storage;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    private IStorageInspector _storageInspector = EmptyStorageInspector.Instance;
    private IPathLauncher _pathLauncher = UnavailablePathLauncher.Instance;
    private CancellationTokenSource? _storageRefreshCancellation;
    private long _storageRefreshVersion;
    private bool _hasLoadedStorage;
    private bool _isDisposed;

    [ObservableProperty]
    private bool _isStorageRefreshing;

    [ObservableProperty]
    private bool _isStorageExpanded;

    [ObservableProperty]
    private string _storageTotalSizeText = "0 B";

    public ObservableCollection<StorageAreaViewModel> StorageAreas { get; } = [];
    public IAsyncRelayCommand RefreshStorageCommand { get; private set; } = null!;
    public IRelayCommand<StorageAreaViewModel?> OpenStorageCommand { get; private set; } = null!;

    public string StorageText => Localize("存储空间", "Storage");
    public string StorageDescriptionText =>
        Localize("查看应用本地数据占用", "Review local application data usage");
    public string RefreshStorageText => Localize("刷新存储信息", "Refresh storage information");
    public string OpenFolderText => Localize("打开目录", "Open folder");
    public string StorageLoadingText => Localize("正在统计...", "Calculating...");

    /// <summary>每次进入通用设置重新统计，保留存储面板的展开状态；旧扫描由既有机制取消。</summary>
    public Task RefreshStorageOnGeneralEntryAsync() =>
        !_isDisposed && IsGeneralCategoryActive
            ? RefreshStorageCommand.ExecuteAsync(null)
            : Task.CompletedTask;

    public async Task EnsureStorageLoadedAsync()
    {
        if (_isDisposed)
            return;

        IsStorageExpanded = true;
        if (_hasLoadedStorage)
            return;

        await RefreshStorageCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopLifecycleOperations();
        var cancellation = Interlocked.Exchange(ref _storageRefreshCancellation, null);
        cancellation?.Cancel();
    }

    private void InitializeStorage(
        IStorageInspector storageInspector,
        IPathLauncher pathLauncher)
    {
        _storageInspector = storageInspector;
        _pathLauncher = pathLauncher;
        RefreshStorageCommand = new AsyncRelayCommand(
            RefreshStorageAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        OpenStorageCommand = new RelayCommand<StorageAreaViewModel?>(OpenStorage);
    }

    private async Task RefreshStorageAsync()
    {
        if (_isDisposed)
            return;

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _storageRefreshCancellation, cancellation);
        previous?.Cancel();
        var version = Interlocked.Increment(ref _storageRefreshVersion);
        IsStorageRefreshing = true;

        try
        {
            var usages = await _storageInspector.InspectAsync(cancellation.Token);
            if (_isDisposed ||
                cancellation.IsCancellationRequested ||
                version != Volatile.Read(ref _storageRefreshVersion))
            {
                return;
            }

            ReplaceStorageAreas(usages);
            _hasLoadedStorage = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 新刷新请求或对象释放会取消旧扫描，不向用户显示错误。
        }
        catch (Exception exception)
        {
            if (!_isDisposed && version == Volatile.Read(ref _storageRefreshVersion))
            {
                ShowError(Localize(
                    $"无法读取存储信息：{exception.Message}",
                    $"Could not inspect storage: {exception.Message}"));
            }
        }
        finally
        {
            var isCurrentRequest = Interlocked.CompareExchange(
                    ref _storageRefreshCancellation,
                    null,
                    cancellation) == cancellation;
            if (isCurrentRequest)
                IsStorageRefreshing = false;

            // 每次扫描自行释放取消源，避免替换请求过早释放仍在使用的 Token。
            cancellation.Dispose();
        }
    }

    private void OpenStorage(StorageAreaViewModel? area)
    {
        if (_isDisposed || area is null || !area.CanOpen)
            return;

        var result = _pathLauncher.OpenFolder(area.Path);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? Localize("未知错误", "Unknown error")
                : result.ErrorMessage;
            ShowError(Localize(
                $"无法打开目录：{detail}",
                $"Could not open folder: {detail}"));
        }
    }

    private void ReplaceStorageAreas(IReadOnlyList<StorageAreaUsage> usages)
    {
        var ordered = usages
            .Where(usage => Enum.IsDefined(usage.Id))
            .GroupBy(usage => usage.Id)
            .Select(group => group.First())
            .OrderBy(usage => usage.Id)
            .Select(usage => new StorageAreaViewModel(usage, IsEnglish))
            .ToArray();

        StorageAreas.Clear();
        foreach (var area in ordered)
            StorageAreas.Add(area);
        RefreshStorageTotal();
    }

    private void RefreshStorageLocalization()
    {
        foreach (var area in StorageAreas)
            area.UpdateLocalization(IsEnglish);
        RefreshStorageTotal();
    }

    private void RefreshStorageTotal()
    {
        long total = 0;
        foreach (var area in StorageAreas)
        {
            try
            {
                total = checked(total + area.Bytes);
            }
            catch (OverflowException)
            {
                total = long.MaxValue;
                break;
            }
        }

        StorageTotalSizeText = StorageSizeFormatter.Format(total);
    }

    private sealed class EmptyStorageInspector : IStorageInspector
    {
        public static EmptyStorageInspector Instance { get; } = new();

        public Task<IReadOnlyList<StorageAreaUsage>> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<StorageAreaUsage>>([]);
        }
    }

    private sealed class UnavailablePathLauncher : IPathLauncher
    {
        public static UnavailablePathLauncher Instance { get; } = new();

        public OperationResult OpenFolder(string path) =>
            OperationResult.Failure("目录启动服务不可用。");
    }
}
