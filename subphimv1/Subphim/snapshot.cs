using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace subphimv1.Models
{
    public enum SnapshotSourceKind { MediaAsset, Subtitle, Text, Audio }

    public class MediaAssetSnapshot
    {
        public string FilePath { get; set; }
        public AssetType Type { get; set; }
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        // Transform / trim trên player
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double Scale { get; set; }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double Rotation { get; set; }
        public double TrimStartSeconds { get; set; }
        public double TrimEndSeconds { get; set; }

        public static MediaAssetSnapshot From(MediaAsset a) => new MediaAssetSnapshot
        {
            FilePath = a.FilePath,
            Type = a.Type,
            DurationSeconds = a.Duration.TotalSeconds,
            Width = a.Width,
            Height = a.Height,
            PositionX = a.PositionX,
            PositionY = a.PositionY,
            Scale = a.Scale,
            ScaleX = a.ScaleX,
            ScaleY = a.ScaleY,
            Rotation = a.Rotation,
            TrimStartSeconds = a.TrimStartOffset.TotalSeconds,
            TrimEndSeconds = a.TrimEndOffset.TotalSeconds
        };

        public MediaAsset ToModel() => new MediaAsset
        {
            FilePath = this.FilePath,
            Type = this.Type,
            Duration = TimeSpan.FromSeconds(this.DurationSeconds),
            Width = this.Width,
            Height = this.Height,
            PositionX = this.PositionX,
            PositionY = this.PositionY,
            Scale = this.Scale,
            ScaleX = this.ScaleX,
            ScaleY = this.ScaleY,
            Rotation = this.Rotation,
            TrimStartOffset = TimeSpan.FromSeconds(this.TrimStartSeconds),
            TrimEndOffset = TimeSpan.FromSeconds(this.TrimEndSeconds)
        };
    }

    public class TimelineClipSnapshot
    {
        public TimelineClipType ClipType { get; set; }
        public int TrackIndex { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public double Speed { get; set; }
        public double VolumeDb { get; set; }
        public double Pan { get; set; }

        // Khóa để map lại nguồn
        public SnapshotSourceKind SourceKind { get; set; }
        public string SourceFilePath { get; set; }     // MediaAsset / AudioClip
        public int? SubtitleIndex { get; set; }        // Subtitle/Text
        public bool IsText { get; set; }               // phân biệt sub vs text
    }

    /// <summary>
    /// Ảnh chụp đầy đủ trạng thái phiên dựng (giống CapCut):
    /// - Project: toàn bộ nội dung (timeline clips, subtitle, text, ...).
    /// - Media bin: các asset đã thêm (Video/Image, Audio, Subtitle).
    /// - TimelineAudioClips rời.
    /// - Thông số UI: playhead, zoom, kích thước tham chiếu...
    /// </summary>
    public sealed class EditorSnapshot
    {
        public string Version { get; set; } = "1.0.0";

        // 1) Nội dung dự án (đã có timeline clips, subtitles, text, voiced, crop, style...)
        public ProjectState Project { get; set; } = new ProjectState();

        // 2) Media bin (những gì user đã thêm vào thư viện)
        public List<MediaAsset> MediaBin_VideoImageAssets { get; set; } = new List<MediaAsset>();
        public List<MediaAsset> MediaBin_AudioAssets { get; set; } = new List<MediaAsset>();
        public List<MediaAsset> MediaBin_SubtitleAssets { get; set; } = new List<MediaAsset>();

        // 3) Track audio rời (không nằm trong ProjectState.TimelineMediaClips)
        public List<TimelineAudioClip> TimelineAudioClips { get; set; } = new List<TimelineAudioClip>();

        // 4) Trạng thái UI/Editor
        public double PlayheadSeconds { get; set; } = 0.0;
        public double PixelsPerSecond { get; set; } = 50.0;

        // Kích thước tham chiếu khung preview/videoGrid (phục vụ bố cục overlay, text)
        public double ReferenceWidth { get; set; } = 1280.0;
        public double ReferenceHeight { get; set; } = 720.0;

        // Mở rộng: Scroll offsets
        public double? TimelineScrollOffsetX { get; set; } = null;
        public double? TimelineScrollOffsetY { get; set; } = null;

        /// <summary>
        /// Tạo snapshot từ ProjectState cũ (giữ tương thích ngược khi mở file project chỉ có ProjectState).
        /// </summary>
        public static EditorSnapshot FromLegacyProject(ProjectState state)
        {
            if (state == null) state = new ProjectState();

            return new EditorSnapshot
            {
                Version = "1.0.0",
                Project = state,
                MediaBin_VideoImageAssets = new List<MediaAsset>(),
                MediaBin_AudioAssets = new List<MediaAsset>(),
                MediaBin_SubtitleAssets = new List<MediaAsset>(),
                TimelineAudioClips = state.VoicedSubtitles ?? new List<TimelineAudioClip>(),
                PlayheadSeconds = 0.0,
                PixelsPerSecond = 50.0,
                ReferenceWidth = state.ProjectReferenceVideoWidth > 0 ? state.ProjectReferenceVideoWidth : 1280.0,
                ReferenceHeight = state.ProjectReferenceVideoHeight > 0 ? state.ProjectReferenceVideoHeight : 720.0
            };
        }
    }
}