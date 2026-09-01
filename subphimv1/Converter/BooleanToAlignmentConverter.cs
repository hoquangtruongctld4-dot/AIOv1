
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace subphimv1 
{
    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? Color.FromRgb(0, 132, 255) : Color.FromRgb(58, 59, 60);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}