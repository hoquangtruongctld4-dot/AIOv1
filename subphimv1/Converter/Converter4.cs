using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace subphimv1.Translation
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = false;
                if (parameter != null)
                {
                    if (parameter is string paramString)
                    {
                        bool.TryParse(paramString, out invert);
                        if (paramString.Equals("invert", StringComparison.OrdinalIgnoreCase) ||
                            paramString.Equals("inverted", StringComparison.OrdinalIgnoreCase) ||
                            paramString.Equals("reverse", StringComparison.OrdinalIgnoreCase))
                            invert = true;
                    }
                    else if (parameter is bool paramBool)
                    {
                        invert = paramBool;
                    }
                }

                if (invert)
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    return boolValue ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibilityValue)
            {
                bool invert = false;
                if (parameter != null)
                {
                    if (parameter is string paramString)
                    {
                        bool.TryParse(paramString, out invert);
                        if (paramString.Equals("invert", StringComparison.OrdinalIgnoreCase) ||
                           paramString.Equals("inverted", StringComparison.OrdinalIgnoreCase) ||
                           paramString.Equals("reverse", StringComparison.OrdinalIgnoreCase))
                            invert = true;
                    }
                    else if (parameter is bool paramBool)
                    {
                        invert = paramBool;
                    }
                }

                if (invert)
                {
                    return visibilityValue != Visibility.Visible;
                }
                else
                {
                    return visibilityValue == Visibility.Visible;
                }
            }
            return false;
        }
    }
}