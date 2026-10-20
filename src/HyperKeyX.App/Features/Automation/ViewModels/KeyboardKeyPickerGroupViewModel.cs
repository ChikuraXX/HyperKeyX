namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 按键选择器中的本地化按键分组。
/// </summary>
public sealed record KeyboardKeyPickerGroupViewModel(
    KeyboardKeyCategory Category,
    string DisplayName,
    IReadOnlyList<KeyboardKeyPickerItemViewModel> Items);
