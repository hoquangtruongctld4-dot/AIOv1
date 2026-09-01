using System;
using System.Globalization;
using System.Windows.Data;

namespace subphimv1.Converters
{
    public class NewlineConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return text.Replace(@"\N", Environment.NewLine);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return text.Replace(Environment.NewLine, @"\N");
            }
            return value;
        }
    }
}