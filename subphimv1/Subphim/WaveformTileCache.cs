using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace subphimv1.Waveform
{
    // Key để định danh một tile, dựa trên clip, mức độ zoom (lượng tử hóa), và chỉ số của tile
    public readonly record struct WaveTileKey(object ClipSourceData, int ZoomQ, int TileIndex);

    public static class WaveformTileCache
    {
        // Cache này sẽ lưu các tile trong RAM.
        private static readonly ConcurrentDictionary<WaveTileKey, ImageSource> _tiles = new();

        public static bool TryGet(WaveTileKey key, out ImageSource img) => _tiles.TryGetValue(key, out img);

        public static void Put(WaveTileKey key, ImageSource img)
        {
            if (img is BitmapSource bs) bs.Freeze(); // Freeze bitmap để có thể sử dụng xuyên thread và tối ưu hiệu năng
            _tiles[key] = img;
        }

        public static void InvalidateClip(object clipSourceData)
        {
            foreach (var k in _tiles.Keys)
            {
                if (ReferenceEquals(k.ClipSourceData, clipSourceData))
                {
                    _tiles.TryRemove(k, out _);
                }
            }
        }

        public static void ClearAll() => _tiles.Clear();
    }
}