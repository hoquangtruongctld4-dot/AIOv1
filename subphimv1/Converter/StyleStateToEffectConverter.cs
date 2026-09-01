using subphimv1.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace subphimv1.Converters
{
    public class StyleStateToEffectConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StyleState style)
            {
                if (style.IsBackgroundEnabled)
                {
                    return null;
                }
                if (style.EdgeStyle == TextEdgeStyle.Outline)
                {
                    var outlineColor = Colors.Black;
                    try
                    {
                        outlineColor = (Color)ColorConverter.ConvertFromString(style.OutlineColorHex);
                    }
                    catch { }

                    return new DropShadowEffect
                    {
                        ShadowDepth = 0,
                        Color = outlineColor,
                        BlurRadius = style.OutlineThickness,
                        Opacity = 1 
                    };
                }
                else if (style.EdgeStyle == TextEdgeStyle.Shadow)
                {
                    var shadowColor = Colors.Black;
                    try
                    {
                        shadowColor = (Color)ColorConverter.ConvertFromString(style.ShadowColorHex);
                    }
                    catch { }

                    return new DropShadowEffect
                    {
                        Color = Color.FromRgb(shadowColor.R, shadowColor.G, shadowColor.B),
                        Opacity = shadowColor.A / 255.0, 
                        ShadowDepth = style.ShadowDepth,
                        BlurRadius = style.ShadowBlur,
                        Direction = style.ShadowDirection
                    };
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PresetBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {

            var defaultBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x34)); // #FF303134
            defaultBrush.Freeze();

            if (value is StyleState style)
            {

                if (style.IsBackgroundEnabled)
                {
                    try
                    {
                        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(style.BackgroundColorHex);
                        brush.Freeze();
                        return brush;
                    }
                    catch
                    {
                        return defaultBrush;
                    }
                }
            }
            return defaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsStylePresetVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is StyleState style) || !(parameter is string elementType))
            {
                return Visibility.Collapsed;
            }
            bool isNonePreset = style.EdgeStyle == TextEdgeStyle.None && !style.IsBackgroundEnabled;

            if (elementType == "Icon")
            {
                return isNonePreset ? Visibility.Visible : Visibility.Collapsed;
            }

            if (elementType == "Text")
            {
                return isNonePreset ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}