using System.ComponentModel;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Profiles.Views;

/// <summary>
/// 配置文件页面承载配置集合和目标进程操作。
/// </summary>
public sealed partial class ProfilesPage : Page
{
    private bool _isScaleMetricsSubscribed;

    public MainViewModel ViewModel { get; }

    public ProfilesPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScaleMetricsSubscription();
        UpdateResponsiveState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScaleMetricsSubscription();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveState();

    private void OnScaleMetricsPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        UpdateResponsiveState();

    private void AttachScaleMetricsSubscription()
    {
        if (_isScaleMetricsSubscribed)
            return;

        ViewModel.Settings.UiScaleMetrics.PropertyChanged += OnScaleMetricsPropertyChanged;
        _isScaleMetricsSubscribed = true;
    }

    private void DetachScaleMetricsSubscription()
    {
        if (!_isScaleMetricsSubscribed)
            return;

        ViewModel.Settings.UiScaleMetrics.PropertyChanged -= OnScaleMetricsPropertyChanged;
        _isScaleMetricsSubscribed = false;
    }

    private void UpdateResponsiveState()
    {
        var threshold = ViewModel.Settings.UiScaleMetrics.ProfilesWideWidth;
        var wide = ProfilesWorkspace.ActualWidth >= threshold;
        // 直接更新已命名布局元素，避免 Page 根节点上的状态组未被应用。
        ProfileListColumn.Width = wide
            ? new GridLength(ViewModel.Settings.UiScaleMetrics.ProfileListWidth)
            : new GridLength(1, GridUnitType.Star);
        ProfileDetailsColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ProfileDetailsRow.Height = wide ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumnSpan(ProfileListPanel, wide ? 1 : 2);
        Grid.SetColumn(ProfileDetailsPanel, wide ? 1 : 0);
        Grid.SetRow(ProfileDetailsPanel, wide ? 0 : 1);
        Grid.SetColumnSpan(ProfileDetailsPanel, wide ? 1 : 2);
        ProfileDetailsPanel.Padding = wide ? new Thickness(24, 0, 0, 0) : new Thickness(0, 20, 0, 0);
        ProfileDetailsPanel.BorderThickness = wide ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0);
        ProfileList.MaxHeight = wide ? 480 : 240;
    }

    private void OnAddProfile(object sender, RoutedEventArgs e)
    {
        ViewModel.AddProfileCommand.Execute(null);
    }

    private void OnExportProfiles(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ExportProfilesCommand.ExecuteAsync(null);
    }

    private void OnImportProfiles(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ImportProfilesCommand.ExecuteAsync(null);
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteProfileCommand.Execute(ViewModel.SelectedProfile);
    }
}
