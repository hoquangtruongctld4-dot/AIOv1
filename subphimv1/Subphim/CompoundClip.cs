using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace subphimv1.Models
{
    public class CompoundClip
    {
        public List<MediaAsset> Clips { get; set; } = new List<MediaAsset>();

        public TimeSpan StartTime
        {
            get => Clips.Any() ? Clips.Min(c => c.StartTime) : TimeSpan.Zero;
        }

        public TimeSpan Duration
        {
            get
            {
                if (!Clips.Any()) return TimeSpan.Zero;
                var startTime = this.StartTime;
                var endTime = Clips.Max(c => c.StartTime + c.EffectiveDuration);
                return endTime - startTime;
            }
        }

        public string DisplayName => "Clip ghép";

        public CompoundClip(List<MediaAsset> clips)
        {
            Clips = clips.OrderBy(c => c.StartTime).ToList();
        }
    }
}