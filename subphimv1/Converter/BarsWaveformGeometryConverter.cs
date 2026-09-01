using subphimv1.Waveform;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace subphimv1.Converters
{
    public sealed class BarsWaveformGeometryConverter : IMultiValueConverter
    {
        private static readonly Dictionary<int, Geometry> _geometryCache = new Dictionary<int, Geometry>();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 8 ||
                !(values[1] is double h) ||
                !(values[2] is double w) ||
                !(values[3] is double volumeDb) ||
                !(values[5] is TimeSpan trimStart) ||
                !(values[6] is TimeSpan effectiveDuration) ||
                !(values[7] is TimeSpan originalDuration) ||
                w <= 1 || h <= 1 || originalDuration.TotalSeconds <= 0)
            {
                return null;
            }

            string mode = parameter as string ?? "Body";
            int hashCode = 17;
            hashCode = hashCode * 23 + (values[0]?.GetHashCode() ?? 0);
            hashCode = hashCode * 23 + h.GetHashCode();
            hashCode = hashCode * 23 + w.GetHashCode();
            hashCode = hashCode * 23 + volumeDb.GetHashCode();
            hashCode = hashCode * 23 + mode.GetHashCode();
            hashCode = hashCode * 23 + (values[4]?.GetHashCode() ?? 0);
            // Thêm các giá trị mới vào hashCode để cache hoạt động chính xác
            hashCode = hashCode * 23 + trimStart.GetHashCode();
            hashCode = hashCode * 23 + effectiveDuration.GetHashCode();

            if (_geometryCache.TryGetValue(hashCode, out var cachedGeometry))
            {
                return cachedGeometry;
            }

            ReadOnlySpan<float> src;
            if (values[0] is WavePeakPyramid pyr && pyr.Levels.Count > 0)
            {
                int barsToDraw = Math.Max(1, (int)(w / 1.6));
                int levelIndex = 0;
                while (levelIndex + 1 < pyr.Levels.Count && pyr.Levels[levelIndex + 1].Length >= barsToDraw)
                {
                    levelIndex++;
                }
                src = pyr.Levels[levelIndex].AsSpan();
            }
            else if (values[0] is List<float> list && list.Count > 0)
            {
                src = CollectionsMarshal.AsSpan(list);
            }
            else
            {
                return null;
            }
            if (src.Length == 0) return null;

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                float gain = (float)Math.Pow(10.0, volumeDb / 20.0);
                double barWidth = 0.9;
                double barGap = 0.9;
                double barTotalWidth = barWidth + barGap;
                const double peakRatio = 0.2;

                double trimStartSec = trimStart.TotalSeconds;
                double effectiveDurationSec = effectiveDuration.TotalSeconds;
                double originalDurationSec = originalDuration.TotalSeconds;

                for (double x = 0; x < w; x += barTotalWidth)
                {
                    // Tính toán thời gian tương ứng tại pixel 'x' trong clip đã được trim
                    double timeInTrimmedClip = (x / w) * effectiveDurationSec;

                    // Chuyển đổi thời gian đó thành thời gian trong file audio gốc
                    double timeInOriginalFile = trimStartSec + timeInTrimmedClip;

                    // Tính toán tỷ lệ vị trí trong file audio gốc để lấy đúng index dữ liệu sóng âm
                    double ratioInOriginalFile = timeInOriginalFile / originalDurationSec;

                    int dataIndex = (int)(ratioInOriginalFile * src.Length);
                    if (dataIndex < 0 || dataIndex >= src.Length) continue;

                    float peak = src[dataIndex];
                    double barHeight = Math.Min(h, peak * gain * h);

                    if (barHeight >= 1)
                    {
                        double topY = h - barHeight;
                        Rect rect;

                        if (mode == "Peak")
                        {
                            double peakHeight = barHeight * peakRatio;
                            rect = new Rect(x, topY, barWidth, peakHeight);
                        }
                        else // "Body"
                        {
                            rect = new Rect(x, topY, barWidth, barHeight);
                        }
                        ctx.BeginFigure(rect.TopLeft, true, true);
                        ctx.LineTo(rect.TopRight, true, false);
                        ctx.LineTo(rect.BottomRight, true, false);
                        ctx.LineTo(rect.BottomLeft, true, false);
                    }
                }
            }

            geom.Freeze();
            if (_geometryCache.Count > 500) _geometryCache.Clear();
            _geometryCache[hashCode] = geom;
            return geom;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}