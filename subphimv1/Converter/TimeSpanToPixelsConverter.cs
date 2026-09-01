using System;
using System.Globalization;
using System.Windows.Data;

namespace subphimv1.Converters
{
    public class TimeSpanToPixelsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !(values[0] is TimeSpan time) ||
                !(values[1] is TimeSpan totalDuration) ||
                !(values[2] is double totalWidth))
            {
                return 0.0;
            }

            if (totalDuration.TotalSeconds <= 0 || totalWidth <= 0)
            {
                return 0.0;
            }
            double pixelsPerSecond = totalWidth / totalDuration.TotalSeconds;
            return time.TotalSeconds * pixelsPerSecond;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}