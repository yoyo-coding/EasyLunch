using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;

namespace StartPage;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is bool b && b;
        if (parameter is string text && bool.TryParse(text, out var parameterInvert) && parameterInvert)
        {
            isVisible = !isVisible;
        }

        if (Invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility visibility && visibility == Visibility.Visible;
        return Invert ? !isVisible : isVisible;
    }
}

public sealed class StringNullOrWhiteSpaceToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = value is string text && !string.IsNullOrWhiteSpace(text);
        if (parameter is string mode && mode.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        if (Invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
// 将 "#AARRGGBB" 颜色字符串转换为画刷，用于图标主色淡化遮罩。
public sealed class ArgbStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string text && TryParseArgb(text, out var color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static bool TryParseArgb(string text, out Color color)
    {
        color = Microsoft.UI.Colors.Transparent;
        if (text.Length != 9 || text[0] != '#')
        {
            return false;
        }

        try
        {
            var a = System.Convert.ToByte(text.Substring(1, 2), 16);
            var r = System.Convert.ToByte(text.Substring(3, 2), 16);
            var g = System.Convert.ToByte(text.Substring(5, 2), 16);
            var b = System.Convert.ToByte(text.Substring(7, 2), 16);
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }
}


