using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 管理专用 108 键选择器的分组、搜索和单选状态。
/// </summary>
public sealed partial class KeyboardKeyPickerViewModel : ObservableObject
{
    private readonly KeyboardKeyCatalog _catalog;
    private readonly bool _isEnglish;

    [ObservableProperty]
    private KeyboardKeyPickerItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public KeyboardKeyPickerViewModel(
        KeyboardKeyCatalog catalog,
        bool isEnglish,
        KeyboardKeyIdentity? selectedKey = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _isEnglish = isEnglish;
        TitleText = isEnglish ? "Choose a keyboard key" : "选择键盘按键";
        SearchPlaceholderText = isEnglish ? "Search keys" : "搜索按键";
        ConfirmText = isEnglish ? "Confirm" : "确认";
        CancelText = isEnglish ? "Cancel" : "取消";

        RefreshGroups();
        if (selectedKey is KeyboardKeyIdentity key)
        {
            SelectedItem = Groups
                .SelectMany(group => group.Items)
                .FirstOrDefault(item =>
                    item.Identity.StorageKey == key.StorageKey);
        }
    }

    public ObservableCollection<KeyboardKeyPickerGroupViewModel> Groups { get; } = [];
    public bool CanConfirm => SelectedItem is not null;
    public string TitleText { get; }
    public string SearchPlaceholderText { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public string GetCategoryName(KeyboardKeyCategory category) =>
        KeyboardKeyCatalog.GetCategoryName(category, _isEnglish);

    partial void OnSearchTextChanged(string value) => RefreshGroups();

    partial void OnSelectedItemChanged(KeyboardKeyPickerItemViewModel? value) =>
        OnPropertyChanged(nameof(CanConfirm));

    private void RefreshGroups()
    {
        var selectedStorageKey = SelectedItem?.Identity.StorageKey;
        var matches = _catalog.Search(SearchText, _isEnglish);
        Groups.Clear();
        foreach (var category in Enum.GetValues<KeyboardKeyCategory>())
        {
            var items = matches
                .Where(item => item.Category == category)
                .Select(item => new KeyboardKeyPickerItemViewModel(
                    item,
                    item.GetDisplayName(_isEnglish)))
                .ToArray();
            if (items.Length > 0)
            {
                Groups.Add(new KeyboardKeyPickerGroupViewModel(
                    category,
                    KeyboardKeyCatalog.GetCategoryName(category, _isEnglish),
                    items));
            }
        }

        SelectedItem = Groups
            .SelectMany(group => group.Items)
            .FirstOrDefault(item =>
                item.Identity.StorageKey == selectedStorageKey);
    }
}
