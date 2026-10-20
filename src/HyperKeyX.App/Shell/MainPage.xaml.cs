using System.ComponentModel;
using HyperKeyX.Features.Keyboard.Views;
using HyperKeyX.Features.Macros.Views;
using HyperKeyX.Features.Mouse.Views;
using HyperKeyX.Features.Profiles.Views;
using HyperKeyX.Features.Settings.Views;
using HyperKeyX.Shell.Appearance;
using HyperKeyX.Shell.Navigation;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HyperKeyX.Shell;

/// <summary>
/// 主页面只负责主导航、页面承载和运行操作。
/// </summary>
public sealed partial class MainPage : Page
{
    private bool _areSubscriptionsAttached;
    private bool _widePanePreference = true;
    private bool _wasNarrow;
    private bool _isApplyingResponsivePane;
    private double _appliedCompactPaneLength = double.NaN;

    public MainViewModel ViewModel { get; }

    internal WindowAppearanceHost CreateAppearanceHost(FrameworkElement applicationRoot) =>
        new(applicationRoot, CustomBackgroundImage, BackgroundOverlay);

    public MainPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        KeyboardHost.Content = new KeyboardPage(viewModel);
        MouseHost.Content = new MousePage(viewModel);
        MacroLibraryHost.Content = new MacroLibraryPage(viewModel);
        ProfilesHost.Content = new ProfilesPage(viewModel);
        SettingsHost.Content = new SettingsPage(viewModel.Settings);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SynchronizeSelectedNavigationItem();
    }

    private void OnNavigationItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem item || item.Tag is not string tag)
            return;

        if (string.Equals(tag, "run", StringComparison.OrdinalIgnoreCase))
        {
            // 运行操作不参与页面选择，执行后保留当前导航高亮。
            ViewModel.ToggleOrchestratorCommand.Execute(null);
            SynchronizeSelectedNavigationItem();
            return;
        }

        _ = ViewModel.Navigation.TryNavigate(tag);
        SynchronizeSelectedNavigationItem();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSubscriptions();
        SynchronizeSelectedNavigationItem();
        UpdateResponsivePane();
        UpdateNavigationTemplateMetrics();
        RefreshGeneralStorageOnEntry();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _appliedCompactPaneLength = double.NaN;
        DetachSubscriptions();
    }

    private void OnMainPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsivePane();

    private void OnNavigationPaneOpened(NavigationView sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdateNavigationTemplateMetrics);
        if (!_isApplyingResponsivePane && !_wasNarrow)
            _widePanePreference = true;
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdateNavigationTemplateMetrics);
        if (!_isApplyingResponsivePane && !_wasNarrow)
            _widePanePreference = false;
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainNavigationState.ActiveSection))
        {
            SynchronizeSelectedNavigationItem();
            RefreshGeneralStorageOnEntry();
        }
    }

    private void RefreshGeneralStorageOnEntry()
    {
        if (IsLoaded && ViewModel.Navigation.ActiveSection == MainSection.Settings)
            _ = ViewModel.Settings.RefreshStorageOnGeneralEntryAsync();
    }

    private void OnUiScaleMetricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModel.Settings.UiScaleMetrics.Factor))
            return;
        UpdateResponsivePane();
        // 等待绑定更新后再同步原生模板容器，避免图标被固定 16px 的 Viewbox 缩回。
        DispatcherQueue.TryEnqueue(UpdateNavigationTemplateMetrics);
    }

    /// <summary>同步原生导航模板中未暴露为公共属性的图标容器和底部菜单空间。</summary>
    private void UpdateNavigationTemplateMetrics()
    {
        if (!IsLoaded) return;
        RootNavigation.ApplyTemplate();
        var metrics = ViewModel.Settings.UiScaleMetrics;
        RefreshCompactPaneGeometry(metrics.NavigationCompactPaneLength);
        foreach (var item in RootNavigation.MenuItems.Concat(RootNavigation.FooterMenuItems).OfType<NavigationViewItem>())
        {
            item.ApplyTemplate();
            foreach (var element in NavigationTemplateElements(item))
            {
                if (element is Viewbox { Name: "IconBox" } iconBox)
                    iconBox.Height = metrics.IconGlyphSize;
                if (element is Grid { Name: "ContentGrid" } content)
                    content.MinHeight = metrics.NavigationItemHeight;
            }
        }
        foreach (var element in NavigationTemplateElements(RootNavigation))
        {
            if (element is ScrollViewer { Name: "FooterItemsScrollViewer" } footer)
            {
                footer.MinHeight = metrics.NavigationFooterMinHeight;
                footer.HorizontalScrollMode = ScrollMode.Disabled;
                footer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                footer.VerticalScrollMode = ScrollMode.Disabled;
                footer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
        }
    }

    /// <summary>
    /// 比例改变后重新应用紧凑状态的宽度和裁剪值。原生状态 Setter 可能仍保留进入状态时的旧值，
    /// 仅更新 CompactPaneLength 会让菜单变宽而内容区占位不变。不切换实际 IsPaneOpen。
    /// </summary>
    private void RefreshCompactPaneGeometry(double compactPaneLength)
    {
        if (_appliedCompactPaneLength == compactPaneLength)
            return;
        var split = NavigationTemplateElements(RootNavigation)
            .OfType<SplitView>().FirstOrDefault(element => element.Name == "RootSplitView");
        if (split is null)
            return;

        split.ApplyTemplate();
        if (!RootNavigation.IsPaneOpen && !split.IsPaneOpen &&
            split.DisplayMode is SplitViewDisplayMode.CompactInline or SplitViewDisplayMode.CompactOverlay)
        {
            // 无过渡地重新进入同一视觉状态，确保列宽与 PaneClip 使用最新 TemplateSettings。
            VisualStateManager.GoToState(RootNavigation, "ListSizeFull", false);
            VisualStateManager.GoToState(RootNavigation, "ListSizeCompact", false);
            VisualStateManager.GoToState(split, "Closed", false);
            var state = split.PanePlacement == SplitViewPanePlacement.Left
                ? "ClosedCompactLeft" : "ClosedCompactRight";
            if (!VisualStateManager.GoToState(split, state, false))
                return;
        }
        _appliedCompactPaneLength = compactPaneLength;
    }

    /// <summary>只访问导航模板，跳过页面内容，避免缩放时扫描业务页面。</summary>
    private static IEnumerable<FrameworkElement> NavigationTemplateElements(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: "PageContentHost" }) continue;
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in NavigationTemplateElements(child)) yield return descendant;
        }
    }

    private void OnCustomBackgroundImageFailed(
        object sender,
        ExceptionRoutedEventArgs args)
    {
        CustomBackgroundImage.Source = null;
        CustomBackgroundImage.Visibility = Visibility.Collapsed;
        var background = AppearanceRoot.ActualTheme == ElementTheme.Dark
            ? ColorHelper.FromArgb(255, 32, 32, 32)
            : Colors.White;
        BackgroundOverlay.Background = new SolidColorBrush(background);
        ViewModel.Settings.ShowWarning(ViewModel.Settings.IsEnglish
            ? "The custom background image could not be displayed."
            : "无法显示自定义背景图片。");
    }

    private void SynchronizeSelectedNavigationItem()
    {
        RootNavigation.SelectedItem = ViewModel.Navigation.ActiveSection switch
        {
            MainSection.Mouse => MouseNavigationItem,
            MainSection.Macros => MacrosNavigationItem,
            MainSection.Profiles => ProfilesNavigationItem,
            MainSection.Settings => SettingsNavigationItem,
            _ => KeyboardNavigationItem
        };
    }

    private void UpdateResponsivePane()
    {
        var isNarrow = ActualWidth < ViewModel.Settings.UiScaleMetrics.NavigationCollapseWidth;
        if (isNarrow)
        {
            if (!_wasNarrow)
                _widePanePreference = RootNavigation.IsPaneOpen;

            _isApplyingResponsivePane = true;
            RootNavigation.IsPaneOpen = false;
            _isApplyingResponsivePane = false;
        }
        else if (_wasNarrow)
        {
            _isApplyingResponsivePane = true;
            RootNavigation.IsPaneOpen = _widePanePreference;
            _isApplyingResponsivePane = false;
        }

        _wasNarrow = isNarrow;
    }

    private void AttachSubscriptions()
    {
        if (_areSubscriptionsAttached)
            return;

        ViewModel.Navigation.PropertyChanged += OnNavigationPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged += OnUiScaleMetricsPropertyChanged;
        _areSubscriptionsAttached = true;
    }

    private void DetachSubscriptions()
    {
        if (!_areSubscriptionsAttached)
            return;

        ViewModel.Navigation.PropertyChanged -= OnNavigationPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged -= OnUiScaleMetricsPropertyChanged;
        _areSubscriptionsAttached = false;
    }
}
