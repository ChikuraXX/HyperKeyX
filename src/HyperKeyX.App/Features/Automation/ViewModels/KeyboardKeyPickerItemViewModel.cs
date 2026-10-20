using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 按键选择器中的本地化按键行。
/// </summary>
public sealed record KeyboardKeyPickerItemViewModel(
    KeyboardKeyCatalogItem CatalogItem,
    string DisplayName)
{
    public KeyboardKeyIdentity Identity => CatalogItem.Identity;
}
