using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace subphimv1.Converters
{
    public class CachingWaveformConverter : IMultiValueConverter
    {
        private static readonly Dictionary<int, PathGeometry> _cache = new Dictionary<int, PathGeometry>();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4 ||
                !(values[0] is List<float> waveformData) ||
                !(values[1] is double actualHeight) ||
                !(values[2] is double actualWidth) ||
                !(values[3] is double volumeDb) ||
                actualWidth <= 0 || actualHeight <= 0 || waveformData == null || !waveformData.Any())
            {
                return null;
            }
            int hashCode = 17;
            hashCode = hashCode * 23 + waveformData.GetHashCode();
            hashCode = hashCode * 23 + actualWidth.GetHashCode();
            hashCode = hashCode * 23 + actualHeight.GetHashCode();
            hashCode = hashCode * 23 + volumeDb.GetHashCode();
            if (_cache.TryGetValue(hashCode, out var cachedGeometry))
            {
                return cachedGeometry;
            }
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure();

            double widthRatio = actualWidth / waveformData.Count;
            double midY = actualHeight / 2.0;
            float gain = (float)Math.Pow(10.0, volumeDb / 20.0);

            pathFigure.StartPoint = new Point(0, midY);

            var segments = new PolyLineSegment();
            for (int i = 0; i < waveformData.Count; i++)
            {
                double x = i * widthRatio;
                double peak = waveformData[i] * gain;
                double y = midY - (peak * midY);
                segments.Points.Add(new Point(x, y));
            }
            pathFigure.Segments.Add(segments);

            var mirrorSegments = new PolyLineSegment();
            for (int i = waveformData.Count - 1; i >= 0; i--)
            {
                double x = i * widthRatio;
                double peak = waveformData[i] * gain;
                double y = midY + (peak * midY);
                mirrorSegments.Points.Add(new Point(x, y));
            }
            pathFigure.Segments.Add(mirrorSegments);
            pathFigure.IsClosed = true;

            pathGeometry.Figures.Add(pathFigure);
            pathGeometry.Freeze();
            _cache[hashCode] = pathGeometry;
            return pathGeometry;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}