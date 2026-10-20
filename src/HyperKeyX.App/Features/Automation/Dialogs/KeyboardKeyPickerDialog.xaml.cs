using System.Collections.Specialized;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Automation.Dialogs;

/// <summary>
/// 提供不依赖物理输入钩子的专用键盘按键选择对话框。
/// </summary>
public sealed partial class KeyboardKeyPickerDialog : ContentDialog
{
    public KeyboardKeyPickerDialog(
        bool isEnglish,
        KeyboardKeyIdentity? selectedKey = null)
    {
        ViewModel = new KeyboardKeyPickerViewModel(
            KeyboardKeyCatalog.Default,
            isEnglish,
            selectedKey);
        InitializeComponent();
        GroupedKeys.Source = ViewModel.Groups;
        ViewModel.Groups.CollectionChanged += OnGroupsChanged;
        Closed += OnDialogClosed;
    }

    public KeyboardKeyPickerViewModel ViewModel { get; }
    public KeyboardKeyIdentity? SelectedKey => ViewModel.SelectedItem?.Identity;

    private void OnItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is KeyboardKeyPickerItemViewModel item)
            ViewModel.SelectedItem = item;
    }

    private void OnGroupsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args) =>
        GroupedKeys.Source = ViewModel.Groups;

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.Groups.CollectionChanged -= OnGroupsChanged;
        Closed -= OnDialogClosed;
    }
}
