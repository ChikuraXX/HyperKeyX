using System.Collections.Specialized;
using System.ComponentModel;
using HyperKeyX.Domain.Preferences;
using HyperKeyX.Features.Settings.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace HyperKeyX.Features.Settings.Views;

/// <summary>
/// 承载可扩展的设置分类，并同步持久化的分类选择。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private bool _areSubscriptionsAttached;
    private bool _isSynchronizingSelection;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        GeneralSettingsHost.Content = new GeneralSettingsView(viewModel);
        AppearanceSettingsHost.Content = new AppearanceSettingsView(viewModel);
        AboutSettingsHost.Content = new AboutSettingsView(viewModel);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RebuildCategoryItems();
        SynchronizeSelectedCategory();
    }

    public SettingsViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildCategoryItems();
        AttachSubscriptions();
        SynchronizeSelectedCategory();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSubscriptions();
    }

    private void OnCategorySelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection || sender.SelectedItem?.Tag is not SettingsCategoryId categoryId)
            return;

        ViewModel.SelectedCategory = categoryId;
        SynchronizeSelectedCategory();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedCategory))
        {
            SynchronizeSelectedCategory();
            if (IsLoaded)
                _ = ViewModel.RefreshStorageOnGeneralEntryAsync();
        }
    }

    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCategoryItems();
        SynchronizeSelectedCategory();
    }

    private void RebuildCategoryItems()
    {
        _isSynchronizingSelection = true;
        try
        {
            SettingsCategorySelector.Items.Clear();
            foreach (var category in ViewModel.Categories)
            {
                var item = new SelectorBarItem
                {
                    Tag = category.Id,
                    Icon = new FontIcon
                    {
                        Glyph = category.Glyph,
                        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)
                            Microsoft.UI.Xaml.Application.Current.Resources["SymbolThemeFontFamily"]
                    }
                };
                BindCategoryMetric(item, Control.FontSizeProperty, nameof(UiScaleMetrics.BodyFontSize));
                BindCategoryMetric(item, FrameworkElement.MinHeightProperty, nameof(UiScaleMetrics.SelectionItemMinHeight));
                BindCategoryMetric(item, Control.PaddingProperty, nameof(UiScaleMetrics.SelectionItemPadding));
                if (item.Icon is FontIcon icon)
                    BindCategoryMetric(icon, FontIcon.FontSizeProperty, nameof(UiScaleMetrics.IconGlyphSize));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                    item, $"SettingsCategory_{category.Id}");
                item.SetBinding(
                    SelectorBarItem.TextProperty,
                    new Binding
                    {
                        Source = category,
                        Path = new PropertyPath(nameof(SettingsCategoryViewModel.Title)),
                        Mode = BindingMode.OneWay
                    });
                SettingsCategorySelector.Items.Add(item);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    /// <summary>动态创建的分类控件使用同一度量源，比例变化时无需重建选项和焦点。</summary>
    private void BindCategoryMetric(FrameworkElement element, DependencyProperty property, string metric) =>
        element.SetBinding(property, new Binding
        {
            Source = ViewModel.UiScaleMetrics,
            Path = new PropertyPath(metric),
            Mode = BindingMode.OneWay
        });

    private void SynchronizeSelectedCategory()
    {
        var selectedItem = SettingsCategorySelector.Items
            .FirstOrDefault(item =>
                item.Tag is SettingsCategoryId categoryId &&
                categoryId == ViewModel.SelectedCategory);

        if (ReferenceEquals(SettingsCategorySelector.SelectedItem, selectedItem))
            return;

        _isSynchronizingSelection = true;
        try
        {
            SettingsCategorySelector.SelectedItem = selectedItem;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void AttachSubscriptions()
    {
        if (_areSubscriptionsAttached)
            return;

        ViewModel.PropertyChanged += OnSettingsPropertyChanged;
        ViewModel.Categories.CollectionChanged += OnCategoriesCollectionChanged;
        _areSubscriptionsAttached = true;
    }

    private void DetachSubscriptions()
    {
        if (!_areSubscriptionsAttached)
            return;

        ViewModel.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.Categories.CollectionChanged -= OnCategoriesCollectionChanged;
        _areSubscriptionsAttached = false;
    }
}
