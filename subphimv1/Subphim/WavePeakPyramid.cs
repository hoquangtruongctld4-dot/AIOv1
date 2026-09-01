using System.Collections.Generic;

namespace subphimv1.Waveform
{
    public sealed class WavePeakPyramid
    {
        public const uint MagicHeader = 0x4B503141; // "A1PK"
        public const ushort CurrentVersion = 1;
        public int BinsPerSecond { get; set; } = (int)(11025 / 128.0);
        public readonly List<float[]> Levels = new();
    }
}