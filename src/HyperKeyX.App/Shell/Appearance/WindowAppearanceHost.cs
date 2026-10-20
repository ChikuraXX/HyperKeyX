using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Shell.Appearance;

public sealed record WindowAppearanceHost(
    FrameworkElement ApplicationRoot,
    Image BackgroundImage,
    Border BackgroundOverlay);
