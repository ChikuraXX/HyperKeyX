using HyperKeyX.Domain.Preferences;
using Microsoft.UI.Xaml;

namespace HyperKeyX.Shell.Appearance;

public interface IWindowAppearanceService
{
    event EventHandler? SystemAppearanceChanged;

    AppearanceApplyResult Apply(
        Window window,
        WindowAppearanceHost host,
        AppPreferences preferences);
}
