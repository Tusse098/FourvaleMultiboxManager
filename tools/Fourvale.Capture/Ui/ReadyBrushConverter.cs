using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fourvale.Capture.Ui;

/// <summary>Gold text when the character can act (action meter full), normal text otherwise.</summary>
public sealed class ReadyBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value is true ? "GoldBrush" : "TextBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
