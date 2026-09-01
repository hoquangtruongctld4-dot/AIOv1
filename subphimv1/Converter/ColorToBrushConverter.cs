using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace subphimv1.Converters
{
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color?)
            {
                Color? nullableColor = (Color?)value;
                if (nullableColor.HasValue)
                {
                    return new SolidColorBrush(nullableColor.Value);
                }
            }
            else if (value is Color)
            {
                Color color = (Color)value;
                return new SolidColorBrush(color);
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HexStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hexString && !string.IsNullOrEmpty(hexString))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hexString);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze(); 
                    return brush;
                }
                catch
                {
                    return new SolidColorBrush(Colors.Transparent);
                }
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}