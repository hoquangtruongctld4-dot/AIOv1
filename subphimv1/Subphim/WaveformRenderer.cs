using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace subphimv1.Waveform
{
    public static class WaveformRenderer
    {
        // Các tham số cho việc render tile
        public const int TileWidthPx = 512;  // Chiều rộng của mỗi tile bitmap
        public const int TileHeightPx = 48;   // Chiều cao của khu vực vẽ sóng âm (để vừa trong track audio/video)

        // Hàm chính để render một tile
        public static BitmapSource RenderTile(
            WavePeakPyramid pyr,
            double pixelsPerSecond,
            double clipStartTimeSec,
            int tileIndex,
            double volumeDb)
        {
            if (pyr == null || pyr.Levels.Count == 0) return null;

            // Tính toán level phù hợp từ pyramid để có khoảng 1-2 điểm dữ liệu cho mỗi pixel ngang
            double binsPerSecBase = pyr.BinsPerSecond;
            double targetBinsPerPixel = 2.0;
            double targetBinsPerSec = pixelsPerSecond * targetBinsPerPixel;

            int level = 0;
            int decimation = 1;
            while ((level + 1) < pyr.Levels.Count && (binsPerSecBase / (decimation * 2)) > targetBinsPerSec)
            {
                decimation *= 2;
                level++;
            }

            float[] data = pyr.Levels[level];
            double effectiveBinsPerSec = binsPerSecBase / decimation;
            double gain = Math.Pow(10, volumeDb / 20.0);

            // Dùng DrawingVisual để vẽ vector, sau đó rasterize nó thành bitmap.
            // Cách này hiệu quả hơn nhiều so với việc tạo hàng ngàn UIElement.
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Nền trong suốt
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, TileWidthPx, TileHeightPx));

                var bodyBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                var peakBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));
                bodyBrush.Freeze();
                peakBrush.Freeze();

                double midY = TileHeightPx / 2.0;

                // Lặp qua từng pixel ngang của tile để vẽ một thanh bar
                for (int x = 0; x < TileWidthPx; x++)
                {
                    double pixelTimeOnTimeline = clipStartTimeSec + (tileIndex * TileWidthPx + x) / pixelsPerSecond;
                    int binIndex = (int)(pixelTimeOnTimeline * effectiveBinsPerSec);

                    if (binIndex >= 0 && binIndex < data.Length)
                    {
                        double peakValue = data[binIndex] * gain;
                        if (peakValue > 1.0) peakValue = 1.0;

                        double barHeight = peakValue * TileHeightPx;
                        if (barHeight < 1.0) barHeight = 1.0; // Đảm bảo thanh luôn có chiều cao tối thiểu

                        double peakHeightRatio = 0.4; // Tỷ lệ chiều cao của phần đỉnh so với phần thân
                        double bodyHeight = barHeight * (1 - peakHeightRatio);
                        double peakHeight = barHeight * peakHeightRatio;

                        // Vẽ phần thân (Body)
                        double y_top_body = midY - bodyHeight / 2.0;
                        dc.DrawRectangle(bodyBrush, null, new Rect(x, y_top_body, 1, bodyHeight));

                        // Vẽ phần đỉnh (Peak) - đè lên trên phần thân
                        double y_top_peak = y_top_body - peakHeight;
                        dc.DrawRectangle(peakBrush, null, new Rect(x, y_top_peak, 1, peakHeight));
                    }
                }
            }

            // Render DrawingVisual ra một RenderTargetBitmap
            var rtb = new RenderTargetBitmap(TileWidthPx, TileHeightPx, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze(); // Rất quan trọng để tối ưu hiệu năng
            return rtb;
        }
    }
}