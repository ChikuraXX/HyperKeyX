using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace HyperKeyX.Common.Converters;

/// <summary>
/// 将布尔状态转换为页面可见性。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool flag && flag
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
