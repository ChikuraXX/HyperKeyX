using HyperKeyX.Features.Automation.Controls;
using HyperKeyX.Features.Mouse.Controls;
using HyperKeyX.Shell.Navigation;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace HyperKeyX.Features.Mouse.Views;

/// <summary>
/// 鼠标页面承载按钮选择和当前触发器配置操作。
/// </summary>
public sealed partial class MousePage : Page
{
    private bool _isScaleSubscribed;
    public MainViewModel ViewModel { get; }

    public MousePage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        MouseButtonSelectorHost.Content = new MouseButtonSelectorControl(viewModel);
        TriggerInspectorHost.Content = new TriggerInspectorControl(viewModel);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isScaleSubscribed)
        {
            ViewModel.Settings.UiScaleMetrics.PropertyChanged += OnScaleChanged;
            _isScaleSubscribed = true;
        }
        UpdateWorkspaceLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isScaleSubscribed)
            return;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged -= OnScaleChanged;
        _isScaleSubscribed = false;
    }

    private void OnScaleChanged(object? sender, PropertyChangedEventArgs e) => UpdateWorkspaceLayout();

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWorkspaceLayout();

    /// <summary>先保证鼠标按钮的可读宽度，再分配检查器位置；不重建页面或编辑器。</summary>
    private void UpdateWorkspaceLayout()
    {
        if (MouseWorkspace.ActualWidth <= 0)
            return;
        var metrics = ViewModel.Settings.UiScaleMetrics;
        var stacked = metrics.ShouldStackMouse(MouseWorkspace.ActualWidth);
        MouseWorkspace.RowDefinitions[0].Height = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        MouseWorkspace.RowDefinitions[1].Height = stacked ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(TriggerInspectorHost, stacked ? 0 : 1);
        Grid.SetRow(TriggerInspectorHost, stacked ? 1 : 0);
        TriggerInspectorHost.Width = stacked ? double.NaN : metrics.EditorPanelWidth;
        // 上下布局整体可滚动，避免上方选择区把参数区域挤到零高度。
        TriggerInspectorHost.Height = stacked ? Math.Min(400 * metrics.Factor, 600) : double.NaN;
        WorkspaceScrollViewer.VerticalScrollMode = stacked ? ScrollMode.Enabled : ScrollMode.Disabled;
        WorkspaceScrollViewer.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

    }

    private void OnOpenProfiles(object sender, RoutedEventArgs e)
    {
        ViewModel.Navigation.ActiveSection = MainSection.Profiles;
    }

}
