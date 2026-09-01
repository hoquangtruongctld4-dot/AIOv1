
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace subphimv1.Converters
{
    public class DbToPositionConverter : IMultiValueConverter
    {
        private const double MIN_DB = -60.0;
        private const double MAX_DB = 20.0;
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2 ||
                !(values[0] is double db) ||
                !(values[1] is double canvasHeight) || canvasHeight <= 0)
            {
                return 0.0;
            }
            var clampedDb = Math.Clamp(db, MIN_DB, MAX_DB);
            var ratio = (clampedDb - MIN_DB) / (MAX_DB - MIN_DB);
            var position = canvasHeight - (ratio * canvasHeight);
            return position;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
