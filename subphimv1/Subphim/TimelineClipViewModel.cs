using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace subphimv1.Models
{
    public enum TimelineClipType
    {
        Video,
        Audio,
        Image,
        Subtitle,
        Text
    }

    public class TrackBackgroundViewModel : INotifyPropertyChanged
    {
        public double Height { get; set; }
        public int TrackIndex { get; set; } // Giữ lại TrackIndex để định danh
        public TimelineClipType TrackType { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class TimelineClipViewModel : INotifyPropertyChanged
    {
        public void SetRenderTimes(TimeSpan startTime, TimeSpan duration)
        {
            RenderStartTime = startTime;
            RenderDuration = duration;
        }
        public TimeSpan RenderStartTime { get; set; }
        public TimeSpan RenderDuration { get; set; }
        public object SourceData { get; private set; }
        public TimelineClipType ClipType { get; private set; }

        #region Properties for Binding
        private double _x;
        public double X { get => _x; set { if (_x != value) { _x = value; OnPropertyChanged(); } } }

        private double _y;
        public double Y { get => _y; set { if (_y != value) { _y = value; OnPropertyChanged(); } } }

        private double _width;
        public double Width { get => _width; set { if (_width != value) { _width = value; OnPropertyChanged(); } } }

        private double _height;
        public double Height { get => _height; set { if (_height != value) { _height = value; OnPropertyChanged(); } } }

        private int _trackIndex;
        public int TrackIndex
        {
            get => _trackIndex;
            set
            {
                if (_trackIndex != value)
                {
                    _trackIndex = value;
                    OnPropertyChanged();
                    UpdateSourceTrackIndex();
                }
            }
        }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
        }
        public BitmapImage Thumbnail { get; set; }
        public ObservableCollection<BitmapImage> FilmstripThumbnails { get; set; }
        public List<float> WaveformData { get; set; }
        public int WaveformVersion { get; private set; } = 0;
        public void InvalidateWaveform()
        {
            this.WaveformVersion++;
            OnPropertyChanged(nameof(WaveformVersion));
        }
        #endregion

        #region Playback Properties (FIXED)

        public string FilePath
        {
            get
            {
                if (SourceData is MediaAsset ma) return ma.FilePath;
                if (SourceData is TimelineAudioClip tac) return tac.FilePath;
                return null;
            }
        }
        public string FileName => !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath) : "No File";
        public bool IsVideo => ClipType == TimelineClipType.Video;

        public double VolumeDb
        {
            get
            {
                if (SourceData is MediaAsset ma)
                {
                    return ma.VolumeDb;
                }
                if (SourceData is TimelineAudioClip tac) return tac.VolumeDb;

                return 0.0;
            }
            set
            {
                bool changed = false;
                if (SourceData is MediaAsset ma && ma.VolumeDb != value)
                {
                    ma.VolumeDb = value;
                    changed = true;
                }
                else if (SourceData is TimelineAudioClip tac && tac.VolumeDb != value)
                {
                    tac.VolumeDb = value;
                    changed = true;
                }
                if (changed)
                {
                    OnPropertyChanged();
                    InvalidateWaveform();
                }
            }
        }
        public TimeSpan StartTime
        {
            get
            {
                if (SourceData is SrtSubtitleLine s) return s.StartTime;
                if (SourceData is MediaAsset a) return a.StartTime;
                if (SourceData is TimelineAudioClip tac) return tac.StartTime;
                return TimeSpan.Zero;
            }
            set
            {
                bool changed = false;
                if (SourceData is SrtSubtitleLine s && s.StartTime != value)
                {
                    s.StartTime = value;
                    changed = true;
                }
                else if (SourceData is MediaAsset a && a.StartTime != value)
                {
                    a.StartTime = value;
                    changed = true;
                }
                else if (SourceData is TimelineAudioClip tac && tac.StartTime != value)
                {
                    tac.StartTime = value;
                    changed = true;
                }
                if (changed)
                {
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }

        public TimeSpan Duration
        {
            get
            {
                if (SourceData is SrtSubtitleLine s) return s.Duration;
                if (SourceData is MediaAsset a) return a.EffectiveDuration;
                if (SourceData is TimelineAudioClip tac) return tac.EffectiveDuration;
                return TimeSpan.Zero;
            }
            set
            {
            }
        }

        public TimeSpan EndTime
        {
            get
            {
                if (SourceData is TimelineAudioClip tac) return tac.EndTime;
                return StartTime + Duration;
            }
        }
        public subphimv1.Waveform.WavePeakPyramid WavePeaks
        {
            get
            {
                if (SourceData is subphimv1.Subphim.MediaAsset ma) return ma.WavePeaks;
                if (SourceData is subphimv1.Subphim.TimelineAudioClip tac) return tac.WavePeaks;
                return null;
            }
        }
        #endregion

        public TimelineClipViewModel(object source)
        {
            SourceData = source;
            FilmstripThumbnails = new ObservableCollection<BitmapImage>();

            if (source is MediaAsset asset)
            {
                this.ClipType = asset.Type switch
                {
                    AssetType.Video => TimelineClipType.Video,
                    AssetType.Audio => TimelineClipType.Audio,
                    AssetType.Image => TimelineClipType.Image,
                    _ => throw new ArgumentException("Unsupported MediaAsset type")
                };
                this.DisplayName = asset.FileName;
                this.WaveformData = asset.WaveformData;
                this.Thumbnail = asset.ThumbnailSource;
            }
            else if (source is SrtSubtitleLine subtitle)
            {
                this.ClipType = subtitle.IsTextClip ? TimelineClipType.Text : TimelineClipType.Subtitle;
                this.DisplayName = subtitle.OriginalText;
                this.TrackIndex = subtitle.TrackIndex;
            }
            else if (source is TimelineAudioClip audioClip)
            {
                this.ClipType = TimelineClipType.Audio;
                this.DisplayName = Path.GetFileName(audioClip.FilePath);
                this.WaveformData = audioClip.WaveformData;
            }
            else
            {
                throw new ArgumentException("Unsupported source data type for TimelineClipViewModel");
            }
        }
        public double Speed
        {
            get
            {
                if (SourceData is MediaAsset ma) return ma.Speed;
                if (SourceData is TimelineAudioClip tac) return tac.Speed;
                return 1.0;
            }
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }

        private void UpdateSourceTrackIndex()
        {
            if (SourceData is SrtSubtitleLine subtitle)
            {
                subtitle.TrackIndex = this.TrackIndex;
            }
        }
        public void RefreshPropertiesFromSource()
        {
            OnPropertyChanged(nameof(StartTime));
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(Speed));
            OnPropertyChanged(nameof(VolumeDb));
        }
        public event PropertyChangedEventHandler PropertyChanged;
        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}