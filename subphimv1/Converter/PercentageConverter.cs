using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace subphimv1.Converters
{
    public class PercentageConverter : MarkupExtension, IValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue * 100; // Convert to percentage
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue / 100; // Convert back from percentage
            }
            return value;
        }
    }
}