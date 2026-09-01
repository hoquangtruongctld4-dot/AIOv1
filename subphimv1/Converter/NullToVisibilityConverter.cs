using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace subphimv1
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string param = parameter as string;
            bool isVisibleIfNotNull = string.IsNullOrEmpty(param) || param.Equals("VisibleIfNotNull", StringComparison.OrdinalIgnoreCase);

            if (isVisibleIfNotNull)
            {
                return value != null ? Visibility.Visible : Visibility.Collapsed;
            }
            else 
            {
                return value == null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}