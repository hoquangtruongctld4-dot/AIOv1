using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace subphimv1.Controls // Hoặc namespace phù hợp với project của bạn
{
    /// <summary>
    /// Lớp host để chứa một DrawingVisual trong cây giao diện WPF.
    /// </summary>
    public class VisualHost : FrameworkElement
    {
        private readonly Visual _visual;

        public VisualHost(Visual visual)
        {
            _visual = visual ?? throw new ArgumentNullException(nameof(visual));
            this.AddVisualChild(_visual);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index)
        {
            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _visual;
        }
    }

    /// <summary>
    /// Lớp chịu trách nhiệm vẽ thước đo timeline một cách hiệu năng cao.
    /// </summary>
    public class TimelineRulerVisual
    {
        private readonly DrawingVisual _visual = new DrawingVisual();
        private readonly Typeface _typeface = new Typeface("Segoe UI");
        private readonly Pen _majorTickPen;
        private readonly Pen _minorTickPen;
        private readonly Brush _textBrush;
        private readonly Brush _durationBarBrush;

        public DrawingVisual Visual => _visual;

        public TimelineRulerVisual()
        {
            _majorTickPen = new Pen(Brushes.Gray, 1);
            _minorTickPen = new Pen(Brushes.DarkGray, 0.5);
            _textBrush = Brushes.Gray;
            _durationBarBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xA6, 0x00));

            _majorTickPen.Freeze();
            _minorTickPen.Freeze();
            _textBrush.Freeze();
            _durationBarBrush.Freeze();
        }

        public void Redraw(double pixelsPerSecond,
                    double scrollOffset,
                    double viewportWidth,
                    TimeSpan totalDuration,
                    TimeSpan actualContentDuration)
        {
            using (DrawingContext dc = _visual.RenderOpen())
            {
                // Nền
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x34)), null, new Rect(0, 0, viewportWidth, 25));

                if (pixelsPerSecond <= 0) return;

                
                if (actualContentDuration > TimeSpan.Zero)
                {
                    double indicatorStartX = -scrollOffset;
                    double indicatorWidth = actualContentDuration.TotalSeconds * pixelsPerSecond;
                    dc.DrawRectangle(_durationBarBrush, null, new Rect(indicatorStartX, 0, indicatorWidth, 4));
                }

                // Vạch chia
                const double minPixelsPerTick = 80; // tránh chữ dày quá
                double minSecondsPerTick = minPixelsPerTick / pixelsPerSecond;
                double majorTickSeconds = GetNiceInterval(minSecondsPerTick);
                int minorTicksPerMajor = 4;
                double minorTickSeconds = majorTickSeconds / (minorTicksPerMajor + 1);

                double startSeconds = Math.Max(0, scrollOffset / pixelsPerSecond - majorTickSeconds);
                double endSeconds = Math.Min(
                    totalDuration.TotalSeconds,
                    (scrollOffset + viewportWidth) / pixelsPerSecond + majorTickSeconds
                );

                double firstSec = Math.Floor(startSeconds / minorTickSeconds) * minorTickSeconds;

                for (double sec = firstSec; sec <= endSeconds; sec += minorTickSeconds)
                {
                    sec = Math.Round(sec, 6);
                    double visualTickX = (sec * pixelsPerSecond) - scrollOffset;

                    if (visualTickX < -1 || visualTickX > viewportWidth + 1) continue;

                    bool isMajorTick = Math.Abs(sec % majorTickSeconds) < 0.000001;

                    if (isMajorTick)
                    {
                        // Vạch chính + nhãn
                        dc.DrawLine(_majorTickPen, new Point(visualTickX, 10), new Point(visualTickX, 25));

                        string label = FormatTickLabel(sec, majorTickSeconds);
                        var text = new FormattedText(
                            label,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            _typeface,
                            10,
                            _textBrush,
                            1.0 // PixelsPerDip
                        );

                        dc.DrawText(text, new Point(visualTickX + 3, 1));
                    }
                    else
                    {
                        // Vạch phụ
                        dc.DrawLine(_minorTickPen, new Point(visualTickX, 18), new Point(visualTickX, 25));
                    }
                }
            }
        }

        private string FormatTickLabel(double seconds, double majorTickSeconds)
        {
            if (majorTickSeconds >= 60)
            {
                long totalMinutes = (long)Math.Floor(seconds / 60.0 + 1e-9);
                int secPart = (int)Math.Round(seconds - totalMinutes * 60);
                if (secPart == 60) { totalMinutes++; secPart = 0; }
                return $"{totalMinutes:00}:{secPart:00}";
            }
            else if (majorTickSeconds >= 1)
            {
                return TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
            }
            else
            {
                return TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.fff");
            }
        }

        /// <summary>
        /// Thang vạch "đẹp", mở rộng tới giờ để hỗ trợ zoom-out timeline dài.
        /// </summary>
        private double GetNiceInterval(double minInterval)
        {
            double[] niceIntervals = {
        0.1, 0.2, 0.5,
        1, 2, 5, 10, 15, 30,
        60, 120, 300, 600, 900, 1800, 3600, 7200, 14400
    };
            foreach (var interval in niceIntervals)
            {
                if (interval >= minInterval) return interval;
            }
            return niceIntervals[niceIntervals.Length - 1];
        }
    }
}