using FFmpeg.AutoGen;
using FFMpegCore;
using FFMpegCore.Enums;
using System.Collections.Specialized;
using Google.Apis.Drive.v3;
using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using subphimv1.Audio;
using subphimv1.Controls;
using subphimv1.Converters;
using subphimv1.Filmstrip;
using subphimv1.Models;
using subphimv1.Services;
using subphimv1.Services.Whisper;
using subphimv1.Subphim;
using subphimv1.UserView;
using subphimv1.Waveform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Unosquare.FFME.Common;
using static subphimv1.Models.ProjectState;
using static subphimv1.Services.ApiService;
using static System.Net.Mime.MediaTypeNames;
using Keyboard = System.Windows.Input.Keyboard;
using MediaElement = Unosquare.FFME.MediaElement;
using Mouse = System.Windows.Input.Mouse;
using Path = System.IO.Path;





namespace subphimv1
{
    public enum VoiceoverExportMode
    {
        Default,
        Ducking
    }
    public enum SmartCutMode
    {
        None,
        StaticReview,
        ParallelDynamic,
        DynamicVideo
    }
    public class SizeToRectConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) { if (values.Length == 2 && values[0] is double width && values[1] is double height) { if (width > 0 && height > 0) { return new System.Windows.Rect(0, 0, width, height); } } return DependencyProperty.UnsetValue; }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
    }
    public partial class MainWindow : System.Windows.Window, System.ComponentModel.INotifyPropertyChanged
    {

        private readonly SmartCutService _smartCutService = new SmartCutService();
        private readonly DispatcherTimer _scrollRenderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private bool _isSmartCutPopupOpen;
        private bool _scrollDirty = false;
        private bool _imeCompositionActive = false;
        private bool _imeCompositionJustCommitted = false;
        private VoiceoverExportMode _selectedVoiceoverMode = VoiceoverExportMode.Default;
        private List<TimelineClipViewModel> _clipsSortedByStart = new List<TimelineClipViewModel>();

        private sealed class TimelineClipStartComparer : IComparer<TimelineClipViewModel>
        {
            public static readonly TimelineClipStartComparer Instance = new TimelineClipStartComparer();
            public int Compare(TimelineClipViewModel a, TimelineClipViewModel b)
                => a.StartTime.CompareTo(b.StartTime);
        }
        public bool IsSmartCutPopupOpen
        {
            get => _isSmartCutPopupOpen;
            set
            {
                if (_isSmartCutPopupOpen != value)
                {
                    _isSmartCutPopupOpen = value;
                    OnPropertyChanged(nameof(IsSmartCutPopupOpen));
                }
            }
        }
        private List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)> _smartCutSubtitleTimeline;
        private SmartCutMode _currentSmartCutMode = SmartCutMode.None;
        private double _smartCutDynamicSpeed = 0.6;
        private const int TTS_CHUNK_TRIGGER_BYTES = 45000;
        private const int TTS_CHUNK_TARGET_BYTES = 10000;
        private readonly DispatcherTimer _zoomRenderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        private readonly HashSet<TimelineClipViewModel> _preparedFilmstrip = new();
        private readonly HashSet<TimelineClipViewModel> _preparedWaveform = new();
        private ImageAdorner _imageAdorner;
        private MediaAsset _activeTransformingImageAsset;
        private double _playheadDragStartAbsX;
        private TimeSpan _playheadDragStartTime;
        private readonly Dictionary<MediaAsset, FrameworkElement> _activeImageOverlays = new Dictionary<MediaAsset, FrameworkElement>();
        private readonly ConcurrentDictionary<TimelineClipViewModel, CancellationTokenSource> _filmstripTasks = new();
        private readonly ConcurrentDictionary<string, FilmstripService> _filmstripServiceInstances = new();
        private TimelineAudioEngine _audioEngine;
        private TimeSpan _previousPlayhead = TimeSpan.Zero;
        private TimelineClipViewModel _pendingOpenClip;
        private bool _pendingWasPlaying;
        private bool _isSwitchingVideoSource;
        private readonly TimelineRulerVisual _timelineRulerVisual;
        private readonly VisualHost _rulerHost;
        private DispatcherTimer _rulerRedrawTimer;
        private bool _isRulerRedrawPending = false;
        private bool _isAwaitingSeekCompletion = false;
        private volatile bool _isUserDraggingPlayhead = false;
        private int _seekGracePeriodCounter = 0;
        private readonly System.Diagnostics.Stopwatch _renderStopwatch = new System.Diagnostics.Stopwatch();
        private bool _isSwitchingSourceDueToPlayback = false;
        private bool _isModifyingVideoClip = false;
        private TimeSpan _preModificationEndTime;
        private StyleState _copiedStyleState = null;
        private bool _isDraggingPositionMarker = false;
        private bool _isMarqueeSelecting = false;
        private System.Windows.Point _marqueeStartPoint;
        private bool _suppressAudioDuringResize = false;
        private double _playerPanelScale = 1.0 / 1.1;
        private const double PLAYER_ZOOM_SPEED = 1.1;
        private const double PLAYER_ZOOM_MIN = 0.2;
        private List<StyleState> _stylePresets;
        private string _lastExportPath = "";
        private CancellationTokenSource _videoOpenCts;
        private bool _videoReady;
        private MediaAsset _activeVideo;
        private bool _isAdornerRotating = false;
        private double _finalAdornerAngle = 0;
        private RotateTransform _activeRotateTransform = null;
        private FrameworkElement _rotatingVisual = null;
        private readonly TimeSpan _clipEpsilon = TimeSpan.FromMilliseconds(80);
        private DateTime _lastScrubSeekAt = DateTime.MinValue;
        private static readonly TimeSpan _minScrubInterval = TimeSpan.FromMilliseconds(40);
        private bool _isOverlayVisible = false;
        private bool _isEdgeResizeActive = false;
        private double _edgeLeftPx;
        private double _edgeRightPx;
        private double _resizeCanvasWidth;
        private int _auxUiDepth = 0;

        private bool IsAuxUiActive => _auxUiDepth > 0;
        private void EnterAuxUi() { _auxUiDepth++; }
        private void ExitAuxUi() { if (_auxUiDepth > 0) _auxUiDepth--; }
        #region Timeline Playback
        private bool _isTextInEditMode = false;
        private const double RESIZE_EDGE_SLOP = 8.0;
        private TimeSpan _actualContentDuration;
        private FontFamily _originalFontFamilyBeforePreview;
        private const bool ScalePaddingLikeWpf = false;
        private enum PlayerMode { Timeline, Preview }
        private TimelineClipViewModel _activeVideoClip;
        private TimeSpan _playhead = TimeSpan.Zero;
        private bool _isTimelinePlaying = false;
        private static string BuildAtempoChain(double speed)
        {
            double s = Math.Clamp(speed, 0.25, 4.0);

            if (Math.Abs(s - 1.0) < 1e-6)
                return string.Empty;

            // Ngưỡng biên an toàn: FFmpeg build của bạn có vẻ loại trừ 0.5 => dùng > 0.5 một chút.
            const double MinStrict = 0.500001; // > 0.5 để tránh lỗi "out of range"
            const double MaxStep = 2.0;      // Giới hạn chuẩn của atempo mỗi bước (2.0 vẫn OK)
            var steps = new List<double>();

            // Hàm tiện ích định dạng
            static string fmt(double v) => v.ToString("0.###############", CultureInfo.InvariantCulture);

            if (s > 1.0)
            {
                // Tách các bước 2.0 đến khi còn lại <= 2.0
                while (s > MaxStep + 1e-9)
                {
                    steps.Add(MaxStep);
                    s /= MaxStep;
                }

                // Bước cuối (0.5, 2.0]
                // Nếu vô tình rơi chính xác 2.0 do làm tròn, giữ nguyên 2.0 (FFmpeg chấp nhận),
                // nếu rơi xuống <= 0.5 (cực kỳ hiếm với nhánh >1.0), chia thành hai căn bậc hai để đẩy mỗi bước > 0.5
                if (s <= 0.5 + 1e-12)
                {
                    // Chia làm hai bước căn bậc hai, mỗi bước > 0.5
                    double f = Math.Sqrt(Math.Max(s, 0.25));
                    if (f <= MinStrict) f = MinStrict; // đảm bảo > 0.5
                    steps.Add(f);
                    steps.Add(s / f);
                }
                else
                {
                    steps.Add(s);
                }
            }
            else
            {
                // s < 1.0
                if (s >= MinStrict)
                {
                    // Nằm trong (0.5,1): một bước là đủ
                    steps.Add(s);
                }
                else
                {
                    // s < 0.5: tìm m sao cho f = s^(1/m) > 0.5 (nghiêm ngặt)
                    // m > log_{0.5}(s). Ta lấy m = floor(log_{0.5}(s)) + 1 để đảm bảo f > 0.5
                    double logBase = Math.Log(0.5);
                    int m = (int)Math.Floor(Math.Log(s) / logBase) + 1; // tối thiểu 2 khi s<=0.5
                    if (m < 2) m = 2;

                    // Tính f và đảm bảo f>0.5
                    double f = Math.Pow(s, 1.0 / m);
                    while (f <= MinStrict)
                    {
                        m++;
                        f = Math.Pow(s, 1.0 / m);
                        if (m > 12) break; // chốt an toàn chống lặp quá lâu
                    }

                    // Thêm m-1 bước bằng f
                    for (int i = 0; i < m - 1; i++)
                        steps.Add(f);

                    // Bước cuối để đảm bảo tích đúng s (giảm sai số do làm tròn)
                    double prod = 1.0;
                    for (int i = 0; i < steps.Count; i++) prod *= steps[i];
                    double last = s / prod;

                    // Nếu last vẫn chạm ≤ 0.5 do sai số, cân bằng lại hai bước cuối
                    if (last <= MinStrict)
                    {
                        // Trộn đều hai bước cuối cùng bằng trung bình nhân (geometric mean)
                        if (steps.Count >= 1)
                        {
                            double prev = steps[^1];
                            double g = Math.Sqrt(prev * last);
                            if (g <= MinStrict) g = MinStrict + 1e-6;
                            steps[^1] = g;
                            last = s / (prod * (g / prev));
                        }
                    }

                    steps.Add(last);
                }
            }

            // Loại bỏ các bước ~1.0 để chuỗi gọn
            for (int i = steps.Count - 1; i >= 0; i--)
                if (Math.Abs(steps[i] - 1.0) < 1e-6) steps.RemoveAt(i);

            // Bảo đảm mỗi bước vẫn nằm trong (0.5, 2.0]
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] <= 0.5) steps[i] = MinStrict;
                if (steps[i] > 2.0) steps[i] = 2.0;
            }

            if (steps.Count == 0) return string.Empty;
            return string.Join(",", steps.Select(v => $"atempo={fmt(v)}"));
        }

        private static float DbToGain(double db) => (float)Math.Pow(10.0, db / 20.0);
        private Task _playbackTask;
        private CancellationTokenSource _playbackCts;
        private TimeSpan? _pendingSeekForNewSource = null;
        private bool _wasPlayingBeforeSeek = false;
        #endregion
        #region Custom Prompt Fields
        private Dictionary<string, string> _customPrompts = new Dictionary<string, string>();
        public ObservableCollection<string> CustomPromptNames { get; set; }

        private bool _isCustomPromptEnabled;
        public bool IsCustomPromptEnabled
        {
            get => _isCustomPromptEnabled;
            set { if (_isCustomPromptEnabled != value) { _isCustomPromptEnabled = value; OnPropertyChanged(); } }
        }

        private string _selectedCustomPromptName;
        public string SelectedCustomPromptName
        {
            get => _selectedCustomPromptName;
            set
            {
                if (_selectedCustomPromptName != value)
                {
                    _selectedCustomPromptName = value;
                    OnPropertyChanged();
                    if (_customPrompts.TryGetValue(_selectedCustomPromptName ?? "", out var promptText))
                    {
                        CurrentCustomPromptText = promptText;
                    }
                }
            }
        }

        private string _currentCustomPromptText;
        public string CurrentCustomPromptText
        {
            get => _currentCustomPromptText;
            set { if (_currentCustomPromptText != value) { _currentCustomPromptText = value; OnPropertyChanged(); } }
        }

        private bool _isSavePromptPopupOpen;
        public bool IsSavePromptPopupOpen
        {
            get => _isSavePromptPopupOpen;
            set { if (_isSavePromptPopupOpen != value) { _isSavePromptPopupOpen = value; OnPropertyChanged(); } }
        }

        private string _newPromptName;
        public string NewPromptName
        {
            get => _newPromptName;
            set { if (_newPromptName != value) { _newPromptName = value; OnPropertyChanged(); } }
        }
        #endregion
        #region INotifyPropertyChanged Implementation
        private bool _isBatchSubtitleMode = false;
        public bool IsBatchSubtitleMode { get => _isBatchSubtitleMode; set { if (_isBatchSubtitleMode != value) { _isBatchSubtitleMode = value; OnPropertyChanged(nameof(IsBatchSubtitleMode)); UpdateSubtitleButtonVisibility(); } } }
        private List<TimelineClipViewModel> _clipsForBatchSubtitle;
        private subphimv1.Services.UndoRedoService<EditorSnapshot> _undoRedoService = new subphimv1.Services.UndoRedoService<EditorSnapshot>();
        private TimeSpan _resizeInitialStartTime;
        private TimeSpan _resizeInitialEndTime;
        private double _pixelsPerSecond = 50.0;
        private enum DragMode { None, MoveClip, AdjustVolume, ResizeClip }
        private DragMode _currentDragMode = DragMode.None;
        private Thumb _draggedResizeHandle = null;
        private System.Windows.Point _dragStartPointInCanvas;
        private bool _isPreparingToDragClip = false;
        private System.Windows.Point _dragStartPointInGrid;
        private double _dragClipInitialX;
        private double _dragClipInitialY;
        private System.Windows.Point _dragStartPoint;
        private List<TimelineAudioClipViewModel> _timelineAudioClipsVM = new List<TimelineAudioClipViewModel>();
        private StyleState _globalSubtitleStyle = new StyleState();
        private SrtSubtitleLine _selectedSubtitle;
        public SrtSubtitleLine SelectedSubtitle { get => _selectedSubtitle; set { if (_selectedSubtitle != value) { _selectedSubtitle = value; OnPropertyChanged(nameof(SelectedSubtitle)); } } }
        private MediaAsset _activeMediaAsset;
        public MediaAsset ActiveMediaAsset { get => _activeMediaAsset; set { if (_activeMediaAsset != value) { if (_activeMediaAsset != null) { _activeMediaAsset.IsSelected = false; } _activeMediaAsset = value; if (_activeMediaAsset != null) { _activeMediaAsset.IsSelected = true; } OnPropertyChanged(nameof(ActiveMediaAsset)); } } }
        private AdornerLayer _adornerLayer;
        private readonly Dictionary<SrtSubtitleLine, FrameworkElement> _activeVisuals = new Dictionary<SrtSubtitleLine, FrameworkElement>();
        private SubtitleAdorner _subtitleAdorner;
        private VideoAdorner _videoAdorner;
        private MediaAsset _activeTransformingVideoAsset;
        private bool _isAdornerDragging = false;
        private double _renderLatchSavedVolume = 1.0;
        private ProjectState _currentProject;
        public ProjectState CurrentProject { get => _currentProject; set { if (_currentProject != value) { _currentProject = value; OnPropertyChanged(nameof(CurrentProject)); } } }
        private bool _isUpdatingUiFromCode = false;
        private double _projectReferenceVideoHeight = 720.0;
        private double WPF_FONT_SIZE_CORRECTION_FACTOR = 0.9;
        private const double DEFAULT_FONT_SIZE_PERCENT_OF_HEIGHT = 0.07;
        public AudioTrackModel MainAudioTrack { get => _mainAudioTrack; }
        public ObservableCollection<TimelineClipViewModel> TimelineClips { get; set; }
        #region Multi-select Audio Sync Logic
        private bool _isApplyingBulkAudioChange = false;

        private void AttachHandlersForClipVM(TimelineClipViewModel vm)
        {
            if (vm == null) return;
            vm.PropertyChanged += ClipVM_PropertyChanged_ForAudioSync;
        }

        private void DetachHandlersForClipVM(TimelineClipViewModel vm)
        {
            if (vm == null) return;
            vm.PropertyChanged -= ClipVM_PropertyChanged_ForAudioSync;
        }

        private void TimelineClips_CollectionChanged_ForAudioSync(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e?.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is TimelineClipViewModel vm)
                    {
                        AttachHandlersForClipVM(vm);
                    }
                }
            }
            if (e?.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is TimelineClipViewModel vm)
                    {
                        DetachHandlersForClipVM(vm);
                    }
                }
            }
        }
        private void ClipVM_PropertyChanged_ForAudioSync(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TimelineClipViewModel.VolumeDb) || _isApplyingBulkAudioChange) return;

            if (sender is not TimelineClipViewModel sourceVm) return;
            if (sourceVm.ClipType != TimelineClipType.Audio && sourceVm.ClipType != TimelineClipType.Video) return;
            if (!sourceVm.IsSelected) return;

            ApplyVolumeToSelectedClips(sourceVm.VolumeDb, sourceVm);
        }
        private void SelectedAudioClip_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isApplyingBulkAudioChange) return;

            // Chỉ xử lý khi sender là 1 clip audio kiểu TimelineAudioClip (đang được panel edit “SelectedAudioClip” bind)
            if (sender is not TimelineAudioClip changedClip) return;

            // Quan tâm các thuộc tính sẽ ảnh hưởng playback/UI/ffmpeg
            bool isVolume = (e.PropertyName == nameof(TimelineAudioClip.VolumeDb));
            bool isSpeed = (e.PropertyName == nameof(TimelineAudioClip.Speed) || e.PropertyName == nameof(MediaAsset.Speed));
            bool isPitch = (e.PropertyName == nameof(TimelineAudioClip.PitchCorrection));
            bool isFade = (e.PropertyName == nameof(TimelineAudioClip.FadeInDuration)
                          || e.PropertyName == nameof(TimelineAudioClip.FadeOutDuration));
            // Volume đã có luồng riêng cho multi-select slider trên clip; ở đây ưu tiên speed/pitch/fade.
            if (!isSpeed && !isPitch && !isFade && !isVolume) return;

            try
            {
                _isApplyingBulkAudioChange = true;

                // Lấy giá trị làm "mẫu" từ clip đang chỉnh trong panel
                double? newSpeed = changedClip.Speed;
                bool? newPitch = changedClip.PitchCorrection;
                double? newFadeIn = changedClip.FadeInDuration;
                double? newFadeOut = changedClip.FadeOutDuration;

                // Tập VM audio đang được chọn (không đụng clip video)
                var selectedAudioVMs = TimelineClips
                    .Where(vm => vm.IsSelected && vm.ClipType == TimelineClipType.Audio)
                    .ToList();

                // Áp dụng xuống model cho từng VM
                foreach (var vm in selectedAudioVMs)
                {
                    if (vm.SourceData is TimelineAudioClip tac)
                    {
                        if (isSpeed && newSpeed.HasValue) tac.Speed = newSpeed.Value;
                        if (isPitch && newPitch.HasValue) tac.PitchCorrection = newPitch.Value;
                        if (isFade && newFadeIn.HasValue) tac.FadeInDuration = newFadeIn.Value;
                        if (isFade && newFadeOut.HasValue) tac.FadeOutDuration = newFadeOut.Value;
                    }
                    else if (vm.SourceData is MediaAsset ma && ma.Type == AssetType.Audio)
                    {
                        if (isSpeed && newSpeed.HasValue) ma.Speed = newSpeed.Value;
                    }
                }

                // Cập nhật playback ngay cho clip đang chạy + làm tươi UI + width
                foreach (var vm in selectedAudioVMs)
                {
                    if (isSpeed || isPitch) _audioEngine.UpdateClipProperties(vm);

                    vm.RefreshPropertiesFromSource();

                    if (isSpeed) // speed đổi => duration đổi => width đổi
                    {
                        if (vm.SourceData is TimelineAudioClip tac2)
                            vm.Width = Math.Max(1.0, tac2.EffectiveDuration.TotalSeconds * _pixelsPerSecond);
                        else if (vm.SourceData is MediaAsset ma2)
                            vm.Width = Math.Max(1.0, ma2.EffectiveDuration.TotalSeconds * _pixelsPerSecond);
                    }
                }
            }
            finally
            {
                _isApplyingBulkAudioChange = false;
            }

            // Tính lại tổng thời lượng + vẽ lại timeline nếu speed đổi
            if (isSpeed)
            {
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
            }

            // Lưu lại trạng thái để ffmpeg sử dụng đúng speed/fade/pitch mới
            _undoRedoService?.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }

        private void ApplyVolumeToSelectedClips(double targetDb, TimelineClipViewModel sourceVm)
        {
            if (_isApplyingBulkAudioChange) return;

            try
            {
                _isApplyingBulkAudioChange = true;

                var selectedAudioCapableVMs = TimelineClips
                    .Where(c => c.IsSelected && (c.ClipType == TimelineClipType.Audio || c.ClipType == TimelineClipType.Video))
                    .ToList();
                foreach (var vm in selectedAudioCapableVMs)
                {
                    // Chỉ cập nhật nếu giá trị thực sự khác để tránh vòng lặp sự kiện không cần thiết
                    if (Math.Abs(vm.VolumeDb - targetDb) > 1e-9)
                    {
                        vm.VolumeDb = targetDb;
                    }
                }
            }
            finally
            {
                _isApplyingBulkAudioChange = false;
            }
        }
        #endregion
        // ==== Blur preview state ====
        private Border _blurPreview;
        private SubtitleAdorner _blurAdorner;
        public bool IsBlurPopupOpen { get; set; }

        public ObservableCollection<TimelineClipViewModel> VisibleTimelineClips { get; set; }
        private const double TIMELINE_TRACK_HEIGHT = 80;
        private const double TIMELINE_AUDIO_TRACK_HEIGHT = 50;
        private const double TIMELINE_TRACK_SPACING = 8;
        private bool _isDraggingTimelineClip = false;
        private TimelineClipViewModel _draggedTimelineClip = null;
        private System.Windows.Point _dragClipStartPoint;
        private TimelineClipViewModel _selectedTimelineClip;
        public TimelineClipViewModel SelectedTimelineClip { get => _selectedTimelineClip; set { if (_selectedTimelineClip != value) { _selectedTimelineClip = value; OnPropertyChanged(nameof(SelectedTimelineClip)); } } }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName)); }
        #endregion
        #region TTS Fields & Properties

        private MediaElement _ttsAudioPlayer;
        private DispatcherTimer _ttsProgressTimer;
        private bool _isTtsPlaying = false;
        private bool _isTtsSliderDragging = false;
        private string _currentTtsAudioPath = null;
        #endregion
        #region Fields & Properties
        private string _currentRotationDisplay;
        public string CurrentRotationDisplay
        {
            get => _currentRotationDisplay;
            set
            {
                if (_currentRotationDisplay != value)
                {
                    _currentRotationDisplay = value;
                    OnPropertyChanged();
                }
            }
        }
        private List<(TimeSpan Start, TimeSpan End)> _exportCachedBlurIntervals = null;
        public ObservableCollection<MediaAsset> SubtitleAssets { get; set; }
        public ObservableCollection<MediaAsset> VideoImageAssets { get; set; }
        public ObservableCollection<MediaAsset> AudioAssets { get; set; }
        public ObservableCollection<TimelineAudioClip> TimelineAudioClips { get; set; }
        private TimelineAudioClip _selectedAudioClip;
        public TimelineAudioClip SelectedAudioClip { get => _selectedAudioClip; set { if (_selectedAudioClip != value) { if (_selectedAudioClip != null) { _selectedAudioClip.PropertyChanged -= SelectedAudioClip_PropertyChanged; } _selectedAudioClip = value; if (_selectedAudioClip != null) { _selectedAudioClip.PropertyChanged += SelectedAudioClip_PropertyChanged; } OnPropertyChanged(nameof(SelectedAudioClip)); } } }
        private bool _isAutoProcessing = false;
        private bool _wasPlayingBeforeDrag = false;
        // --- Configuration ---
        private string _googleDriveFolderId = "";
        private string _pathVSF = "";
        private string _ocrTextFolderName = "TXTImages";
        private string _cmdVsfArgs = "";
        private string _customVsfOutputBaseDir = "";
        private string _customSubtitleOutputDir = "";
        private VsfVideoOpenMethod _selectedVsfOpenMethod = VsfVideoOpenMethod.Default;
        private VsfProcessingMode _selectedVsfProcessingMode = VsfProcessingMode.CleanAndCreateTxtImages;
        private string _vsfNumThreadsSearch = "-1";
        private string _vsfNumThreadsClean = "-1";
        private bool _useCuda = true;
        private bool _isSpeechToTextMode = false;
        private readonly string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LauncherAIO", "Settings.ini");
        // --- Services & Helpers ---
        private VsfService _vsfService;
        private WhisperService _whisperService;
        private CancellationTokenSource _masterCts;
        // --- Video Player & Crop State ---
        private TimeSpan _totalTimelineDuration;
        private string _currentVideoPath = null;
        private double _finalCropTop, _finalCropBottom, _finalCropLeft, _finalCropRight;
        private double _topPercent = 0.7;
        private double _bottomPercent = 0.9;
        private double _leftPercent = 0.1;
        private double _rightPercent = 0.9;
        private double _videoNativeWidth;
        private double _videoNativeHeight;
        private DispatcherTimer _layoutTimer;
        private SrtTranslationService _srtTranslationService;
        // --- AIOSubPhim Cleanup & State ---
        private string _lastImagesFolderPath = "";
        private bool _deleteRawTexts = true;
        private bool _deleteTexts = true;
        private bool _compressRawTexts = false;
        private string _selectedGeminiModelOcr = "gemini-2.5-flash";
        private List<string> _geminiCustomModelsOcr = new List<string>();
        // --- Services & Helpers ---
        private GeminiOcrService _geminiServiceOcr;
        // --- State & Timers ---
        private Stopwatch _operationStopwatch;
        private DispatcherTimer _progressTimer;
        private string _currentTimedLogMessageFormat;
        private string _currentTaskNameForLog;
        private readonly string _desktopErrorFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"OCR_Failed_Images_{DateTime.Now:yyyyMMdd_HHmmss}");
        // --- Gemini OCR Config ---
        private List<string> _geminiApiKeysOcr = new List<string>();
        private int _geminiImagesPerRequestOcr = 15;
        private int _geminiRequestsPerMinuteOcr = 8;
        private bool _geminiEnableMultiKeyOcr = false;
        #region Multi-Account Google OCR
        private class GoogleAccountContext
        {
            public string AccountName { get; init; }
            public string DriveFolderId { get; init; }
            public GoogleDriveService DriveService { get; init; }
            public GoogleOcrProcessor OcrProcessor { get; init; }
        }
        private List<GoogleAccountContext> _googleAccounts = new List<GoogleAccountContext>();
        #endregion
        public ObservableCollection<SrtSubtitleLine> SrtSubtitleLinesView { get; set; }
        private bool _isSrtTranslating = false;
        public bool IsSrtTranslating { get => _isSrtTranslating; set { if (_isSrtTranslating != value) { _isSrtTranslating = value; OnPropertyChanged(nameof(IsSrtTranslating)); } } }
        private string _currentSrtFilePath = "";
        // SRT Translation Config
        private string _chutesApiKeySrt = "";
        private List<string> _geminiApiKeysSrt = new List<string>();
        private bool _geminiEnableMultiKeySrt = false;
        private SrtApiProvider _currentSrtApiProvider = SrtApiProvider.AIOLauncher;
        private string _selectedChutesModelSrt = "google/gemini-pro";
        private string _selectedGeminiModelSrt = "gemini-2.5-flash";
        private string _selectedChatGPTModelSrt = "gpt-4o";
        private List<string> _chutesCustomModelsSrt = new List<string>();
        private List<string> _geminiCustomModelsSrt = new List<string>();
        private List<string> _chatGptCustomModelsSrt = new List<string>();
        private int _geminiSrtTranslationRpm = 8;
        private int _geminiSrtTranslationBatchSize = 40;
        private int _geminiSrtThinkingBudget = 8192;
        private int _chatGptSrtBatchSize = 40;
        private string _selectedSrtGenreValue = "H.Huyễn Tiên Hiệp";
        private string _selectedSrtTargetLanguage = "Tiếng Việt";
        private readonly HashSet<string> _srtErrorMarkers;
        private const int MAX_OCR_THREADS_GOOGLE = 50;
        private OcrMode _currentOcrMode = OcrMode.GeminiApi;
        // --- Timeline & Waveform State ---
        private AudioTrackModel _mainAudioTrack;
        private double _waveformZoomLevel = 1.0;
        private const double MIN_PIXELS_PER_SECOND = 0.2;
        private const double MAX_PIXELS_PER_SECOND = 1000.0;
        private const double ZOOM_STEP = 1.2;
        private const double MAX_DB = 20.0;
        private const double MIN_DB = -60.0;
        private const double MUTE_THRESHOLD_DB = -59.0;
        private Border _audioTrackContainer;
        private System.Windows.Point _lastMousePosition;
        private const double AUDIO_TRACK_CONTAINER_HEIGHT = 80.0;
        private const double WAVEFORM_DRAWING_HEIGHT = 40.0;
        private const double SUBTITLE_TRACK_AREA_HEIGHT = 100.0;
        private TimeSpan _visibleDuration = TimeSpan.FromSeconds(60);
        #endregion
        #region Timeline Clip Drag-Move State
        private bool _isDraggingClip = false;
        private TimelineClipViewModel _draggedClipVM = null;
        private double _dragInitialMouseX;
        private TimeSpan _dragInitialStartTime;
        private TimeSpan _dragInitialEndTime;
        private double _dragInitialMouseY;
        private int _dragInitialTrackIndex;
        #endregion
        // === [transport & gating flags] ===
        private volatile bool _isExplicitSeekInProgress = false;
        private volatile bool _isSwitchingSource = false;
        private DateTime _suppressUiCorrectionsUntil = DateTime.MinValue;
        private string _currentProjectFilePath;
        // Transport control
        private volatile int _transportVersion = 0;
        private readonly TimeSpan _settleTolerance = TimeSpan.FromMilliseconds(100);
        private readonly TimeSpan _settleTimeout = TimeSpan.FromMilliseconds(200);
        private readonly TimeSpan _settlePoll = TimeSpan.FromMilliseconds(10);
        private const double DEFAULT_REFERENCE_WIDTH = 1280;
        private const double DEFAULT_REFERENCE_HEIGHT = 720;

        private int NewTransportVersion() => Interlocked.Increment(ref _transportVersion);
        private void BlackoutOn() { FFMEPlayer.Opacity = 0; }
        private void BlackoutOff() { FFMEPlayer.Opacity = 1; }
        private bool _suppressAdornerForHardSub = false;
        private bool IsHardSubModeActive()
        {
            return (SubtitleTab != null && SubtitleTab.IsChecked == true)
                   && (ModeOcrRadio != null && ModeOcrRadio.IsChecked == true);
        }
        private void UpdateAdornerSuppression()
        {
            _suppressAdornerForHardSub = IsHardSubModeActive();

            if (_suppressAdornerForHardSub)
            {
                RemoveVideoAdorner();
                RemoveImageAdorner();
                RemoveSubtitleAdorner();
                if (overlayCanvas != null)
                {
                    overlayCanvas.IsHitTestVisible = true;
                    Panel.SetZIndex(overlayCanvas, 5000);
                }
            }
        }
        private async Task<bool> WaitForSeekSettleAsync(TimeSpan targetInClip, int token)
        {
            var start = Environment.TickCount; while (Environment.TickCount - start < _settleTimeout.TotalMilliseconds)
            {
                if (token != Volatile.Read(ref _transportVersion)) { return false; }
                var cur = FFMEPlayer.Position;
                if (cur >= targetInClip - _settleTolerance && cur <= targetInClip + _settleTolerance)
                { return true; }
                await Task.Delay(_settlePoll);
            }
            return false;
        }
        public MainWindow()
        {
            try { string ffmpegDllFolder = AppDomain.CurrentDomain.BaseDirectory; FFmpegWaveformExtractor.Register(ffmpegDllFolder); } catch (Exception ex) { }
            try { string ffmpegPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"); if (!Directory.Exists(ffmpegPath)) { } else { Unosquare.FFME.Library.FFmpegDirectory = ffmpegPath; } }
            catch (Exception ex) { }
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            InitializeSelectedTextEditorImeSupport();

            CustomPromptNames = new ObservableCollection<string>();
            FFMEPlayer.RendererOptions.VideoImageType = VideoRendererImageType.WriteableBitmap;
            RenderOptions.SetBitmapScalingMode(FFMEPlayer, BitmapScalingMode.LowQuality);
            overlayCanvas.CacheMode = new BitmapCache();
            RenderOptions.SetBitmapScalingMode(overlayCanvas, BitmapScalingMode.LowQuality);
            FFMEPlayer.MediaOpening += FFMEPlayer_MediaOpening;
            _audioEngine = new TimelineAudioEngine();
            InitializeStylePresets();
            videoGridScaleTransform.ScaleX = videoGridScaleTransform.ScaleY = _playerPanelScale;
            FFMEPlayer.MediaOpened += FFMEPlayer_MediaOpened;
            FFMEPlayer.MediaEnded += FFMEPlayer_MediaEnded;
            FFMEPlayer.MediaFailed += FFMEPlayer_MediaFailed;
            FFMEPlayer.PositionChanged += FFMEPlayer_PositionChanged;
            ffmpeg.RootPath = AppDomain.CurrentDomain.BaseDirectory;
            PositionMarkerThumb.DragStarted += PositionMarkerThumb_DragStarted;
            PositionMarkerThumb.DragDelta += PositionMarkerThumb_DragDelta;
            PositionMarkerThumb.DragCompleted += PositionMarkerThumb_DragCompleted;
            MainContentGrid.PreviewMouseLeftButtonDown += MainContentGrid_PreviewMouseLeftButtonDown;
            this.DataContext = this;
            SrtSubtitleLinesView = new ObservableCollection<SrtSubtitleLine>();
            SubtitleAssets = new ObservableCollection<MediaAsset>();
            VideoImageAssets = new ObservableCollection<MediaAsset>();
            AudioAssets = new ObservableCollection<MediaAsset>();
            TimelineAudioClips = new ObservableCollection<TimelineAudioClip>();
            TimelineClips = new ObservableCollection<TimelineClipViewModel>();
            TimelineClips.CollectionChanged += TimelineClips_CollectionChanged_ForAudioSync;
            foreach (var vm in TimelineClips) { AttachHandlersForClipVM(vm); }
            TimelineClips.CollectionChanged += TimelineClips_CollectionChanged_ForIndex;
            RebuildSortedClipsIndex();
            VisibleTimelineClips = new ObservableCollection<TimelineClipViewModel>();
            VisibleTimelineClips.CollectionChanged += VisibleTimelineClips_CollectionChanged;
            _zoomRenderDebounce.Tick += (s, e) => { _zoomRenderDebounce.Stop(); RenderTimeline(this._pixelsPerSecond); RenderVisibleClipsOnly(); DrawRuler(this._pixelsPerSecond); UpdatePositionMarkerVisuals(); };
            _scrollRenderDebounce.Tick += (s, e) =>
            {
                if (!_scrollDirty) { _scrollRenderDebounce.Stop(); return; }
                _scrollDirty = false;

                DrawRuler(this._pixelsPerSecond);
                UpdateVisibleClips();
                RenderVisibleClipsOnly();
                UpdatePositionMarkerVisuals();
                TracksContainerGrid.InvalidateMeasure();
                TracksContainerGrid.UpdateLayout();
            };

            TempFileManager.CleanupPreviousSessionFiles();
            _vsfService = new VsfService { LogMessage = (msg, isErr) => LogMessage(msg, isErr), OnProgress = (p) => Dispatcher.Invoke(() => { /* */ }) };
            _whisperService = new WhisperService { LogMessage = (msg, isErr) => LogMessage(msg, isErr) };
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _progressTimer.Tick += (s, e) => { /* */ };
            AudioSpeedComboBox.ItemsSource = new List<double> { 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 2.0, 3.0, 4.0 };
            _operationStopwatch = new Stopwatch();
            _mainAudioTrack = new AudioTrackModel();
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath));
            LoadConfiguration();
            InitializeVsfThreadComboBox();
            ResetApplicationState(true);
            InitializeEditorEvents();
            FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
            TempFileManager.CleanupPreviousSessionFiles();
            this.DataContext = this;
            _srtErrorMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "[API KHÔNG TRẢ VỀ DÒNG NÀY]", "[API DỊCH RỖNG]", "[LỖI DỊCH BATCH]", "[LỖI BATCH NGHIÊM TRỌNG]", "[LỖI PHẢN HỒI RỖNG TỪ API]", "[LỖI PAYLOAD RỖNG]", "[LỖI PARSE (RETRY)]", "[KHÔNG CÓ PHẢN HỒI (RETRY)]", "[LỖI NGHIÊM TRỌNG (RETRY)]", "[API DỊCH RỖNG (RETRY)]" };
            SrtLinesDataGrid.SelectionChanged += SrtLinesDataGrid_SelectionChanged;
            SrtApiProviderComboBox.ItemsSource = Enum.GetValues(typeof(SrtApiProvider));
            SrtGenreComboBox.ItemsSource = new List<string> { "H.Huyễn Tiên Hiệp", "Ngôn Tình", "Đô Thị Hiện Đại", "Khoa học lịch sử" };
            SrtTargetLanguageComboBox.ItemsSource = new List<string> {
                "Tiếng Việt",
                "Tiếng Anh",
                "Tiếng Trung (Giản thể)",
                "Tiếng Trung (Phồn thể)",
                "Tiếng Ả Rập",
                "Tiếng Bengali",
                "Tiếng Bungary",
                "Tiếng Catalan",
                "Tiếng Croatia",
                "Tiếng Séc",
                "Tiếng Đan Mạch",
                "Tiếng Hà Lan",
                "Tiếng Estonia",
                "Tiếng Filipino",
                "Tiếng Phần Lan",
                "Tiếng Pháp",
                "Tiếng Đức",
                "Tiếng Hy Lạp",
                "Tiếng Gujarati",
                "Tiếng Do Thái",
                "Tiếng Hindi",
                "Tiếng Hungary",
                "Tiếng Iceland",
                "Tiếng Indonesia",
                "Tiếng Ireland",
                "Tiếng Ý",
                "Tiếng Nhật",
                "Tiếng Java",
                "Tiếng Kannada",
                "Tiếng Khmer",
                "Tiếng Hàn",
                "Tiếng Lào",
                "Tiếng Latin",
                "Tiếng Latvia",
                "Tiếng Litva",
                "Tiếng Mã Lai",
                "Tiếng Malayalam",
                "Tiếng Marathi",
                "Tiếng Miến Điện",
                "Tiếng Nepal",
                "Tiếng Na Uy",
                "Tiếng Ba Tư",
                "Tiếng Ba Lan",
                "Tiếng Bồ Đào Nha",
                "Tiếng Punjabi",
                "Tiếng Rumani",
                "Tiếng Nga",
                "Tiếng Serbia",
                "Tiếng Slovak",
                "Tiếng Slovenia",
                "Tiếng Tây Ban Nha",
                "Tiếng Sunda",
                "Tiếng Swahili",
                "Tiếng Thụy Điển",
                "Tiếng Tamil",
                "Tiếng Telugu",
                "Tiếng Thái",
                "Tiếng Thổ Nhĩ Kỳ",
                "Tiếng Ukraina",
                "Tiếng Urdu",
                "Tiếng Uzbek",
                "Tiếng Wales"
            };
            InitializeSubtitleStyler();
            InitializeWhisperControls();
            UpdateTimeInputStates();
            UpdateResultsText();
            InitializeTtsComponents();
            this.PreviewMouseMove += MainWindow_PreviewMouseMove;
            this.PreviewMouseLeftButtonUp += MainWindow_PreviewMouseLeftButtonUp;
            this.Closing += MainWindow_OnClosing;
            this.StateChanged += MainWindow_StateChanged;
            MainContentGrid.PreviewMouseLeftButtonDown += MainContentGrid_PreviewMouseLeftButtonDown;
            if (_totalTimelineDuration.TotalSeconds <= 0) { _totalTimelineDuration = TimeSpan.FromMinutes(5); }
            UpdateTimelineScaleAndRender();
            UpdatePresetMenu();
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _timelineRulerVisual = new TimelineRulerVisual();
            _rulerHost = new VisualHost(_timelineRulerVisual.Visual);
            TimelineRulerHost.Child = _rulerHost;
        }
        private void RebuildSortedClipsIndex()
        {
            _clipsSortedByStart = TimelineClips.OrderBy(c => c.StartTime).ToList();
        }

        private void InsertIntoSortedIndex(TimelineClipViewModel clip)
        {
            int idx = _clipsSortedByStart.BinarySearch(clip, TimelineClipStartComparer.Instance);
            if (idx < 0) idx = ~idx;
            _clipsSortedByStart.Insert(idx, clip);
        }

        // REPLACE ENTIRE METHOD: TimelineClips_CollectionChanged_ForIndex
        private async void TimelineClips_CollectionChanged_ForIndex(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Trạng thái trước khi thêm: đã có clip video nào trong index chưa?
            bool hadAnyVideoBefore = _clipsSortedByStart != null && _clipsSortedByStart.Any(c => c != null && c.ClipType == TimelineClipType.Video);

            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                // Cập nhật chỉ mục theo StartTime như cũ
                foreach (TimelineClipViewModel clip in e.NewItems)
                    InsertIntoSortedIndex(clip);

                // === FIX: Nếu đây là lần đầu timeline có clip video, khởi tạo player ngay lập tức ===
                if (!hadAnyVideoBefore)
                {
                    var firstVideoJustAdded = e.NewItems
                        .Cast<TimelineClipViewModel>()
                        .Where(vm => vm != null && vm.ClipType == TimelineClipType.Video)
                        .OrderBy(vm => vm.StartTime)
                        .FirstOrDefault();

                    if (firstVideoJustAdded != null)
                    {
                        // Đảm bảo player sẵn sàng cho clip video đầu tiên
                        await EnsurePlayerReadyOnFirstVideoClipAsync(firstVideoJustAdded);
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TimelineClipViewModel clip in e.OldItems)
                    _clipsSortedByStart.Remove(clip);
            }
            else
            {
                RebuildSortedClipsIndex();
            }
        }
        private async Task EnsurePlayerReadyOnFirstVideoClipAsync(TimelineClipViewModel newVideoClip)
        {
            try
            {
                if (newVideoClip == null || !newVideoClip.IsVideo)
                    return;

                // Nếu player đã mở một clip video nào đó rồi thì bỏ qua
                if (FFMEPlayer != null && FFMEPlayer.IsInitialized && _activeVideoClip != null)
                    return;

                // Bảo vệ: file path phải hợp lệ
                if (string.IsNullOrWhiteSpace(newVideoClip.FilePath) || !System.IO.File.Exists(newVideoClip.FilePath))
                    return;

                // Giữ nguyên playhead hiện tại; yêu cầu seek tương ứng khi mở source mới
                _pendingSeekForNewSource = _playhead;

                // Switch sang clip video đầu tiên trên timeline để buộc FFMEPlayer.Open chạy
                await SwitchActiveVideoClip(newVideoClip, resume: false);

                // Đảm bảo dừng ở khung tĩnh (không tự phát)
                if (FFMEPlayer != null && FFMEPlayer.CanPause)
                    await FFMEPlayer.Pause();
            }
            catch
            {
                // An toàn: nếu có lỗi, trả player về trạng thái sạch để tránh "treo"
                try { await ResetVideoPlayerState(); } catch { /* swallow */ }
            }
            finally
            {
                // Đảm bảo không để blackout bị kẹt nếu trước đó bật
                BlackoutOff();
            }
        }
        private void LoadGoogleAccounts()
        {
            _googleAccounts.Clear();
            string oauthRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oauth2.0");
            if (!Directory.Exists(oauthRoot))
            {
                return;
            }

            var accountDirectories = Directory.GetDirectories(oauthRoot);
            foreach (var accDir in accountDirectories)
            {
                string accountName = Path.GetFileName(accDir);
                string credentialsPath = Path.Combine(accDir, "credentials.json");
                string tokenDirPath = Path.Combine(accDir, "token.json");

                if (!File.Exists(credentialsPath))
                {
                    continue;
                }
                var folderIdFile = Directory.GetFiles(accDir)
                    .FirstOrDefault(f => string.IsNullOrEmpty(Path.GetExtension(f)) &&
                                         !Path.GetFileName(f).Equals("credentials.json", StringComparison.OrdinalIgnoreCase));

                if (folderIdFile == null)
                {
                    continue;
                }

                string driveFolderId = Path.GetFileName(folderIdFile);
                if (string.IsNullOrWhiteSpace(driveFolderId))
                {
                    continue;
                }

                try
                {
                    var driveService = new GoogleDriveService(credentialsPath, tokenDirPath, accountName)
                    {
                        LogMessage = (msg, isErr) => LogMessage(msg, isErr)
                    };

                    var semaphore = new SemaphoreSlim(MAX_OCR_THREADS_GOOGLE, MAX_OCR_THREADS_GOOGLE);
                    var ocrProcessor = new GoogleOcrProcessor(driveService, semaphore);
                    ocrProcessor.ErrorImageCopied += CopyFailedImage;

                    _googleAccounts.Add(new GoogleAccountContext
                    {
                        AccountName = accountName,
                        DriveFolderId = driveFolderId,
                        DriveService = driveService,
                        OcrProcessor = ocrProcessor
                    });
                }
                catch (Exception ex) { }
            }
        }
        private void InitializeSelectedTextEditorImeSupport()
        {
            if (SelectedTextEditorTextBox == null)
                return;
            InputMethod.SetIsInputMethodEnabled(SelectedTextEditorTextBox, true);
            InputMethod.SetPreferredImeState(SelectedTextEditorTextBox, InputMethodState.On);
            TextCompositionManager.AddPreviewTextInputStartHandler(SelectedTextEditorTextBox, SelectedTextEditorTextBox_TextInputStart);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(SelectedTextEditorTextBox, SelectedTextEditorTextBox_TextInputUpdate);
            TextCompositionManager.AddPreviewTextInputHandler(SelectedTextEditorTextBox, SelectedTextEditorTextBox_TextInputCommit);
            SelectedTextEditorTextBox.LostKeyboardFocus += (s, e) =>
            {
                _imeCompositionActive = false;
                _imeCompositionJustCommitted = false;
            };
        }
        private void SelectedTextEditorTextBox_TextInputStart(object sender, TextCompositionEventArgs e)
        {
            _imeCompositionActive = true;
            _imeCompositionJustCommitted = false;
        }
        private void SelectedTextEditorTextBox_TextInputUpdate(object sender, TextCompositionEventArgs e)
        {
            _imeCompositionActive = true;
            _imeCompositionJustCommitted = false;
        }
        private void SelectedTextEditorTextBox_TextInputCommit(object sender, TextCompositionEventArgs e)
        {
            _imeCompositionActive = false;
            _imeCompositionJustCommitted = true;
        }

        private const string CUSTOM_PROMPT_SUFFIX = @"

QUY TẮC CHUNG:
1. Kết quả là văn bản thuần túy, KHÔNG thêm lời dẫn, chú thích, markdown, in đậm/in nghiêng.
2. Mỗi dòng phải giữ nguyên số thứ tự. Ví dụ: 123: Nội dung dịch. Nếu không có gì để dịch, trả về: 123: Không có nội dung để dịch.
3. KHÔNG tự ý thêm dấu chấm, dấu phẩy ở đầu/cuối câu.";

        private void FFMEPlayer_MediaOpening(object sender, Unosquare.FFME.Common.MediaOpeningEventArgs e) { e.Options.VideoFilter = "scale=w='min(1920,iw)':h=-2:flags=fast_bilinear"; }
        private void QueueWaveformGeneration(object sourceData)
        {
            WavePeakPyramid existingPyramid = null;
            if (sourceData is MediaAsset ma_check) existingPyramid = ma_check.WavePeaks;
            else if (sourceData is TimelineAudioClip tac_check) existingPyramid = tac_check.WavePeaks;
            if (existingPyramid != null) return;
            string filePath = null;
            Action<WavePeakPyramid> onComplete = null;

            if (sourceData is MediaAsset ma && (ma.Type == AssetType.Video || ma.Type == AssetType.Audio))
            {
                filePath = ma.FilePath;
                onComplete = (pyramid) => ma.WavePeaks = pyramid;
            }
            else if (sourceData is TimelineAudioClip tac)
            {
                filePath = tac.FilePath;
                onComplete = (pyramid) => tac.WavePeaks = pyramid;
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            _ = Task.Run(async () =>
            {
                if (WavePeakCache.TryLoad(filePath, out var pyramid))
                {
                    onComplete(pyramid);
                }
                else
                {
                    pyramid = await WavePeakCache.BuildAsync(filePath, CancellationToken.None);
                    onComplete(pyramid);
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    var vm = TimelineClips.FirstOrDefault(c => c.SourceData == sourceData);
                    if (vm != null)
                    {
                        vm.OnPropertyChanged(nameof(vm.WavePeaks));
                    }
                });
            });
        }

        private async void FFMEPlayer_MediaFailed(object sender, Unosquare.FFME.Common.MediaFailedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                BlackoutOff();
                _isSwitchingVideoSource = false;

                CustomMessageBox.Show(
                    $"Không thể mở hoặc phát file media.\n\nLỗi: {e.ErrorException.Message}",
                    "Lỗi Trình Phát",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                await ResetVideoPlayerState();
            });
        }
        private void UpdateSubtitleButtonVisibility()
        {
            if (DefaultOcrButtonsPanel == null || StartBatchSubtitleCreationButton == null) return;

            if (IsBatchSubtitleMode)
            {
                DefaultOcrButtonsPanel.Visibility = Visibility.Collapsed;
                StartBatchSubtitleCreationButton.Visibility = Visibility.Visible;
            }
            else
            {
                DefaultOcrButtonsPanel.Visibility = Visibility.Visible;
                StartBatchSubtitleCreationButton.Visibility = Visibility.Collapsed;
            }
        }
        private void ClearAllAdorners()
        {
            RemoveSubtitleAdorner();
            RemoveVideoAdorner();
            RemoveImageAdorner();
            RemoveBlurAdorner();
            _activeTransformingImageAsset = null;
            _activeTransformingVideoAsset = null;
            _selectedSubtitle = null;
            _isTextInEditMode = false;
            UpdateEditorPanelVisibility();
        }
        private void MainContentGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {


            var source = e.OriginalSource as DependencyObject;
            if (IsAuxUiActive)
            {
                return;
            }
            bool isClickOnNonPlayerZone =
                FindVisualParent<ContentPresenter>(source)?.DataContext is TimelineClipViewModel ||
                IsDescendantOf(source, EditorPanel) ||
                IsDescendantOf(source, AudioEditorPanel) ||
                IsDescendantOf(source, VideoEditorPanel) ||
                IsInsidePopup(source) ||
                FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(source) != null;

            if (isClickOnNonPlayerZone)
            {
                return;
            }
            if (overlayCanvas != null)
            {
                System.Windows.Point ptOverlay = e.GetPosition(overlayCanvas);
                var hitOverlay = overlayCanvas.InputHitTest(ptOverlay) as DependencyObject;
                if (hitOverlay != null)
                {
                    var thumbOnOverlay =
                        FindVisualParent<System.Windows.Controls.Primitives.Thumb>(hitOverlay) ??
                        (hitOverlay as System.Windows.Controls.Primitives.Thumb);

                    if (thumbOnOverlay != null)
                    {
                        return;
                    }
                }
            }

            bool clickedPlayerArea = IsDescendantOf(source, VideoContainerBorder);
            if (clickedPlayerArea)
            {
                if (FindVisualParent<Adorner>(source) != null)
                {
                    return;
                }
                System.Windows.Point clickPoint = e.GetPosition(SubtitleRenderCanvas);
                var hitObject = SubtitleRenderCanvas.InputHitTest(clickPoint) as DependencyObject;
                if (hitObject != null)
                {

                    var subtitleVisual = FindVisualParent<Border>(hitObject);
                    if (subtitleVisual != null && subtitleVisual.DataContext is SrtSubtitleLine srtLine)
                    {
                        return;
                    }
                    var blurVisual = FindVisualParent<Border>(hitObject);
                    if (blurVisual != null && blurVisual.Name == "BlurPreview" && blurVisual.DataContext is MediaAsset ma && ma.Type == AssetType.Blur)
                    {
                        return;
                    }

                }
                if (IsPointInsideAnyImageOverlay(clickPoint))
                {
                    return;
                }
                if (GetReferenceVideoFrameRect().Contains(e.GetPosition(PlayerAdornerDecorator)))
                {
                    var currentVideoClip = FindActiveVideoClipAt(_playhead);
                    if (currentVideoClip != null)
                    {
                        SwitchToClip(currentVideoClip);
                        e.Handled = true;
                        return;
                    }
                }
            }
            ClearAllAdorners();
            if (_selectedTimelineClip != null)
            {
                if (_selectedTimelineClip?.SourceData is INotifyPropertyChanged oldSource)
                    oldSource.PropertyChanged -= SelectedClip_PropertyChanged;

                _selectedTimelineClip.IsSelected = false;
                _selectedTimelineClip = null;
            }
            if (SelectedSubtitle != null)
            {
                SelectedSubtitle = null;
            }
            UpdateEditorPanelVisibility();
        }
        private bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }

            DependencyObject current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
        private void AudioSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // 1) Lấy giá trị speed mới từ ComboBox (hỗ trợ nhiều kiểu item)
                double ParseSpeed(object item)
                {
                    if (item == null) return 1.0;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;

                    if (item is double d) return d > 0 ? d : 1.0;
                    if (item is float f) return f > 0 ? (double)f : 1.0;
                    if (item is string s)
                    {
                        // chấp nhận "1.25", "1.25x", "x1.25", có/dấu phẩy…
                        s = s.Trim().ToLower().Replace("x", "");
                        s = s.Replace(',', '.'); // đề phòng locale
                        if (double.TryParse(s, System.Globalization.NumberStyles.Float, ci, out var v) && v > 0) return v;
                    }
                    if (item is ComboBoxItem cbi)
                    {
                        var txt = cbi.Content?.ToString()?.Trim()?.ToLower();
                        if (!string.IsNullOrEmpty(txt))
                        {
                            txt = txt.Replace("x", "").Replace(',', '.');
                            if (double.TryParse(txt, System.Globalization.NumberStyles.Float, ci, out var v2) && v2 > 0) return v2;
                        }
                    }
                    return 1.0;
                }

                var combo = sender as ComboBox;
                if (combo == null) return;

                double newSpeed = ParseSpeed(combo.SelectedItem);
                if (newSpeed <= 0) return;

                // 2) Xác định danh sách VM audio đang được chọn
                var selectedVMs = TimelineClips
                    .Where(vm => vm.IsSelected && vm.ClipType == TimelineClipType.Audio)
                    .ToList();

                // Nếu vì lý do nào đó chưa có VM chọn, nhưng có SelectedAudioClip thì áp dụng cho nó
                if (selectedVMs.Count == 0 && SelectedAudioClip != null)
                {
                    var vmSingle = TimelineClips.FirstOrDefault(vm => vm.SourceData == SelectedAudioClip);
                    if (vmSingle != null) selectedVMs.Add(vmSingle);
                }

                if (selectedVMs.Count == 0) return;

                _isApplyingBulkAudioChange = true;

                // 3) Áp dụng speed xuống model (hỗ trợ cả MediaAsset và TimelineAudioClip)
                foreach (var vm in selectedVMs)
                {
                    if (vm.SourceData is MediaAsset ma && ma.Type == AssetType.Audio)
                    {
                        if (Math.Abs(ma.Speed - newSpeed) > 1e-9)
                            ma.Speed = newSpeed;
                    }
                    else if (vm.SourceData is TimelineAudioClip tac)
                    {
                        if (Math.Abs(tac.Speed - newSpeed) > 1e-9)
                            tac.Speed = newSpeed;
                    }
                }

                // 4) Cập nhật playback ngay cho source đang active, làm tươi VM và width
                foreach (var vm in selectedVMs)
                {
                    // cập nhật audio engine nếu clip đang phát
                    _audioEngine.UpdateClipProperties(vm); // playback cập nhật speed tức thì

                    // làm tươi binding (Duration/EndTime/Speed/Volume)
                    vm.RefreshPropertiesFromSource();

                    // tính lại width theo duration mới
                    if (vm.SourceData is TimelineAudioClip tac2)
                    {
                        vm.Width = Math.Max(1.0, tac2.EffectiveDuration.TotalSeconds * _pixelsPerSecond);
                    }
                    else if (vm.SourceData is MediaAsset ma2)
                    {
                        vm.Width = Math.Max(1.0, ma2.EffectiveDuration.TotalSeconds * _pixelsPerSecond);
                    }
                }

                // 5) Tính lại tổng thời lượng + vẽ lại timeline
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();

                // 6) Ghi undo + lưu project để ffmpeg dùng speed mới
                _undoRedoService?.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
            }
            finally
            {
                _isApplyingBulkAudioChange = false;
            }
        }

        private void UpdateVisibleClips()
        {
            if (_totalTimelineDuration.TotalSeconds <= 0 || double.IsNaN(TracksContainerGrid.Width) || TracksContainerGrid.Width <= 0)
            {
                if (VisibleTimelineClips.Any()) VisibleTimelineClips.Clear();
                return;
            }

            double pps = this._pixelsPerSecond;
            if (pps <= 0) return;

            double viewportStart = TimelineScrollViewer.HorizontalOffset;
            double viewportEnd = viewportStart + TimelineScrollViewer.ViewportWidth;

            TimeSpan timeStart = TimeSpan.FromSeconds(viewportStart / pps);
            TimeSpan timeEnd = TimeSpan.FromSeconds(viewportEnd / pps);

            // Buffer tránh pop-in khi kéo nhanh
            TimeSpan buffer = TimeSpan.FromSeconds(10);
            if (timeStart > buffer) timeStart -= buffer; else timeStart = TimeSpan.Zero;
            timeEnd += buffer;

            if (_clipsSortedByStart.Count == 0)
            {
                if (VisibleTimelineClips.Any()) VisibleTimelineClips.Clear();
                return;
            }

            // *** SỬA LỖI: Thay thế binary search bằng vòng lặp đơn giản để đảm bảo đúng ***
            // Logic cũ (bị lỗi) đã được thay thế bằng logic mới dưới đây.
            var newVisible = new HashSet<TimelineClipViewModel>();
            foreach (var clip in _clipsSortedByStart)
            {
                // Điều kiện chuẩn để kiểm tra hai khoảng thời gian có giao nhau không
                // Một clip được coi là "nhìn thấy" nếu nó kết thúc SAU KHI viewport bắt đầu
                // VÀ nó bắt đầu TRƯỚC KHI viewport kết thúc.
                if (clip.EndTime > timeStart && clip.StartTime < timeEnd)
                {
                    newVisible.Add(clip);
                }
            }

            // Diff apply để hạn chế churn UI (Giữ nguyên phần này)
            // Loại bỏ các clip không còn hiển thị
            for (int i = VisibleTimelineClips.Count - 1; i >= 0; i--)
            {
                var c = VisibleTimelineClips[i];
                if (!newVisible.Contains(c))
                {
                    VisibleTimelineClips.RemoveAt(i);
                }
            }
            // Thêm các clip mới cần hiển thị
            foreach (var c in newVisible)
            {
                if (!VisibleTimelineClips.Contains(c))
                {
                    VisibleTimelineClips.Add(c);
                }
            }
        }
        public class TtsProviderInfo
        {
            public string DisplayName { get; set; }
            public string ApiName { get; set; }
        }
        private void TtsInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int currentLength = TtsInputTextBox.Text.Length;
            long remaining = App.User.TtsCharacterLimit - App.User.TtsCharactersUsed;
            TtsCharCountTextBlock.Text = $"{currentLength} / {remaining:N0}";
            if (currentLength > remaining)
            {
                TtsCharCountTextBlock.Foreground = Brushes.Red;
                GenerateTtsButton.IsEnabled = false;
            }
            else
            {
                TtsCharCountTextBlock.Foreground = (SolidColorBrush)FindResource("CapCut.TextSecondaryColor");
                GenerateTtsButton.IsEnabled = true;
            }
        }
        private void InitializeTtsComponents()
        {
            _ttsAudioPlayer = new Unosquare.FFME.MediaElement
            {
                LoadedBehavior = Unosquare.FFME.Common.MediaPlaybackState.Manual,
                UnloadedBehavior = Unosquare.FFME.Common.MediaPlaybackState.Close
            };

            TtsPlayerHost.Children.Add(_ttsAudioPlayer);
            TtsPlaybackControlsPanel.Visibility = Visibility.Collapsed;
            _ttsAudioPlayer.MediaOpened += TtsAudioPlayer_MediaOpened;
            _ttsAudioPlayer.MediaEnded += TtsAudioPlayer_MediaEnded;

            _ttsProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _ttsProgressTimer.Tick += TtsProgressTimer_Tick;


            var languages = new List<TtsLanguage>
    {
        new TtsLanguage("Tiếng Việt (vi-VN)", "vi-VN"),
        new TtsLanguage("English (United States)", "en-US"),
        new TtsLanguage("English (United Kingdom)", "en-GB"),
        new TtsLanguage("English (India)", "en-IN"),
        new TtsLanguage("English (Australia)", "en-AU"),
        new TtsLanguage("Español (España)", "es-ES"),
        new TtsLanguage("Español (US)", "es-US"),
        new TtsLanguage("Français (France)", "fr-FR"),
        new TtsLanguage("Deutsch (Deutschland)", "de-DE"),
        new TtsLanguage("Português (Brasil)", "pt-BR"),
        new TtsLanguage("Italiano (Italia)", "it-IT"),
        new TtsLanguage("日本語 (ja-JP)", "ja-JP"),
        new TtsLanguage("한국어 (ko-KR)", "ko-KR"),
        new TtsLanguage("中文普通话 (cmn-CN)", "cmn-CN"),
        new TtsLanguage("हिन्दी (भारत)", "hi-IN"),
        new TtsLanguage("Русский (Россия)", "ru-RU"),
        new TtsLanguage("ไทย (th-TH)", "th-TH"),
        new TtsLanguage("Bahasa Indonesia (id-ID)", "id-ID"),
        new TtsLanguage("العربية (ar-XA)", "ar-XA")
    };
            AioLanguageComboBox.ItemsSource = languages;
            AioLanguageComboBox.SelectedIndex = 0;
            var voices = new List<TtsVoice>
    {
        new TtsVoice("Hương Giang (Nữ)", "Achernar"),
        new TtsVoice("Quang Hùng (Nam)", "Achird"),
        new TtsVoice("Podcast (Nam)", "Algenib"),
        new TtsVoice("Podcast 2 (Nam)", "Algieba"),
        new TtsVoice("Quang Minh (Nam)", "Alnilam"),
        new TtsVoice("Ngọc Nhi (Nữ)", "Aoede"),
        new TtsVoice("Quỳnh Như (Nữ)", "Autonoe"),
        new TtsVoice("Hương Tràm (Nữ)", "Callirrhoe"),
        new TtsVoice("Văn Hoàng (Nam)", "Charon"),
        new TtsVoice("Ngọc Diễm (Nữ)", "Despina"),
        new TtsVoice("Trung Thường (Nam)", "Enceladus"),
        new TtsVoice("Lan Anh (Nữ)", "Erinome"),
        new TtsVoice("Nam Miền Nam", "Fenrir"),
        new TtsVoice("MC Ngọc Trinh (Nữ)", "Gacrux"),
        new TtsVoice("MC Nam Minh (Nam)", "Iapetus"),
        new TtsVoice("Hoa (Nữ)", "Kore"),
        new TtsVoice("Quỳnh Anh (Nữ)", "Laomedeia"),
        new TtsVoice("Ngọc Diệp (Nữ)", "Leda"),
        new TtsVoice("Quang Toản (Nam)", "Orus"),
        new TtsVoice("MC Miền Nam (Nam)", "Pulcherrima"),
        new TtsVoice("Nam Miền Nam 2", "Puck"),
        new TtsVoice("Thành Tiến (Nam)", "Rasalgethi"),
        new TtsVoice("Nam Minh (Nam)", "Sadachbia"),
        new TtsVoice("Trầm Giọng Bắc (Nam)", "Sadaltager"),
        new TtsVoice("MC Đám Ma (Nam)", "Schedar"),
        new TtsVoice("Fake Ngọc Huyền (Nữ)", "Sulafat"),
        new TtsVoice("Quang Hải (Nam)", "Umbriel"),
        new TtsVoice("Giọng đọc thơ (Nữ)", "Vindemiatrix"),
        new TtsVoice("Thiền Anh (Nữ)", "Zephyr"),
        new TtsVoice("Nam Trầm (Nam)", "Zubenelgenubi")
    };
            AioVoiceComboBox.ItemsSource = voices;
            AioVoiceComboBox.SelectedIndex = 0;
        }

        private async void GenerateTtsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTtsPlaying)
            {
                await _ttsAudioPlayer.Stop();
                _ttsProgressTimer.Stop();
                _isTtsPlaying = false;
            }
            TtsPlaybackControlsPanel.Visibility = Visibility.Collapsed;

            string textToVoice = TtsInputTextBox.Text;
            if (string.IsNullOrWhiteSpace(textToVoice))
            {
                CustomMessageBox.Show("Vui lòng nhập văn bản cần tạo giọng nói.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (AioLanguageComboBox.SelectedValue == null || AioVoiceComboBox.SelectedValue == null)
            {
                CustomMessageBox.Show("Vui lòng chọn đầy đủ Ngôn ngữ và Giọng nói.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string language = AioLanguageComboBox.SelectedValue.ToString();
            string voiceId = AioVoiceComboBox.SelectedValue.ToString();
            double rate = AioRateSlider.Value;
            string optimizedText = OptimizeTextForTts(textToVoice);
            int totalBytes = Encoding.UTF8.GetByteCount(optimizedText);
            int characterCount = optimizedText.Length;

            await App.User.RefreshProfileAsync();
            long remainingChars = App.User.TtsCharacterLimit - App.User.TtsCharactersUsed;

            if (characterCount > remainingChars)
            {
                CustomMessageBox.Show($"Số ký tự văn bản ({characterCount:N0}) vượt quá giới hạn còn lại của bạn ({remainingChars:N0}).", "Không đủ ký tự", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            GenerateTtsButton.IsEnabled = false;
            GenerateTtsButton.Content = "Đang tạo...";
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                byte[] finalAudioData = null;

                if (totalBytes <= TTS_CHUNK_TRIGGER_BYTES)
                {
                    GenerateTtsButton.Content = "Đang tạo (1/1)...";
                    var (success, audioData, errorMessage) = await ApiService.GenerateAioTtsAsync(language, voiceId, rate, optimizedText);

                    if (success && audioData != null)
                    {
                        finalAudioData = audioData;
                    }
                    else
                    {
                        throw new Exception($"Tạo âm thanh thất bại: {errorMessage}");
                    }
                }
                else
                {
                    List<string> textChunks = SplitTextIntoChunks(optimizedText, TTS_CHUNK_TRIGGER_BYTES, TTS_CHUNK_TARGET_BYTES);
                    var allAudioChunks = new List<byte[]>();
                    int totalChunks = textChunks.Count;

                    for (int i = 0; i < totalChunks; i++)
                    {
                        GenerateTtsButton.Content = $"Đang tạo ({i + 1}/{totalChunks})...";
                        string chunk = textChunks[i];
                        var (success, audioData, errorMessage) = await ApiService.GenerateAioTtsAsync(language, voiceId, rate, chunk);

                        if (success && audioData != null)
                        {
                            allAudioChunks.Add(audioData);
                        }
                        else
                        {
                            throw new Exception($"Tạo âm thanh cho chunk {i + 1} thất bại: {errorMessage}");
                        }
                    }
                    if (allAudioChunks.Count > 0)
                    {
                        GenerateTtsButton.Content = "Đang ghép âm thanh...";
                        finalAudioData = await ConcatenateAudioChunksAsync(allAudioChunks);
                    }
                }

                if (finalAudioData != null)
                {
                    await App.User.RefreshProfileAsync();
                    TtsInputTextBox_TextChanged(null, null);

                    _currentTtsAudioPath = TempFileManager.CreateTempFile(".mp3");
                    await File.WriteAllBytesAsync(_currentTtsAudioPath, finalAudioData);

                    await _ttsAudioPlayer.Open(new Uri(_currentTtsAudioPath));
                    await _ttsAudioPlayer.Play();
                }
                else
                {
                    CustomMessageBox.Show("Không nhận được dữ liệu âm thanh sau khi xử lý.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi không mong muốn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateTtsButton.IsEnabled = true;
                GenerateTtsButton.Content = "Tạo Âm Thanh";
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private string OptimizeTextForTts(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return string.Empty;
                }
                string trimmedText = text.Trim();
                string baseCleanedText = Regex.Replace(trimmedText, @"\s+", " ");
                const int MAX_CHARS_WITHOUT_PUNCTUATION = 220;
                var finalResultBuilder = new StringBuilder();
                var sentences = Regex.Split(baseCleanedText, @"(?<=[.?!…])\s*");

                foreach (var sentence in sentences)
                {
                    if (string.IsNullOrWhiteSpace(sentence)) continue;

                    if (sentence.Length <= MAX_CHARS_WITHOUT_PUNCTUATION)
                    {
                        finalResultBuilder.Append(sentence).Append(" ");
                    }
                    else
                    {
                        string remainingSentence = sentence;

                        while (remainingSentence.Length > MAX_CHARS_WITHOUT_PUNCTUATION)
                        {
                            int breakPosition = remainingSentence.LastIndexOf(' ', MAX_CHARS_WITHOUT_PUNCTUATION);
                            if (breakPosition <= 0)
                            {
                                breakPosition = MAX_CHARS_WITHOUT_PUNCTUATION;
                            }

                            finalResultBuilder.Append(remainingSentence.Substring(0, breakPosition)).Append(". ");
                            remainingSentence = remainingSentence.Substring(breakPosition).TrimStart();
                        }
                        if (!string.IsNullOrWhiteSpace(remainingSentence))
                        {
                            finalResultBuilder.Append(remainingSentence).Append(" ");
                        }
                    }
                }
                string finalResult = finalResultBuilder.ToString().Trim();
                string textWithoutQuotes = finalResult
                    .Replace("\"", "")
                    .Replace("'", "")
                    .Replace("“", "")
                    .Replace("”", "")
                    .Replace("‘", "")
                    .Replace("’", "");


                return textWithoutQuotes;
            }
            catch (Exception ex)
            {
                return text?.Trim() ?? string.Empty;
            }
        }
        private List<string> SplitTextIntoChunks(string text, int maxBytes, int targetBytes)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return chunks;
            }
            var currentChunk = new StringBuilder();
            var sentences = Regex.Split(text, @"(?<=[.?!…])\s*");
            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                var sentenceBytes = Encoding.UTF8.GetByteCount(sentence);
                var currentChunkBytes = Encoding.UTF8.GetByteCount(currentChunk.ToString());
                if (sentenceBytes > maxBytes)
                {
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }

                    var words = sentence.Split(' ');
                    var wordChunk = new StringBuilder();
                    foreach (var word in words)
                    {
                        var wordWithSpace = word + " ";
                        if (Encoding.UTF8.GetByteCount(wordChunk.ToString() + wordWithSpace) > maxBytes)
                        {
                            chunks.Add(wordChunk.ToString().Trim());
                            wordChunk.Clear();
                        }
                        wordChunk.Append(wordWithSpace);
                    }
                    if (wordChunk.Length > 0)
                    {
                        chunks.Add(wordChunk.ToString().Trim());
                    }
                    continue;
                }
                if (currentChunk.Length > 0 &&
                    (currentChunkBytes + sentenceBytes > maxBytes || currentChunkBytes > targetBytes))
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                    currentChunkBytes = 0;
                }
                currentChunk.Append(sentence);
            }
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }
            return chunks;
        }
        private async Task<byte[]> ConcatenateAudioChunksAsync(List<byte[]> audioChunks)
        {
            if (audioChunks == null || audioChunks.Count == 0) return null;
            if (audioChunks.Count == 1) return audioChunks[0];

            var tempFilePaths = new List<string>();
            string tempDir = TempFileManager.CreateTempDirectory();
            string outputPath = Path.Combine(tempDir, "output.mp3");

            try
            {
                for (int i = 0; i < audioChunks.Count; i++)
                {
                    string tempFilePath = Path.Combine(tempDir, $"chunk_{i}.mp3");
                    await File.WriteAllBytesAsync(tempFilePath, audioChunks[i]);
                    tempFilePaths.Add(tempFilePath);
                }
                bool success = await FFMpegArguments
                    .FromConcatInput(tempFilePaths)
                    .OutputToFile(outputPath, true, options => options
                        .CopyChannel(Channel.Audio))
                    .ProcessAsynchronously();
                if (success && File.Exists(outputPath))
                {
                    byte[] resultData = await File.ReadAllBytesAsync(outputPath);
                    if (resultData.Length > 0)
                    {
                        return resultData;
                    }
                    else
                    {
                        return null;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex) { }
            }
        }
        private async void GenerateVoiceSubButton_Click(object sender, RoutedEventArgs e)
        {
            var linesToVoice = SrtLinesDataGrid.SelectedItems.Cast<SrtSubtitleLine>().ToList();
            if (!linesToVoice.Any())
            {
                linesToVoice = SrtSubtitleLinesView.ToList();
            }

            if (!linesToVoice.Any())
            {
                CustomMessageBox.Show("Không có dòng phụ đề nào để tạo giọng nói.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await GenerateVoiceForSrtLines(linesToVoice);
        }
        private async void CreateVoice_Click(object sender, RoutedEventArgs e)
        {
            var selectedClips = TimelineClips
                .Where(c => c.IsSelected && (c.ClipType == TimelineClipType.Text || c.ClipType == TimelineClipType.Subtitle))
                .ToList();

            var linesToVoice = selectedClips
                .Select(c => c.SourceData as SrtSubtitleLine)
                .Where(s => s != null)
                .ToList();

            if (!linesToVoice.Any())
            {
                CustomMessageBox.Show("Hãy chọn các clip phụ đề trên timeline để tạo voice.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string sourceTextChoice = "Original";
            bool hasTranslatedText = linesToVoice.Any(l => !string.IsNullOrWhiteSpace(l.TranslatedText));
            if (hasTranslatedText)
            {
                var result = CustomMessageBox.Show(
                    "Bạn muốn tạo giọng nói từ 'Văn bản gốc' hay 'Bản dịch'?\n\n- Chọn 'Yes' để dùng VĂN BẢN GỐC.\n- Chọn 'No' để dùng BẢN DỊCH.",
                    "Chọn nguồn văn bản",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.No) sourceTextChoice = "Translated";
            }
            if (AioLanguageComboBox.SelectedValue == null || AioVoiceComboBox.SelectedValue == null)
            {
                CustomMessageBox.Show("Vui lòng chọn đầy đủ Ngôn ngữ và Giọng nói trong tab Text to Speech.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string language = AioLanguageComboBox.SelectedValue.ToString();
            string voiceId = AioVoiceComboBox.SelectedValue.ToString();
            double rate = AioRateSlider.Value;

            string srtContent = SrtFileUtils.GenerateSrtContent(linesToVoice, sourceTextChoice == "Translated");
            long totalCharacterCount = srtContent.Length;

            await App.User.RefreshProfileAsync();
            long remainingChars = App.User.TtsCharacterLimit - App.User.TtsCharactersUsed;
            if (totalCharacterCount > remainingChars)
            {
                CustomMessageBox.Show(
                    $"Tổng số ký tự của các dòng đã chọn ({totalCharacterCount:N0}) vượt quá giới hạn còn lại ({remainingChars:N0}).",
                    "Không đủ ký tự",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            var generateButton = GenerateVoiceSubButton;
            generateButton.IsEnabled = false;
            generateButton.Content = "Bắt đầu.";

            try
            {
                var (startSuccess, jobId, startError) =
                    await ApiService.StartAioTtsBatchJobAsync(srtContent, language, voiceId, rate);
                if (!startSuccess) throw new Exception($"Không thể bắt đầu tác vụ: {startError}");
                generateButton.Content = "Đang xử lý.";
                AioTtsBatchJobStatus currentStatus;
                while (true)
                {
                    await Task.Delay(3000);
                    var (statusSuccess, status, statusError) = await ApiService.GetAioTtsBatchJobStatusAsync(jobId);
                    if (!statusSuccess) throw new Exception($"Mất kết nối khi kiểm tra trạng thái: {statusError}");
                    currentStatus = status;
                    generateButton.Content = $"Đang xử lý. ({currentStatus.ProcessedLines}/{currentStatus.TotalLines})";
                    if (currentStatus.Status == "Completed" || currentStatus.Status == "Failed") break;
                }
                if (currentStatus.Status == "Failed")
                    throw new Exception($"Tác vụ thất bại trên server: {currentStatus.ErrorMessage}");
                generateButton.Content = "Đang tải kết quả.";
                var (downloadSuccess, zipData, downloadError) = await ApiService.DownloadAioTtsBatchResultAsync(jobId);
                if (!downloadSuccess) throw new Exception($"Không thể tải kết quả: {downloadError}");
                generateButton.Content = "Đang xử lý.";
                string projectsRootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects");
                string ttsProjectFolder = Path.Combine(projectsRootFolder, "TTS", _currentProject.ProjectName, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(ttsProjectFolder);
                using (var stream = new MemoryStream(zipData))
                using (var archive = new ZipArchive(stream))
                {
                    archive.ExtractToDirectory(ttsProjectFolder, true);
                }
                var audioFiles = Directory
    .GetFiles(ttsProjectFolder, "*.*", SearchOption.AllDirectories)
    .Where(f =>
        f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
    .ToList();

                if (!audioFiles.Any())
                    throw new Exception("Không tìm thấy file âm thanh nào trong gói trả về.");

                var linesByIndex = linesToVoice.ToDictionary(l => l.Index);
                var audioPathByIndex = new Dictionary<int, string>();
                const double timeToleranceMs = 300.0;

                var newClipViewModels = new List<TimelineClipViewModel>();
                int matchedCount = 0;

                foreach (var audioFile in audioFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(audioFile);
                    var parts = fileName.Split('_');
                    if (parts.Length < 3) continue;

                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                        continue;

                    SrtSubtitleLine lineToUpdate = null;
                    if (!linesByIndex.TryGetValue(idx, out lineToUpdate))
                    {
                        if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long startMs) &&
                            long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long endMs))
                        {
                            var startT = TimeSpan.FromMilliseconds(startMs);
                            var endT = TimeSpan.FromMilliseconds(endMs);

                            lineToUpdate = linesToVoice.FirstOrDefault(l =>
                                Math.Abs((l.StartTime - startT).TotalMilliseconds) <= timeToleranceMs &&
                                Math.Abs((l.EndTime - endT).TotalMilliseconds) <= timeToleranceMs);
                        }
                    }

                    if (lineToUpdate == null) continue;

                    var mediaInfo = await FFProbe.AnalyseAsync(audioFile);
                    if (mediaInfo?.PrimaryAudioStream == null && mediaInfo?.Duration == TimeSpan.Zero) continue;

                    var newAudioClip = new TimelineAudioClip
                    {
                        FilePath = audioFile,
                        StartTime = lineToUpdate.StartTime,
                        OriginalDuration = mediaInfo.Duration,
                        TrimStartOffset = TimeSpan.Zero,
                        TrimEndOffset = TimeSpan.Zero,
                        SourceSubtitleIndexSnapshot = lineToUpdate.Index,
                        CaptionTextSnapshot = lineToUpdate.OriginalText
                    };

                    _currentProject.VoicedSubtitles.Add(newAudioClip);
                    QueueWaveformGeneration(newAudioClip);

                    var newClipVM = new TimelineClipViewModel(newAudioClip);
                    TimelineClips.Add(newClipVM);
                    newClipViewModels.Add(newClipVM);

                    lineToUpdate.VoicedAudioPath = audioFile;
                    lineToUpdate.IsVoiced = true;
                    audioPathByIndex[lineToUpdate.Index] = audioFile;
                    matchedCount++;
                }
                await WriteTtsSmartCutManifestAsync(ttsProjectFolder, linesToVoice, audioPathByIndex, _currentSrtFilePath);
                if (newClipViewModels.Any())
                {
                    AssignAudioClipsToIntelligentTracks(newClipViewModels);
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                    _undoRedoService.AddState(CaptureEditorSnapshot());
                    SaveProjectCurrent();
                }
                await App.User.RefreshProfileAsync();
                TtsInputTextBox_TextChanged(null, null);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi trong quá trình tạo Voice Sub:\n\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                generateButton.IsEnabled = true;
                generateButton.Content = "Tạo Voice Sub";
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task GenerateVoiceForSrtLines(List<SrtSubtitleLine> linesToVoice)
        {
            if (linesToVoice == null || !linesToVoice.Any())
            {
                CustomMessageBox.Show("Không có dòng phụ đề nào hợp lệ để tạo giọng nói.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string sourceTextChoice = "Original";
            bool hasTranslatedText = linesToVoice.Any(l => !string.IsNullOrWhiteSpace(l.TranslatedText));
            if (hasTranslatedText)
            {
                var result = CustomMessageBox.Show(
                    "Bạn muốn tạo giọng nói từ 'Văn bản gốc' hay 'Bản dịch'?\n\n- Chọn 'Yes' để dùng VĂN BẢN GỐC.\n- Chọn 'No' để dùng BẢN DỊCH.",
                    "Chọn nguồn văn bản",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.No) sourceTextChoice = "Translated";
            }

            if (AioLanguageComboBox.SelectedValue == null || AioVoiceComboBox.SelectedValue == null)
            {
                CustomMessageBox.Show("Vui lòng chọn đầy đủ Ngôn ngữ và Giọng nói trong tab Text to Speech.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string language = AioLanguageComboBox.SelectedValue.ToString();
            string voiceId = AioVoiceComboBox.SelectedValue.ToString();
            double rate = AioRateSlider.Value;
            string srtContent = SrtFileUtils.GenerateSrtContent(linesToVoice, sourceTextChoice == "Translated");
            long totalCharacterCount = srtContent.Length;
            await App.User.RefreshProfileAsync();
            long remainingChars = App.User.TtsCharacterLimit - App.User.TtsCharactersUsed;
            if (totalCharacterCount > remainingChars)
            {
                CustomMessageBox.Show(
                    $"Tổng số ký tự của các dòng đã chọn ({totalCharacterCount:N0}) vượt quá giới hạn còn lại ({remainingChars:N0}).",
                    "Không đủ ký tự",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            var generateButton = TtsModeVoiceSubRadio.IsChecked == true ? GenerateVoiceSubButton : GenerateTtsButton;
            generateButton.IsEnabled = false;
            generateButton.Content = "Bắt đầu.";

            try
            {
                var (startSuccess, jobId, startError) =
                    await ApiService.StartAioTtsBatchJobAsync(srtContent, language, voiceId, rate);
                if (!startSuccess) throw new Exception($"Không thể bắt đầu tác vụ: {startError}");
                generateButton.Content = "Đang xử lý.";
                AioTtsBatchJobStatus currentStatus;
                while (true)
                {
                    await Task.Delay(3000);
                    var (statusSuccess, status, statusError) = await ApiService.GetAioTtsBatchJobStatusAsync(jobId);
                    if (!statusSuccess) throw new Exception($"Mất kết nối khi kiểm tra trạng thái: {statusError}");
                    currentStatus = status;
                    generateButton.Content = $"Đang xử lý. ({currentStatus.ProcessedLines}/{currentStatus.TotalLines})";
                    if (currentStatus.Status == "Completed" || currentStatus.Status == "Failed") break;
                }
                if (currentStatus.Status == "Failed")
                    throw new Exception($"Tác vụ thất bại trên server: {currentStatus.ErrorMessage}");
                generateButton.Content = "Đang tải kết quả.";
                var (downloadSuccess, zipData, downloadError) = await ApiService.DownloadAioTtsBatchResultAsync(jobId);
                if (!downloadSuccess) throw new Exception($"Không thể tải kết quả: {downloadError}");
                generateButton.Content = "Đang xử lý.";
                string projectsRootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects");
                string ttsProjectFolder = Path.Combine(projectsRootFolder, "TTS", _currentProject.ProjectName, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(ttsProjectFolder);
                using (var stream = new MemoryStream(zipData))
                using (var archive = new ZipArchive(stream))
                {
                    archive.ExtractToDirectory(ttsProjectFolder, true);
                }
                var audioFiles = Directory
    .GetFiles(ttsProjectFolder, "*.*", SearchOption.AllDirectories)
    .Where(f =>
        f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
    .ToList();

                if (!audioFiles.Any())
                    throw new Exception("Không tìm thấy file âm thanh nào trong gói trả về.");

                var linesByIndex = linesToVoice.ToDictionary(l => l.Index);
                var audioPathByIndex = new Dictionary<int, string>();
                const double timeToleranceMs = 300.0;

                var newClipViewModels = new List<TimelineClipViewModel>();
                int matchedCount = 0;

                foreach (var audioFile in audioFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(audioFile);
                    var parts = fileName.Split('_');
                    if (parts.Length < 3) continue;

                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                        continue;

                    SrtSubtitleLine lineToUpdate = null;
                    if (!linesByIndex.TryGetValue(idx, out lineToUpdate))
                    {
                        if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long startMs) &&
                            long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long endMs))
                        {
                            var startT = TimeSpan.FromMilliseconds(startMs);
                            var endT = TimeSpan.FromMilliseconds(endMs);

                            lineToUpdate = linesToVoice.FirstOrDefault(l =>
                                Math.Abs((l.StartTime - startT).TotalMilliseconds) <= timeToleranceMs &&
                                Math.Abs((l.EndTime - endT).TotalMilliseconds) <= timeToleranceMs);
                        }
                    }

                    if (lineToUpdate == null) continue;

                    var mediaInfo = await FFProbe.AnalyseAsync(audioFile);
                    if (mediaInfo?.PrimaryAudioStream == null && mediaInfo?.Duration == TimeSpan.Zero) continue;

                    var newAudioClip = new TimelineAudioClip
                    {
                        FilePath = audioFile,
                        StartTime = lineToUpdate.StartTime,
                        OriginalDuration = mediaInfo.Duration,
                        TrimStartOffset = TimeSpan.Zero,
                        TrimEndOffset = TimeSpan.Zero,
                        SourceSubtitleIndexSnapshot = lineToUpdate.Index,
                        CaptionTextSnapshot = lineToUpdate.OriginalText
                    };
                    _currentProject.VoicedSubtitles.Add(newAudioClip);
                    QueueWaveformGeneration(newAudioClip);
                    var newClipVM = new TimelineClipViewModel(newAudioClip);
                    TimelineClips.Add(newClipVM);
                    newClipViewModels.Add(newClipVM);
                    lineToUpdate.VoicedAudioPath = audioFile;
                    lineToUpdate.IsVoiced = true;
                    audioPathByIndex[lineToUpdate.Index] = audioFile;
                    matchedCount++;
                }
                await WriteTtsSmartCutManifestAsync(ttsProjectFolder, linesToVoice, audioPathByIndex, _currentSrtFilePath);
                if (newClipViewModels.Any())
                {
                    AssignAudioClipsToIntelligentTracks(newClipViewModels);
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                    _undoRedoService.AddState(CaptureEditorSnapshot());
                    SaveProjectCurrent();
                }

                await App.User.RefreshProfileAsync();
                TtsInputTextBox_TextChanged(null, null);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi trong quá trình tạo Voice Sub:\n\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                generateButton.IsEnabled = true;
                generateButton.Content = "Tạo Voice Sub";
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void PlayVoicedLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SrtSubtitleLine line)
            {
                if (!string.IsNullOrEmpty(line.VoicedAudioPath) && File.Exists(line.VoicedAudioPath))
                {
                    _currentTtsAudioPath = line.VoicedAudioPath;
                    _ttsAudioPlayer.Play();
                    _isTtsPlaying = true;
                    TtsPlayPauseButton.Content = "\uE769";
                    TtsTab.IsChecked = true;
                }
            }
        }

        private void TtsAudioPlayer_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            if (_ttsAudioPlayer.NaturalDuration.HasValue)
            {
                TtsProgressSlider.Maximum = _ttsAudioPlayer.NaturalDuration.Value.TotalSeconds;
                _isTtsPlaying = true;
                TtsPlayPauseButton.Content = "";
                TtsPlaybackControlsPanel.Visibility = Visibility.Visible;
                TtsPlayPauseButton.IsEnabled = true;
                TtsProgressSlider.IsEnabled = true;
                _ttsProgressTimer.Start();
            }
        }
        private void VoiceItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TtsVoice voice)
            {
                audioPreviewPlayer.Stop();

                string displayName = voice.DisplayName;
                string baseName = displayName.Contains("(") ? displayName.Substring(0, displayName.IndexOf("(")).Trim() : displayName.Trim();

                try
                {
                    string voiceFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "voices");
                    string filePath = Path.Combine(voiceFolderPath, $"{baseName}.mp3");

                    if (File.Exists(filePath))
                    {
                        audioPreviewPlayer.Source = new Uri(filePath);
                        audioPreviewPlayer.Play();
                    }
                    else { }
                }
                catch (Exception ex) { }
            }
        }

        private void VoiceItem_MouseLeave(object sender, MouseEventArgs e)
        {
            audioPreviewPlayer.Stop();
        }

        private void AioVoiceComboBox_DropDownClosed(object sender, EventArgs e)
        {
            audioPreviewPlayer.Stop();
        }
        private async void TtsAudioPlayer_MediaEnded(object sender, EventArgs e)
        {
            _isTtsPlaying = false;
            _ttsProgressTimer.Stop();
            await _ttsAudioPlayer.Stop();
            TtsProgressSlider.Value = 0;
            TtsPlayPauseButton.Content = "";
            if (_ttsAudioPlayer.NaturalDuration.HasValue)
            {
                TtsTimeDisplay.Text = $"{TimeSpan.Zero:mm\\:ss} / {_ttsAudioPlayer.NaturalDuration.Value:mm\\:ss}";
            }
        }
        private void TtsPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTtsPlaying)
            {
                _ttsAudioPlayer.Pause();
                _ttsProgressTimer.Stop();
                TtsPlayPauseButton.Content = "\uE768";
            }
            else
            {
                _ttsAudioPlayer.Play();
                _ttsProgressTimer.Start();
                TtsPlayPauseButton.Content = "\uE769";
            }
            _isTtsPlaying = !_isTtsPlaying;
        }

        private void TtsProgressTimer_Tick(object sender, EventArgs e)
        {
            if (!_isTtsSliderDragging && _ttsAudioPlayer.NaturalDuration.HasValue)
            {
                TtsProgressSlider.Value = _ttsAudioPlayer.Position.TotalSeconds;
                TtsTimeDisplay.Text = $"{_ttsAudioPlayer.Position:mm\\:ss} / {_ttsAudioPlayer.NaturalDuration.Value:mm\\:ss}";
            }
        }

        private void FontFamilyComboBox_DropDownOpened(object sender, EventArgs e)
        {
            EnterAuxUi();
            _originalFontFamilyBeforePreview = FontFamilyComboBox.SelectedItem as FontFamily;
        }

        private void FontFamilyTextBlock_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is FontFamily hoveredFont)
            {
                if (_selectedSubtitle != null && _originalFontFamilyBeforePreview != null)
                {
                    if (_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual))
                    {
                        var previewStyle = _selectedSubtitle.Style.Clone();
                        previewStyle.FontFamilyName = hoveredFont.Source;

                        ApplyStyleToVisual(currentVisual, previewStyle);
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            else
            {
            }
        }
        private sealed class VideoSegmentInfo
        {
            public int SegmentIndex { get; init; }

            // Thời gian trên video gốc (source)
            public TimeSpan SourceStart { get; init; }
            public TimeSpan SourceEnd { get; init; }
            public TimeSpan SourceDuration => SourceEnd - SourceStart;

            // Thời gian mục tiêu (sau khi slow/fast)
            public TimeSpan TargetDuration { get; init; }

            public double ActualOutputDuration { get; set; } = 0; // Duration thực tế sau khi render
            public double SourceStartExact { get; set; } = 0;     // SourceStart dạng seconds chính xác
            public double SourceEndExact { get; set; } = 0;       // SourceEnd dạng seconds chính xác
            public double TargetDurationExact { get; set; } = 0;
            // < 1.0 = slow down, = 1.0 = giữ nguyên, > 1.0 = speed up
            public double RequiredSpeed { get; init; }

            // Voice TTS clip tương ứng (có thể null nếu là gap)
            public TimelineAudioClip VoiceClip { get; init; }

            // Subtitle line tương ứng (có thể null nếu là gap)
            public SrtSubtitleLine SubtitleLine { get; init; }

            // Đây có phải là khoảng im lặng (gap) giữa các subtitle không?
            public bool IsSilenceGap { get; init; }

            // Thời gian trong output timeline final
            public TimeSpan OutputStart { get; set; }
            public TimeSpan OutputEnd { get; set; }

            // Path của file segment output
            public string OutputPath { get; set; }

            // Có lỗi khi xử lý không?
            public bool HasError { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Quản lý hàng đợi xử lý song song
        /// </summary>
        private sealed class ParallelProcessingQueue
        {
            public ConcurrentQueue<VideoSegmentInfo> PendingSegments { get; init; }
            public ConcurrentBag<VideoSegmentInfo> CompletedSegments { get; init; }
            public ConcurrentBag<VideoSegmentInfo> FailedSegments { get; init; }
            public int TotalSegments { get; init; }
            public int MaxParallelProcesses { get; init; }

            // Thread-safe counter
            private int _processedCount = 0;
            public int ProcessedCount => _processedCount;

            public void IncrementProcessed()
            {
                Interlocked.Increment(ref _processedCount);
            }
        }

        // ============================================================================
        // BƯỚC 3: CÁC HÀM UTILITY
        // ============================================================================

        /// <summary>
        /// Tính speed cần thiết để video segment khớp với voice duration
        /// </summary>
        private double CalculateRequiredSpeed(TimeSpan videoSegmentDuration, TimeSpan voiceDuration)
        {
            if (voiceDuration <= TimeSpan.Zero)
                return 1.0;

            if (videoSegmentDuration <= TimeSpan.Zero)
                return 1.0;

            double speed = voiceDuration.TotalSeconds / videoSegmentDuration.TotalSeconds;

            // Chỉ slow down, không speed up
            // Nếu video dài hơn voice, giữ nguyên (speed = 1.0)
            if (speed > 1.0)
                speed = 1.0;

            // Clamp trong khoảng hợp lệ của ffmpeg
            speed = Math.Clamp(speed, 0.25, 1.0);

            return speed;
        }

        /// <summary>
        /// Tính target duration sau khi áp dụng speed
        /// </summary>
        private TimeSpan CalculateTargetDuration(TimeSpan sourceDuration, double speed)
        {
            if (speed <= 0.0001)
                return sourceDuration;

            double targetSeconds = sourceDuration.TotalSeconds / speed;
            return TimeSpan.FromSeconds(targetSeconds);
        }
        private async Task<List<VideoSegmentInfo>> BuildSegmentListForParallelProcessing(
    MediaAsset mainVideoAsset,
    List<SmartCutMapItem> smartCutMapItems,
    CancellationToken cancellationToken)
        {
            var segments = new List<VideoSegmentInfo>();

            if (smartCutMapItems == null || smartCutMapItems.Count == 0)
            {
                throw new Exception("Không có dữ liệu SmartCut để xử lý.");
            }

            // Lấy thông tin video để tính GOP size
            double videoFps = 30.0; // default
            try
            {
                var videoInfo = await FFProbe.AnalyseAsync(mainVideoAsset.FilePath);
                if (videoInfo?.PrimaryVideoStream != null)
                {
                    videoFps = videoInfo.PrimaryVideoStream.FrameRate;
                    Debug.WriteLine($"[BUILD-SEGMENTS] Detected FPS: {videoFps}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BUILD-SEGMENTS] Cannot detect FPS: {ex.Message}, using default 30fps");
            }

            // GOP size thường là 2 seconds (60 frames @ 30fps, 120 frames @ 60fps)
            double gopDuration = 2.0; // seconds
            double frameTime = 1.0 / videoFps; // thời gian 1 frame

            var (ttsClips, _) = ClassifyAudioClipsForExport();
            var ttsPlacement = ResolveTtsPlacementsForDynamic(
                ttsClips,
                smartCutMapItems.Select(it => (it.Line, it.FinalStart, it.FinalEnd)).ToList()
            );

            int segmentIndex = 1;
            TimeSpan outputCursor = TimeSpan.Zero;
            TimeSpan previousSourceEnd = TimeSpan.Zero;

            foreach (var mapItem in smartCutMapItems.OrderBy(m => m.FinalStart))
            {
                var line = mapItem.Line;

                // Source time - sử dụng double để chính xác hơn
                double sourceStartSec = line.StartTime.TotalSeconds;
                double sourceEndSec = line.EndTime.TotalSeconds;

                // QUAN TRỌNG: Làm tròn thời gian cắt về keyframe gần nhất
                // Điều này đảm bảo cắt chính xác và không bị frame offset
                sourceStartSec = AlignToKeyframe(sourceStartSec, gopDuration, frameTime, alignToStart: true);
                sourceEndSec = AlignToKeyframe(sourceEndSec, gopDuration, frameTime, alignToStart: false);

                TimeSpan alignedSourceStart = TimeSpan.FromSeconds(sourceStartSec);
                TimeSpan alignedSourceEnd = TimeSpan.FromSeconds(sourceEndSec);

                // Tạo gap segment nếu có khoảng trống
                if (alignedSourceStart > previousSourceEnd)
                {
                    TimeSpan gapSourceStart = previousSourceEnd;
                    TimeSpan gapSourceEnd = alignedSourceStart;
                    TimeSpan gapDuration = gapSourceEnd - gapSourceStart;

                    if (gapDuration > TimeSpan.FromMilliseconds(50))
                    {
                        double gapStartSec = gapSourceStart.TotalSeconds;
                        double gapEndSec = gapSourceEnd.TotalSeconds;

                        // Align gap boundaries
                        gapStartSec = AlignToKeyframe(gapStartSec, gopDuration, frameTime, alignToStart: true);
                        gapEndSec = AlignToKeyframe(gapEndSec, gopDuration, frameTime, alignToStart: false);

                        double gapDurSec = gapEndSec - gapStartSec;

                        if (gapDurSec > 0.05) // > 50ms
                        {
                            var gapSegment = new VideoSegmentInfo
                            {
                                SegmentIndex = segmentIndex++,
                                SourceStart = TimeSpan.FromSeconds(gapStartSec),
                                SourceEnd = TimeSpan.FromSeconds(gapEndSec),
                                SourceStartExact = gapStartSec,
                                SourceEndExact = gapEndSec,
                                TargetDuration = TimeSpan.FromSeconds(gapDurSec),
                                TargetDurationExact = gapDurSec,
                                RequiredSpeed = 1.0,
                                IsSilenceGap = true,
                                SubtitleLine = null,
                                VoiceClip = null,
                                OutputStart = outputCursor,
                                OutputEnd = outputCursor + TimeSpan.FromSeconds(gapDurSec)
                            };

                            segments.Add(gapSegment);
                            outputCursor += TimeSpan.FromSeconds(gapDurSec);
                        }
                    }
                }

                // Tìm voice clip
                TimelineAudioClip voiceClip = null;
                double voiceDurationSec = 0;

                if (!string.IsNullOrWhiteSpace(line.VoicedAudioPath) && File.Exists(line.VoicedAudioPath))
                {
                    voiceClip = ttsClips.FirstOrDefault(c =>
                        string.Equals(c.FilePath, line.VoicedAudioPath, StringComparison.OrdinalIgnoreCase));

                    if (voiceClip != null)
                    {
                        try
                        {
                            var info = await FFProbe.AnalyseAsync(voiceClip.FilePath);
                            voiceDurationSec = info?.Duration.TotalSeconds ?? 0;
                        }
                        catch
                        {
                            voiceDurationSec = voiceClip.EffectiveDuration.TotalSeconds;
                        }

                        // Apply speed to voice duration if needed
                        if (Math.Abs(voiceClip.Speed - 1.0) > 0.01)
                        {
                            voiceDurationSec = voiceDurationSec / voiceClip.Speed;
                        }
                    }
                }

                // Tính speed và target duration
                double sourceSegmentDurSec = sourceEndSec - sourceStartSec;
                double requiredSpeed = 1.0;
                double targetDurSec = sourceSegmentDurSec;

                if (voiceDurationSec > 0.01)
                {
                    targetDurSec = voiceDurationSec;
                    requiredSpeed = sourceSegmentDurSec / voiceDurationSec;

                    // Clamp speed để tránh giá trị quá lớn/nhỏ
                    requiredSpeed = Math.Clamp(requiredSpeed, 0.25, 4.0);

                    // Recalculate target duration với speed đã clamp
                    targetDurSec = sourceSegmentDurSec / requiredSpeed;
                }

                // Làm tròn target duration về frame boundary để chính xác
                int targetFrames = (int)Math.Round(targetDurSec * videoFps);
                targetDurSec = targetFrames / videoFps;

                var subtitleSegment = new VideoSegmentInfo
                {
                    SegmentIndex = segmentIndex++,
                    SourceStart = TimeSpan.FromSeconds(sourceStartSec),
                    SourceEnd = TimeSpan.FromSeconds(sourceEndSec),
                    SourceStartExact = sourceStartSec,
                    SourceEndExact = sourceEndSec,
                    TargetDuration = TimeSpan.FromSeconds(targetDurSec),
                    TargetDurationExact = targetDurSec,
                    RequiredSpeed = requiredSpeed,
                    IsSilenceGap = false,
                    SubtitleLine = line,
                    VoiceClip = voiceClip,
                    OutputStart = outputCursor,
                    OutputEnd = outputCursor + TimeSpan.FromSeconds(targetDurSec)
                };

                segments.Add(subtitleSegment);
                outputCursor += TimeSpan.FromSeconds(targetDurSec);
                previousSourceEnd = TimeSpan.FromSeconds(sourceEndSec);
            }

            // Tạo gap cuối nếu cần
            if (mainVideoAsset != null && previousSourceEnd < mainVideoAsset.Duration)
            {
                double finalGapStartSec = previousSourceEnd.TotalSeconds;
                double finalGapEndSec = mainVideoAsset.Duration.TotalSeconds;

                // Align final gap
                finalGapStartSec = AlignToKeyframe(finalGapStartSec, gopDuration, frameTime, alignToStart: true);
                finalGapEndSec = AlignToKeyframe(finalGapEndSec, gopDuration, frameTime, alignToStart: false);

                double finalGapDurSec = finalGapEndSec - finalGapStartSec;

                if (finalGapDurSec > 0.05)
                {
                    var finalGapSegment = new VideoSegmentInfo
                    {
                        SegmentIndex = segmentIndex++,
                        SourceStart = TimeSpan.FromSeconds(finalGapStartSec),
                        SourceEnd = TimeSpan.FromSeconds(finalGapEndSec),
                        SourceStartExact = finalGapStartSec,
                        SourceEndExact = finalGapEndSec,
                        TargetDuration = TimeSpan.FromSeconds(finalGapDurSec),
                        TargetDurationExact = finalGapDurSec,
                        RequiredSpeed = 1.0,
                        IsSilenceGap = true,
                        SubtitleLine = null,
                        VoiceClip = null,
                        OutputStart = outputCursor,
                        OutputEnd = outputCursor + TimeSpan.FromSeconds(finalGapDurSec)
                    };

                    segments.Add(finalGapSegment);
                }
            }

            return segments;
        }
        private double MapSourceTimeToOutputTimelineSeconds(double sourceTimeSec, List<VideoSegmentInfo> segments)
        {
            foreach (var segment in segments.OrderBy(s => s.SourceStart))
            {
                // Sử dụng SourceStartExact và SourceEndExact để so sánh chính xác
                if (sourceTimeSec >= segment.SourceStartExact && sourceTimeSec < segment.SourceEndExact)
                {
                    double progressInSegment = sourceTimeSec - segment.SourceStartExact;
                    double sourceDur = segment.SourceEndExact - segment.SourceStartExact;
                    double segmentRatio = sourceDur > 0 ? progressInSegment / sourceDur : 0;

                    double offsetInOutput = segmentRatio * segment.TargetDurationExact;
                    return segment.OutputStart.TotalSeconds + offsetInOutput;
                }
            }
            // Fallback: nếu thời gian nằm ngoài tất cả các segment, trả về giá trị gốc (ít khi xảy ra)
            return sourceTimeSec;
        }
        private double AlignToKeyframe(double timeSec, double gopDuration, double frameTime, bool alignToStart)
        {
            // FIX CHÍNH: Làm tròn về frame boundary TRƯỚC, sau đó mới xét GOP
            // Điều này đảm bảo timing luôn chính xác theo frame

            if (alignToStart)
            {
                // Làm tròn lên về frame boundary gần nhất
                int frameIndex = (int)Math.Ceiling(timeSec / frameTime);
                double alignedTime = frameIndex * frameTime;

                // Nếu aligned time nằm trên frame đầu tiên của GOP, ưu tiên GOP boundary
                int gopIndex = (int)(alignedTime / gopDuration);
                double gopStart = gopIndex * gopDuration;

                // Nếu gần GOP boundary (< 1 frame), snap về GOP boundary
                if (Math.Abs(alignedTime - gopStart) < frameTime)
                {
                    return gopStart;
                }

                return alignedTime;
            }
            else
            {
                // Làm tròn xuống về frame boundary gần nhất
                int frameIndex = (int)Math.Floor(timeSec / frameTime);
                double alignedTime = frameIndex * frameTime;

                // Nếu aligned time nằm trên frame cuối của GOP, ưu tiên GOP boundary
                int gopIndex = (int)Math.Ceiling(alignedTime / gopDuration);
                double gopEnd = gopIndex * gopDuration;

                // Nếu gần GOP boundary (< 1 frame), snap về GOP boundary
                if (Math.Abs(gopEnd - alignedTime) < frameTime)
                {
                    return gopEnd;
                }

                return alignedTime;
            }
        }
        private async Task<bool> ProcessSingleSegmentAsync(
            VideoSegmentInfo segment,
            MediaAsset mainVideoAsset,
            string ffmpegPath,
            VideoExportSettings settings,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                segment.OutputPath = TempFileManager.CreateTempFile(".mp4");
                TempFileManager.RegisterForCleanup(segment.OutputPath);

                var args = new StringBuilder();
                args.Append("-y -hide_banner -loglevel error ");

                // GPU acceleration
                if (!string.IsNullOrEmpty(settings.GpuAcceleration) &&
                    settings.GpuAcceleration != "none" &&
                    settings.GpuAcceleration != "auto")
                {
                    args.Append($"-hwaccel {settings.GpuAcceleration} ");
                }

                // FIX: Thêm -copyts để giữ timestamps chính xác khi trim
                args.Append("-copyts ");

                // Input video
                args.Append($"-i \"{mainVideoAsset.FilePath}\" ");

                // Input voice audio if exists
                bool hasVoice = segment.VoiceClip != null &&
                                !string.IsNullOrWhiteSpace(segment.VoiceClip.FilePath) &&
                                File.Exists(segment.VoiceClip.FilePath);

                if (hasVoice)
                {
                    args.Append($"-i \"{segment.VoiceClip.FilePath}\" ");
                }

                // Build filter complex
                var filterParts = new List<string>();
                string videoTag = "0:v";
                string audioTag = "0:a";

                // Sử dụng exact times với độ chính xác cao
                string startTimeStr = segment.SourceStartExact.ToString("0.#########", CultureInfo.InvariantCulture);
                string endTimeStr = segment.SourceEndExact.ToString("0.#########", CultureInfo.InvariantCulture);
                double sourceDurSec = segment.SourceEndExact - segment.SourceStartExact;

                // FIX CHÍNH #1: Chỉ trim 1 LẦN DUY NHẤT với start/end
                // Không dùng fps filter, không trim lại bằng duration
                // setpts=PTS-STARTPTS reset PTS về 0 ngay sau trim
                filterParts.Add($"[{videoTag}]trim=start={startTimeStr}:end={endTimeStr},setpts=PTS-STARTPTS[v_trimmed]");
                videoTag = "v_trimmed";

                // Audio trim tương tự
                filterParts.Add($"[{audioTag}]atrim=start={startTimeStr}:end={endTimeStr},asetpts=PTS-STARTPTS[a_trimmed]");
                audioTag = "a_trimmed";

                // FIX CHÍNH #2: Apply speed ĐÚNG CÁCH
                // Nếu cần speed, áp dụng TRƯỚC khi scale
                if (Math.Abs(segment.RequiredSpeed - 1.0) > 0.001)
                {
                    // Video speed: setpts với PTS multiplier
                    // speed > 1.0 (nhanh hơn): PTS multiplier < 1.0
                    // speed < 1.0 (chậm hơn): PTS multiplier > 1.0
                    double ptsMultiplier = 1.0 / segment.RequiredSpeed;
                    string ptsMultStr = ptsMultiplier.ToString("0.##########", CultureInfo.InvariantCulture);

                    filterParts.Add($"[{videoTag}]setpts={ptsMultStr}*PTS[v_speed]");
                    videoTag = "v_speed";

                    // Audio speed với atempo
                    string atempoChain = BuildAtempoChain(segment.RequiredSpeed);
                    if (!string.IsNullOrEmpty(atempoChain))
                    {
                        filterParts.Add($"[{audioTag}]{atempoChain}[a_speed]");
                        audioTag = "a_speed";
                    }
                }

                // Scale và pad để match output resolution
                filterParts.Add($"[{videoTag}]scale={settings.ResolutionWidth}:{settings.ResolutionHeight}:" +
                               $"force_original_aspect_ratio=decrease,pad={settings.ResolutionWidth}:{settings.ResolutionHeight}:" +
                               $"(ow-iw)/2:(oh-ih)/2:black[v_scaled]");
                videoTag = "v_scaled";

                // Blur nếu cần
                if (!segment.IsSilenceGap && _currentProject.BlurMode == BlurApplyMode.PerSubtitle)
                {
                    string blurFilter = BuildBlurFilterForSegment(videoTag, "v_blur", segment);
                    if (!string.IsNullOrEmpty(blurFilter))
                    {
                        filterParts.Add(blurFilter);
                        videoTag = "v_blur";
                    }
                }

                // FIX CHÍNH #3: KHÔNG DÙNG fps filter
                // fps filter gây ra PTS không liên tục và frame offset
                // Video đã có framerate ổn định từ source, không cần force lại

                // Output video
                filterParts.Add($"[{videoTag}]null[vout]");

                // Audio processing
                if (hasVoice)
                {
                    string videoVolumeGain = DbToGain(mainVideoAsset.VolumeDb).ToString(CultureInfo.InvariantCulture);
                    string voiceVolumeGain = DbToGain(segment.VoiceClip.VolumeDb).ToString(CultureInfo.InvariantCulture);

                    filterParts.Add($"[{audioTag}]volume={videoVolumeGain},aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a_vid]");
                    filterParts.Add($"[1:a]volume={voiceVolumeGain},aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a_voice]");
                    filterParts.Add("[a_vid][a_voice]amix=inputs=2:duration=longest:normalize=0:dropout_transition=0,alimiter=limit=0.95[aout]");
                }
                else
                {
                    string videoVolumeGain = DbToGain(mainVideoAsset.VolumeDb).ToString(CultureInfo.InvariantCulture);
                    filterParts.Add($"[{audioTag}]volume={videoVolumeGain},aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,alimiter=limit=0.95[aout]");
                }

                // Write filter script
                string filterContent = string.Join(";", filterParts);
                string filterScriptPath = TempFileManager.CreateTempFile(".txt");
                await WriteTextNoBomAsync(filterScriptPath, filterContent, cancellationToken);
                TempFileManager.RegisterForCleanup(filterScriptPath);

                args.Append($"-filter_complex_script \"{filterScriptPath}\" ");
                args.Append("-filter_threads 0 ");
                args.Append("-map \"[vout]\" -map \"[aout]\" ");

                // FIX CHÍNH #4: GOP structure và keyframe settings
                // Force keyframe mỗi 2 giây để đảm bảo segments có cấu trúc GOP đồng nhất
                args.Append("-g 60 -keyint_min 60 ");      // GOP size = 60 frames @ 30fps = 2s
                args.Append("-sc_threshold 0 ");            // Disable scene change detection
                args.Append("-force_key_frames expr:gte(t,n_forced*2) "); // Keyframe mỗi 2s

                // FIX CHÍNH #5: Thêm -vsync cfr để constant framerate
                // Điều này đảm bảo mọi segment có framerate đồng nhất khi concat
                args.Append("-vsync cfr ");
                args.Append("-r 30 ");  // Explicit framerate

                // Encoding settings
                args.Append($"-c:v {settings.VideoCodec} ");

                if (settings.VideoCodec.Contains("264") || settings.VideoCodec.Contains("265"))
                {
                    args.Append($"-preset {settings.Preset} ");
                    args.Append($"-crf {settings.Crf} ");
                }
                else if (settings.VideoCodec.Contains("nvenc"))
                {
                    args.Append("-preset p4 -rc vbr -cq 23 ");
                }

                args.Append("-c:a aac -b:a 192k -ar 48000 ");

                // FIX CHÍNH #6: Timestamps flags
                args.Append("-avoid_negative_ts make_zero ");

                // FIX: KHÔNG dùng -start_at_zero vì đã dùng setpts
                // args.Append("-start_at_zero ");  

                args.Append("-max_muxing_queue_size 9999 ");

                args.Append($"\"{segment.OutputPath}\"");

                Debug.WriteLine($"[SEGMENT-PROCESS] #{segment.SegmentIndex}: {args}");
                progress?.Report($"Segment #{segment.SegmentIndex}: Đang xử lý...");

                int exitCode = await RunFfmpegAsync(ffmpegPath, args.ToString(), cancellationToken);

                if (exitCode != 0)
                {
                    segment.HasError = true;
                    segment.ErrorMessage = $"FFmpeg trả về mã lỗi {exitCode}";
                    return false;
                }

                if (!File.Exists(segment.OutputPath))
                {
                    segment.HasError = true;
                    segment.ErrorMessage = "File output không được tạo";
                    return false;
                }

                // Verify output duration
                try
                {
                    var mediaInfo = await FFProbe.AnalyseAsync(segment.OutputPath);
                    segment.ActualOutputDuration = mediaInfo.Duration.TotalSeconds;
                    double diff = segment.ActualOutputDuration - segment.TargetDurationExact;

                    if (Math.Abs(diff) > 0.05) // > 50ms - cho phép tolerance lớn hơn
                    {
                        Debug.WriteLine($"[SEGMENT-DURATION-WARNING] Seg #{segment.SegmentIndex}: " +
                                        $"Target={segment.TargetDurationExact:F6}s, " +
                                        $"Actual={segment.ActualOutputDuration:F6}s, " +
                                        $"Diff={diff * 1000:F2}ms");
                    }
                    else
                    {
                        Debug.WriteLine($"[SEGMENT-DURATION-OK] Seg #{segment.SegmentIndex}: " +
                                        $"Duration={segment.ActualOutputDuration:F6}s (Diff={diff * 1000:F2}ms)");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SEGMENT-PROBE-ERROR] Seg #{segment.SegmentIndex}: {ex.Message}");
                }

                progress?.Report($"Segment #{segment.SegmentIndex}: Hoàn tất");
                return true;
            }
            catch (Exception ex)
            {
                segment.HasError = true;
                segment.ErrorMessage = ex.Message;
                Debug.WriteLine($"[SEGMENT-ERROR] Seg #{segment.SegmentIndex}: {ex.Message}");
                return false;
            }
        }
        private string BuildVideoSpeedFilter(double speed)
        {
            if (Math.Abs(speed - 1.0) < 0.001)
                return string.Empty;

            // Clamp speed
            speed = Math.Clamp(speed, 0.25, 4.0);

            // setpts formula: PTS / speed
            // speed < 1.0 (slow down): PTS / 0.5 = PTS * 2 (video dài hơn)
            // speed > 1.0 (speed up): PTS / 2.0 = PTS * 0.5 (video ngắn hơn)
            double ptsMultiplier = 1.0 / speed;

            return $"setpts={ptsMultiplier.ToString("0.######", CultureInfo.InvariantCulture)}*PTS";
        }

        /// <summary>
        /// Build blur filter cho 1 segment
        /// </summary>
        private string BuildBlurFilterForSegment(string videoInLabel, string videoOutLabel, VideoSegmentInfo segment)
        {
            if (_currentProject.BlurRectNormalized == null)
                return string.Empty;

            var rect = _currentProject.BlurRectNormalized.Value;
            double sigma = _currentProject.BlurPreviewRadius / 100.0 * 20.0; // Convert to sigma
            sigma = Math.Clamp(sigma, 0.1, 50.0);

            var ci = CultureInfo.InvariantCulture;

            // Tính crop và overlay expressions
            string wCrop = $"iw*{rect.Width.ToString(ci)}";
            string hCrop = $"ih*{rect.Height.ToString(ci)}";
            string xCrop = $"iw*{rect.X.ToString(ci)}";
            string yCrop = $"ih*{rect.Y.ToString(ci)}";

            // Build filter
            return $"[{videoInLabel}]split[orig][crop];" +
                   $"[crop]crop=w={wCrop}:h={hCrop}:x={xCrop}:y={yCrop},gblur=sigma={sigma.ToString(ci)}[blurred];" +
                   $"[orig][blurred]overlay=x={xCrop}:y={yCrop}:enable='1'[{videoOutLabel}]";
        }

        // ============================================================================
        // BƯỚC 6: XỬ LÝ SONG SONG NHIỀU SEGMENTS
        // ============================================================================

        /// <summary>
        /// Xử lý song song tối đa N segments cùng lúc
        /// </summary>
        private async Task ProcessSegmentsInParallelAsync(
            List<VideoSegmentInfo> segments,
            MediaAsset mainVideoAsset,
            string ffmpegPath,
            VideoExportSettings settings,
            GenerateVideoWindow progressWindow,
            int maxParallelProcesses,
            CancellationToken cancellationToken)
        {
            var queue = new ParallelProcessingQueue
            {
                PendingSegments = new ConcurrentQueue<VideoSegmentInfo>(segments),
                CompletedSegments = new ConcurrentBag<VideoSegmentInfo>(),
                FailedSegments = new ConcurrentBag<VideoSegmentInfo>(),
                TotalSegments = segments.Count,
                MaxParallelProcesses = maxParallelProcesses
            };

            // Progress reporter
            var progress = new Progress<string>(msg =>
            {
                int processed = queue.ProcessedCount;
                int total = queue.TotalSegments;
                int percentage = (int)((double)processed / total * 80) + 10; // 10-90%

                progressWindow.UpdateProgress(percentage,
                    $"Đang xử lý segments: {processed}/{total} - {msg}");
            });

            // Create worker tasks
            var tasks = new List<Task>();

            for (int i = 0; i < maxParallelProcesses; i++)
            {
                int workerId = i + 1;

                tasks.Add(Task.Run(async () =>
                {
                    while (queue.PendingSegments.TryDequeue(out VideoSegmentInfo segment))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        // Retry logic (max 3 attempts)
                        bool success = false;
                        int attempts = 0;
                        const int maxAttempts = 3;

                        while (!success && attempts < maxAttempts)
                        {
                            attempts++;

                            try
                            {
                                success = await ProcessSingleSegmentAsync(
                                    segment,
                                    mainVideoAsset,
                                    ffmpegPath,
                                    settings,
                                    progress,
                                    cancellationToken);

                                if (!success && attempts < maxAttempts)
                                {
                                    await Task.Delay(1000, cancellationToken); // Wait before retry
                                }
                            }
                            catch (Exception ex)
                            {
                                segment.HasError = true;
                                segment.ErrorMessage = $"Attempt {attempts}: {ex.Message}";

                                if (attempts >= maxAttempts)
                                {
                                    success = false;
                                }
                            }
                        }

                        if (success)
                        {
                            queue.CompletedSegments.Add(segment);
                        }
                        else
                        {
                            queue.FailedSegments.Add(segment);
                        }

                        queue.IncrementProcessed();
                    }
                }, cancellationToken));
            }

            // Wait for all workers to complete
            await Task.WhenAll(tasks);

            // Check for failures
            if (queue.FailedSegments.Any())
            {
                var errorMsg = new StringBuilder();
                errorMsg.AppendLine($"Có {queue.FailedSegments.Count} segments bị lỗi:");

                foreach (var failed in queue.FailedSegments.Take(5))
                {
                    errorMsg.AppendLine($"  - Segment #{failed.SegmentIndex}: {failed.ErrorMessage}");
                }

                if (queue.FailedSegments.Count > 5)
                {
                    errorMsg.AppendLine($"  ... và {queue.FailedSegments.Count - 5} lỗi khác");
                }

                throw new Exception(errorMsg.ToString());
            }
        }

        private async Task ConcatSegmentsToFinalOutputAsync(
            List<VideoSegmentInfo> segments,
            string ffmpegPath,
            VideoExportSettings settings,
            string tempAssPath,
            string outputPath,
            GenerateVideoWindow progressWindow,
            CancellationToken cancellationToken)
        {
            progressWindow.UpdateProgress(91, "Đang ghép các segments...");

            var orderedSegments = segments.OrderBy(s => s.OutputStart).ToList();

            // Tạo concat list file
            string concatListPath = TempFileManager.CreateTempFile(".txt");
            var concatContent = new StringBuilder();

            foreach (var segment in orderedSegments)
            {
                if (File.Exists(segment.OutputPath))
                {
                    // FIX: Escape path đúng cách cho concat demuxer
                    string escapedPath = segment.OutputPath.Replace("\\", "/").Replace("'", "'\\''");
                    concatContent.AppendLine($"file '{escapedPath}'");
                }
                else
                {
                    throw new Exception($"Segment #{segment.SegmentIndex} không tồn tại: {segment.OutputPath}");
                }
            }

            await WriteTextNoBomAsync(concatListPath, concatContent.ToString(), cancellationToken);
            TempFileManager.RegisterForCleanup(concatListPath);

            progressWindow.UpdateProgress(93, "Đang concat segments...");

            // Step 1: Concat segments thành một video trung gian (không có effects)
            string intermediateVideoPath = TempFileManager.CreateTempFile(".mp4");
            TempFileManager.RegisterForCleanup(intermediateVideoPath);

            var concatArgs = new StringBuilder();
            concatArgs.Append("-y -hide_banner -loglevel error ");

            // FIX CHÍNH #1: Flags để xử lý timestamps chính xác
            // +genpts: Generate presentation timestamps
            // +igndts: Ignore decode timestamps (quan trọng vì DTS có thể không liên tục giữa segments)
            concatArgs.Append("-fflags +genpts+igndts ");

            // FIX CHÍNH #2: Concat demuxer với safe mode off
            concatArgs.Append($"-f concat -safe 0 -i \"{concatListPath}\" ");

            // FIX CHÍNH #3: Copy streams vì segments đã chuẩn hóa
            // Dùng copy rất nhanh và tránh re-encode gây mất chất lượng
            concatArgs.Append("-c:v copy -c:a copy ");

            // FIX CHÍNH #4: Xử lý negative timestamps
            concatArgs.Append("-avoid_negative_ts make_zero ");

            // FIX CHÍNH #5: Vsync passthrough để giữ nguyên timing
            concatArgs.Append("-vsync passthrough ");

            // FIX CHÍNH #6: Thêm -max_interleave_delta để xử lý interleaving tốt hơn
            concatArgs.Append("-max_interleave_delta 0 ");

            concatArgs.Append($"\"{intermediateVideoPath}\"");

            Debug.WriteLine($"[CONCAT-CMD] {concatArgs}");
            progressWindow.UpdateProgress(94, "Đang concat video...");

            int concatExitCode = await RunFfmpegAsync(ffmpegPath, concatArgs.ToString(), cancellationToken);
            if (concatExitCode != 0)
            {
                throw new Exception($"Concat segments thất bại với exit code {concatExitCode}");
            }

            if (!File.Exists(intermediateVideoPath))
            {
                throw new Exception("Không thể tạo intermediate video sau concat");
            }

            // Verify intermediate video duration
            try
            {
                var intermediateInfo = await FFProbe.AnalyseAsync(intermediateVideoPath);
                double expectedTotalDuration = segments.Sum(s => s.ActualOutputDuration);
                double actualDuration = intermediateInfo.Duration.TotalSeconds;
                double diff = actualDuration - expectedTotalDuration;

                Debug.WriteLine($"[CONCAT-VERIFY] Expected={expectedTotalDuration:F3}s, Actual={actualDuration:F3}s, Diff={diff * 1000:F2}ms");

                if (Math.Abs(diff) > 0.15) // Tolerance 150ms
                {
                    Debug.WriteLine($"[CONCAT-WARNING] Duration mismatch > 150ms!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CONCAT-VERIFY-ERROR] {ex.Message}");
            }

            progressWindow.UpdateProgress(95, "Đang áp dụng effects và burn subtitles...");

            // Step 2: Apply effects (images, audio, subtitles) lên intermediate video
            var finalArgs = new StringBuilder();
            finalArgs.Append("-y -hide_banner -loglevel error ");
            finalArgs.Append($"-i \"{intermediateVideoPath}\" ");

            // Add background/FX audio inputs
            var (_, bgFxClips) = ClassifyAudioClipsForExport();
            var distinctBgFx = bgFxClips
                .Select(b => b.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var inputMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int inputIndex = 1;

            foreach (var bgPath in distinctBgFx)
            {
                finalArgs.Append($"-i \"{bgPath}\" ");
                inputMap[bgPath] = inputIndex++;
            }

            // Add image inputs
            var imageClips = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Image && c.SourceData is MediaAsset)
                .ToList();

            var distinctImages = imageClips
                .Select(c => ((MediaAsset)c.SourceData).FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var imgPath in distinctImages)
            {
                finalArgs.Append($"-stream_loop -1 -i \"{imgPath}\" ");
                inputMap[imgPath] = inputIndex++;
            }

            // Build filter complex for effects
            var filterComplex = new StringBuilder();
            string currentVideoTag = "0:v";

            // Image overlays
            int overlayIdx = 0;
            foreach (var imgClip in imageClips)
            {
                if (imgClip.SourceData is not MediaAsset imgAsset) continue;
                if (!inputMap.TryGetValue(imgAsset.FilePath, out int imgInputIndex)) continue;

                // Map source time to output time dựa trên segments
                double tStart = MapSourceTimeToOutputTimelineSeconds(imgClip.RenderStartTime.TotalSeconds, segments);
                double tEnd = tStart + imgClip.RenderDuration.TotalSeconds;

                string scaledLabel = $"img_scaled_{overlayIdx}";
                string processedLabel = $"img_proc_{overlayIdx}";
                string overLabel = $"ov{overlayIdx}";

                int targetW = (int)(imgAsset.Width * imgAsset.ScaleX);
                int targetH = (int)(imgAsset.Height * imgAsset.ScaleY);
                double centerX_norm = imgAsset.PositionX;
                double centerY_norm = imgAsset.PositionY;
                int exportW = settings.ResolutionWidth;
                int exportH = settings.ResolutionHeight;
                double centerX_out = centerX_norm * exportW;
                double centerY_out = centerY_norm * exportH;

                filterComplex.Append($"[{imgInputIndex}:v]scale={targetW}:{targetH}[{scaledLabel}];");

                double opacity = 1.0;
                filterComplex.Append($"[{scaledLabel}]format=yuva420p,colorchannelmixer=aa={opacity.ToString(CultureInfo.InvariantCulture)},fifo[{processedLabel}];");

                string ox = $"{centerX_out.ToString(CultureInfo.InvariantCulture)}-overlay_w/2";
                string oy = $"{centerY_out.ToString(CultureInfo.InvariantCulture)}-overlay_h/2";
                string enableExpr = $"between(t,{tStart.ToString("0.######", CultureInfo.InvariantCulture)},{tEnd.ToString("0.######", CultureInfo.InvariantCulture)})";

                filterComplex.Append($"[{currentVideoTag}][{processedLabel}]overlay=x='{ox}':y='{oy}':eval=init:shortest=1:format=auto:enable='{enableExpr}'[{overLabel}];");

                currentVideoTag = overLabel;
                overlayIdx++;
            }

            // Burn subtitles
            if (File.Exists(tempAssPath))
            {
                string escapedAssPath = EscapePathForFilterScript(tempAssPath);
                filterComplex.Append($"[{currentVideoTag}]subtitles=filename='{escapedAssPath}':charenc=UTF-8[video_final];");
                currentVideoTag = "video_final";
            }
            else
            {
                filterComplex.Append($"[{currentVideoTag}]null[video_final];");
                currentVideoTag = "video_final";
            }

            // Audio mixing
            var audioMixInputs = new List<string> { "[0:a]" };
            foreach (var bg in bgFxClips)
            {
                if (!inputMap.TryGetValue(bg.FilePath, out int bgInputIndex)) continue;

                string processedLabel = $"bgp_{bgInputIndex}";
                string delayedLabel = $"bg_{bgInputIndex}";
                string volumeGain = DbToGain(bg.VolumeDb).ToString(CultureInfo.InvariantCulture);

                filterComplex.Append($"[{bgInputIndex}:a]volume={volumeGain},aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[{processedLabel}];");

                double mappedBgStartSec = MapSourceTimeToOutputTimelineSeconds(bg.StartTime.TotalSeconds, segments);
                long delayMs = (long)Math.Max(0, mappedBgStartSec * 1000);
                filterComplex.Append($"[{processedLabel}]adelay={delayMs}|{delayMs}[{delayedLabel}];");
                audioMixInputs.Add($"[{delayedLabel}]");
            }

            if (audioMixInputs.Count > 1)
            {
                filterComplex.Append($"{string.Join("", audioMixInputs)}amix=inputs={audioMixInputs.Count}:duration=longest:normalize=0:dropout_transition=0,alimiter=limit=0.95[audio_final]");
            }
            else
            {
                filterComplex.Append("[0:a]alimiter=limit=0.95[audio_final]");
            }

            // Write filter script
            string filterScriptPath = TempFileManager.CreateTempFile(".txt");
            await WriteTextNoBomAsync(filterScriptPath, filterComplex.ToString(), cancellationToken);
            TempFileManager.RegisterForCleanup(filterScriptPath);

            finalArgs.Append($"-filter_complex_script \"{filterScriptPath}\" ");
            finalArgs.Append("-filter_threads 0 ");
            finalArgs.Append("-map \"[video_final]\" -map \"[audio_final]\" ");

            // Encoding settings
            finalArgs.Append($"-c:v {settings.VideoCodec} ");
            if (settings.VideoCodec.Contains("264") || settings.VideoCodec.Contains("265"))
            {
                finalArgs.Append($"-preset {settings.Preset} ");
                finalArgs.Append($"-crf {settings.Crf} ");
            }
            else if (settings.VideoCodec.Contains("nvenc"))
            {
                finalArgs.Append("-preset p4 -rc vbr -cq 23 ");
            }

            finalArgs.Append("-c:a aac -b:a 192k -ar 48000 ");
            finalArgs.Append($"\"{outputPath}\"");

            Debug.WriteLine($"[FINAL-CMD] {finalArgs}");
            progressWindow.UpdateProgress(96, "Đang render final output...");

            await ExecuteFfmpegWithProgress(ffmpegPath, finalArgs.ToString(), progressWindow, outputPath, cancellationToken);

            progressWindow.UpdateProgress(100, "Hoàn tất!");
        }
        private TimeSpan MapSourceTimeToOutputTimeline(TimeSpan sourceTime, List<VideoSegmentInfo> segments)
        {
            // Hàm này giữ lại để tương thích với các logic cũ nếu cần
            foreach (var segment in segments.OrderBy(s => s.SourceStart))
            {
                if (sourceTime >= segment.SourceStart && sourceTime < segment.SourceEnd)
                {
                    double progressInSegment = (sourceTime - segment.SourceStart).TotalSeconds;
                    double segmentRatio = segment.SourceDuration.TotalSeconds > 0
                        ? progressInSegment / segment.SourceDuration.TotalSeconds
                        : 0;

                    double offsetInOutput = segmentRatio * segment.TargetDuration.TotalSeconds;
                    return segment.OutputStart + TimeSpan.FromSeconds(offsetInOutput);
                }
            }
            return sourceTime;
        }
        private async Task ExecuteParallelSmartCutExportAsync(
            string ffmpegPath,
            VideoExportSettings settings,
            GenerateVideoWindow progressWindow,
            CancellationToken cancellationToken)
        {
            progressWindow.UpdateProgress(1, "Chuẩn bị Smart Cut Mode 2...");

            // 1. Get main video asset
            var mainVideoClip = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
            if (mainVideoClip == null || mainVideoClip.SourceData is not MediaAsset mainVideoAsset ||
                string.IsNullOrWhiteSpace(mainVideoAsset.FilePath) || !File.Exists(mainVideoAsset.FilePath))
            {
                throw new Exception("Không tìm thấy video chính để xuất ở chế độ Parallel Smart Cut.");
            }

            progressWindow.UpdateProgress(3, "Đang phân tích timeline...");

            // 2. Build smart cut map
            var mapItems = await BuildSmartCutMapItemsAsync();
            if (mapItems == null || mapItems.Count == 0)
            {
                throw new Exception("Không có dữ liệu Smart Cut để xử lý.");
            }

            progressWindow.UpdateProgress(5, "Đang tạo danh sách segments...");

            // 3. Build segment list
            var segments = await BuildSegmentListForParallelProcessing(
                mainVideoAsset,
                mapItems,
                cancellationToken);

            if (segments.Count == 0)
            {
                throw new Exception("Không có segment nào để xử lý.");
            }
            Debug.WriteLine("--- [SMARTCUT PARALLEL PLAN] ---");
            foreach (var seg in segments)
            {
                Debug.WriteLine($"[PLAN] Seg #{seg.SegmentIndex}: " +
                                $"Source[{seg.SourceStart:G} -> {seg.SourceEnd:G} | Dur: {seg.SourceDuration:G}], " +
                                $"Speed: {seg.RequiredSpeed:F6}, " +
                                $"TargetDur: {seg.TargetDuration:G}");
            }
            Debug.WriteLine("--- [END PLAN] ---");
            progressWindow.UpdateProgress(8, $"Đã tạo {segments.Count} segments. Bắt đầu xử lý song song...");

            // 4. Process segments in parallel (5-10 processes)
            int maxParallelProcesses = Math.Min(
                Math.Max(Environment.ProcessorCount / 2, 2),
                10);

            await ProcessSegmentsInParallelAsync(
                segments,
                mainVideoAsset,
                ffmpegPath,
                settings,
                progressWindow,
                maxParallelProcesses,
                cancellationToken);

            progressWindow.UpdateProgress(90, "Đang chuẩn bị subtitle và effects...");

            // 5. Generate ASS subtitle file
            string tempAssPath = TempFileManager.CreateTempFile(".ass");

            // Build output timeline for ALL subtitles (bao gồm cả subtitle có voice và không có voice)
            var outputTimeline = new List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)>();

            // 5.1. Thêm các subtitle từ segments (các subtitle có voice)
            var voicedSubtitles = segments
                .Where(s => !s.IsSilenceGap && s.SubtitleLine != null)
                .Select(s => (s.SubtitleLine, s.OutputStart, s.OutputEnd))
                .ToList();

            outputTimeline.AddRange(voicedSubtitles);

            Debug.WriteLine($"[SUBTITLE-TIMELINE] Added {voicedSubtitles.Count} voiced subtitles from segments");

            // 5.2. Lấy TẤT CẢ subtitle và text clips từ timeline (bao gồm cả subtitle không có voice)
            var allSubtitlesAndTexts = new List<SrtSubtitleLine>();
            if (_currentProject?.Subtitles != null)
            {
                allSubtitlesAndTexts.AddRange(_currentProject.Subtitles.Where(s => !s.IsTextClip));
            }
            if (_currentProject?.TextClips != null)
            {
                allSubtitlesAndTexts.AddRange(_currentProject.TextClips.Where(t => t.IsTextClip));
            }

            Debug.WriteLine($"[SUBTITLE-TIMELINE] Total subtitles in project: {allSubtitlesAndTexts.Count}");

            // 5.3. Lọc ra các subtitle/text KHÔNG có voice (chưa được thêm vào outputTimeline)
            var voicedSubtitleSet = new HashSet<SrtSubtitleLine>(voicedSubtitles.Select(v => v.SubtitleLine));
            var nonVoicedSubtitles = allSubtitlesAndTexts
                .Where(line => !voicedSubtitleSet.Contains(line))
                .ToList();

            Debug.WriteLine($"[SUBTITLE-TIMELINE] Non-voiced subtitles to process: {nonVoicedSubtitles.Count}");

            // 5.4. Map thời gian của các subtitle không có voice sang output timeline
            foreach (var line in nonVoicedSubtitles)
            {
                // Map source time sang output time dựa trên segments
                double sourceStartSec = line.StartTime.TotalSeconds;
                double sourceEndSec = line.EndTime.TotalSeconds;

                double outputStartSec = MapSourceTimeToOutputTimelineSeconds(sourceStartSec, segments);
                double outputEndSec = MapSourceTimeToOutputTimelineSeconds(sourceEndSec, segments);

                // Đảm bảo output end time >= output start time
                if (outputEndSec < outputStartSec)
                {
                    outputEndSec = outputStartSec + 0.1; // Tối thiểu 100ms
                }

                TimeSpan mappedStart = TimeSpan.FromSeconds(outputStartSec);
                TimeSpan mappedEnd = TimeSpan.FromSeconds(outputEndSec);

                // Thêm vào output timeline
                outputTimeline.Add((line, mappedStart, mappedEnd));

                Debug.WriteLine($"[SUBTITLE-MAP] Line #{line.Index}: " +
                                $"Source[{sourceStartSec:F3}s-{sourceEndSec:F3}s] -> " +
                                $"Output[{outputStartSec:F3}s-{outputEndSec:F3}s]");
            }

            Debug.WriteLine($"[SUBTITLE-TIMELINE] Total subtitles in output timeline: {outputTimeline.Count}");

            // 5.5. Sắp xếp timeline theo thời gian
            outputTimeline = outputTimeline.OrderBy(t => t.FinalStart).ToList();

            // 5.6. Tạo file ASS
            if (outputTimeline.Any())
            {
                string assContent = GenerateAssFileContentForSmartCut(
                    settings.ResolutionWidth,
                    settings.ResolutionHeight,
                    outputTimeline);

                await File.WriteAllTextAsync(tempAssPath, assContent, Encoding.UTF8, cancellationToken);
                TempFileManager.RegisterForCleanup(tempAssPath);

                Debug.WriteLine($"[SUBTITLE-ASS] Generated ASS file with {outputTimeline.Count} subtitle entries");
            }
            else
            {
                Debug.WriteLine("[SUBTITLE-ASS] No subtitles to burn, skipping ASS file creation");
            }

            // 6. Concat segments to final output
            await ConcatSegmentsToFinalOutputAsync(
                segments,
                ffmpegPath,
                settings,
                tempAssPath,
                settings.OutputPath,
                progressWindow,
                cancellationToken);

            progressWindow.UpdateProgress(100, "Hoàn tất!");
            progressWindow.MarkAsComplete(true);
        }
        private void FontFamilyComboBox_DropDownClosed(object sender, EventArgs e)
        {
            ExitAuxUi();
            if (_originalFontFamilyBeforePreview != null && _selectedSubtitle != null)
            {
                if (_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual))
                {
                    ApplyStyleToVisual(currentVisual, _selectedSubtitle.Style);
                }
                else
                {
                }
            }

            _originalFontFamilyBeforePreview = null;
        }
        private void TtsProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isTtsSliderDragging = true;
        }

        private void TtsProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isTtsSliderDragging = false;
            _ttsAudioPlayer.Position = TimeSpan.FromSeconds(TtsProgressSlider.Value);
        }
        private string SanitizeFileName(string text)
        {
            string partialText = text.Length > 20 ? text.Substring(0, 20) : text;
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(invalidChars)));
            return r.Replace(partialText, "");
        }
        private void InitializeEditorEvents()
        {
            FontFamilyComboBox.SelectionChanged += EditorControl_ValueChanged;
            FontSizeSlider.ValueChanged += EditorControl_ValueChanged;
            FontSizeTextBox.TextChanged += EditorControl_ValueChanged_TextBox;
            FontColorPicker.SelectedColorChanged += EditorControl_ValueChanged;
            OpacitySlider.ValueChanged += EditorControl_ValueChanged;
            OpacityTextBox.TextChanged += EditorControl_ValueChanged_TextBox;
            BoldButton.Click += EditorControl_ValueChanged;
            ItalicButton.Click += EditorControl_ValueChanged;
            UnderlineButton.Click += EditorControl_ValueChanged;
            CharacterSpacingSlider.ValueChanged += EditorControl_ValueChanged;

            BackgroundEnabledCheckBox.Checked += EditorControl_ValueChanged;
            BackgroundEnabledCheckBox.Unchecked += EditorControl_ValueChanged;
            BackgroundColorPicker.SelectedColorChanged += EditorControl_ValueChanged;
            BackgroundOpacitySlider.ValueChanged += EditorControl_ValueChanged;
            BackgroundCornerRadiusSlider.ValueChanged += EditorControl_ValueChanged;
            BackgroundPaddingXSlider.ValueChanged += EditorControl_ValueChanged;
            BackgroundPaddingYSlider.ValueChanged += EditorControl_ValueChanged;

            OutlineEnabledCheckBox.Checked += EditorControl_ValueChanged;
            OutlineEnabledCheckBox.Unchecked += EditorControl_ValueChanged;
            OutlineColorPicker.SelectedColorChanged += EditorControl_ValueChanged;
            OutlineThicknessSlider.ValueChanged += EditorControl_ValueChanged;

            ShadowEnabledCheckBox.Checked += EditorControl_ValueChanged;
            ShadowEnabledCheckBox.Unchecked += EditorControl_ValueChanged;
            ShadowColorPicker.SelectedColorChanged += EditorControl_ValueChanged;
            ShadowOpacitySlider.ValueChanged += EditorControl_ValueChanged;
            ShadowBlurSlider.ValueChanged += EditorControl_ValueChanged;
            ShadowDepthSlider.ValueChanged += EditorControl_ValueChanged;
            ShadowDirectionSlider.ValueChanged += EditorControl_ValueChanged;
        }

        private void EditorControl_ValueChanged_TextBox(object sender, TextChangedEventArgs e)
        {
            EditorControl_ValueChanged(sender, e);
        }
        private void EditorControl_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUiFromCode) return;

            var selectedClips = TimelineClips.Where(c => c.IsSelected).ToList();
            var selectedSrtLines = selectedClips
                .Select(c => c.SourceData as SrtSubtitleLine)
                .Where(s => s != null)
                .ToList();

            if (!selectedSrtLines.Any())
            {
                UpdateProjectTemplateFromUi();
                var finalTemplate = _currentProject.GetTemplateAsStyleState();
                ApplyTemplateToAllSubtitlesAndUpdate(finalTemplate, null);
            }
            else
            {
                var textClips = selectedSrtLines.Where(s => s.IsTextClip).ToList();
                var subtitleClips = selectedSrtLines.Where(s => !s.IsTextClip).ToList();
                foreach (var line in textClips)
                {
                    var style = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();
                    UpdateStyleFromUi(style);
                    line.Style = style;
                    if (_activeVisuals.TryGetValue(line, out var v))
                    {
                        ApplyStyleToVisual(v, style);
                        if (_selectedSubtitle == line)
                        {
                            RemoveSubtitleAdorner();
                            AddSubtitleAdorner(v, style);
                        }
                    }
                }
                if (subtitleClips.Any())
                {
                    UpdateProjectTemplateFromUi();
                    var finalTemplate = _currentProject.GetTemplateAsStyleState();
                    var mainSubtitleToUpdate = subtitleClips.FirstOrDefault(s => s == _selectedSubtitle) ?? subtitleClips.First();
                    ApplyTemplateToAllSubtitlesAndUpdate(finalTemplate, mainSubtitleToUpdate);
                }
            }
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
            UpdateChildPanelVisibility();
        }
        private void UpdateProjectTemplateFromUi()
        {
            _isUpdatingUiFromCode = true;
            try
            {
                var fonts = FontFamilyComboBox.ItemsSource as IEnumerable<FontFamily>;
                _currentProject.TemplateFontFamily = (fonts?.FirstOrDefault(f => f == (FontFamily)FontFamilyComboBox.SelectedItem)?.Source) ?? _currentProject.TemplateFontFamily;
                double integerFontSize = Math.Round(FontSizeSlider.Value);
                _currentProject.TemplateFontSize = integerFontSize;
                FontSizeSlider.Value = integerFontSize;
                _currentProject.TemplateFontColor = (FontColorPicker.SelectedColor?.ToString()) ?? _currentProject.TemplateFontColor;
                _currentProject.TemplateOpacity = OpacitySlider.Value / 100.0;
                _currentProject.TemplateFontWeight = (BoldButton.IsChecked == true) ? 700 : 400;
                _currentProject.TemplateIsItalic = (ItalicButton.IsChecked == true);
                _currentProject.TemplateIsUnderlined = (UnderlineButton.IsChecked == true);
                _currentProject.TemplateCharacterSpacing = CharacterSpacingSlider.Value;
                if (AlignLeftButton.IsChecked == true) _currentProject.TemplateAlignment = 1;
                else if (AlignCenterButton.IsChecked == true) _currentProject.TemplateAlignment = 2;
                else if (AlignRightButton.IsChecked == true) _currentProject.TemplateAlignment = 3;
                else if (AlignJustifyButton.IsChecked == true) _currentProject.TemplateAlignment = 4;
                _currentProject.IsBackgroundEnabled = BackgroundEnabledCheckBox.IsChecked == true;
                var bgColor = BackgroundColorPicker.SelectedColor ?? Colors.Black;
                var bgOpacity = BackgroundOpacitySlider.Value / 100.0;
                _currentProject.TemplateBackgroundColor = Color.FromArgb((byte)(bgOpacity * 255), bgColor.R, bgColor.G, bgColor.B).ToString();
                _currentProject.TemplateBackgroundOpacity = bgOpacity;
                _currentProject.TemplateBackgroundCornerRadius = BackgroundCornerRadiusSlider.Value;
                _currentProject.TemplateBackgroundPaddingX = BackgroundPaddingXSlider.Value;
                _currentProject.TemplateBackgroundPaddingY = BackgroundPaddingYSlider.Value;
                _currentProject.IsOutlineEnabled = OutlineEnabledCheckBox.IsChecked == true;
                _currentProject.TemplateOutlineColor = OutlineColorPicker.SelectedColor?.ToString() ?? _currentProject.TemplateOutlineColor;
                _currentProject.TemplateOutlineThickness = OutlineThicknessSlider.Value;
                _currentProject.IsShadowEnabled = ShadowEnabledCheckBox.IsChecked == true;
                var shadowColor = ShadowColorPicker.SelectedColor ?? Colors.Black;
                var shadowOpacity = ShadowOpacitySlider.Value / 100.0;
                _currentProject.TemplateShadowColor = Color.FromArgb((byte)(shadowOpacity * 255), shadowColor.R, shadowColor.G, shadowColor.B).ToString();
                _currentProject.TemplateShadowBlur = ShadowBlurSlider.Value;
                _currentProject.TemplateShadowDepth = ShadowDepthSlider.Value;
                _currentProject.TemplateShadowDirection = ShadowDirectionSlider.Value;
            }
            finally
            {
                _isUpdatingUiFromCode = false;
            }
        }

        private void UpdateStyleFromUi(StyleState styleToUpdate)
        {
            var fonts = FontFamilyComboBox.ItemsSource as IEnumerable<FontFamily>;
            styleToUpdate.FontFamilyName = (fonts?.FirstOrDefault(f => f == (FontFamily)FontFamilyComboBox.SelectedItem)?.Source) ?? styleToUpdate.FontFamilyName;
            double integerFontSize = Math.Round(FontSizeSlider.Value);
            styleToUpdate.FontSize = integerFontSize;
            FontSizeSlider.Value = integerFontSize;
            styleToUpdate.FontColorHex = (FontColorPicker.SelectedColor?.ToString()) ?? styleToUpdate.FontColorHex;
            styleToUpdate.Opacity = OpacitySlider.Value / 100.0;
            styleToUpdate.FontWeightValue = (BoldButton.IsChecked == true) ? 700 : 400;
            styleToUpdate.IsItalic = (ItalicButton.IsChecked == true);
            styleToUpdate.IsUnderlined = (UnderlineButton.IsChecked == true);
            styleToUpdate.CharacterSpacing = CharacterSpacingSlider.Value;
            if (AlignLeftButton.IsChecked == true) styleToUpdate.Alignment = 1;
            else if (AlignCenterButton.IsChecked == true) styleToUpdate.Alignment = 2;
            else if (AlignRightButton.IsChecked == true) styleToUpdate.Alignment = 3;
            else if (AlignJustifyButton.IsChecked == true) styleToUpdate.Alignment = 4;
            else styleToUpdate.Alignment = 0;
            styleToUpdate.IsBackgroundEnabled = BackgroundEnabledCheckBox.IsChecked == true;
            var bgColor = BackgroundColorPicker.SelectedColor ?? Colors.Black;
            var bgOpacity = BackgroundOpacitySlider.Value / 100.0;
            styleToUpdate.BackgroundColorHex = Color.FromArgb((byte)(bgOpacity * 255), bgColor.R, bgColor.G, bgColor.B).ToString();
            styleToUpdate.BackgroundCornerRadius = BackgroundCornerRadiusSlider.Value;
            styleToUpdate.BackgroundPaddingX = BackgroundPaddingXSlider.Value;
            styleToUpdate.BackgroundPaddingY = BackgroundPaddingYSlider.Value;
            if (styleToUpdate.IsBackgroundEnabled)
                styleToUpdate.EdgeStyle = TextEdgeStyle.None;
            else if (OutlineEnabledCheckBox.IsChecked == true)
                styleToUpdate.EdgeStyle = TextEdgeStyle.Outline;
            else if (ShadowEnabledCheckBox.IsChecked == true)
                styleToUpdate.EdgeStyle = TextEdgeStyle.Shadow;
            else
                styleToUpdate.EdgeStyle = TextEdgeStyle.None;
            styleToUpdate.OutlineColorHex = OutlineColorPicker.SelectedColor?.ToString() ?? styleToUpdate.OutlineColorHex;
            styleToUpdate.OutlineThickness = OutlineThicknessSlider.Value;
            var sColor = ShadowColorPicker.SelectedColor ?? Colors.Black;
            var sOp = ShadowOpacitySlider.Value / 100.0;
            styleToUpdate.ShadowColorHex = Color.FromArgb((byte)(sOp * 255), sColor.R, sColor.G, sColor.B).ToString();
            styleToUpdate.ShadowBlur = ShadowBlurSlider.Value;
            styleToUpdate.ShadowDepth = ShadowDepthSlider.Value;
            styleToUpdate.ShadowDirection = ShadowDirectionSlider.Value;
        }
        private void UpdateEditorPanelVisibility()
        {
            var selectedClips = TimelineClips.Where(c => c.IsSelected).ToList();
            bool showSubtitleEditor = false;
            bool showAudioEditor = false;
            bool showVideoEditor = false;
            AudioEditorPanel.DataContext = null;
            VideoEditorPanel.DataContext = null;
            if (selectedClips.Any())
            {
                var firstClipType = selectedClips.First().ClipType;
                bool allSameType = selectedClips.All(c =>
                {
                    if (firstClipType == TimelineClipType.Text || firstClipType == TimelineClipType.Subtitle)
                    {
                        return c.ClipType == TimelineClipType.Text || c.ClipType == TimelineClipType.Subtitle;
                    }
                    if (firstClipType == TimelineClipType.Video || firstClipType == TimelineClipType.Image)
                    {
                        return c.ClipType == TimelineClipType.Video || c.ClipType == TimelineClipType.Image;
                    }
                    return c.ClipType == firstClipType;
                });

                if (allSameType)
                {
                    switch (firstClipType)
                    {
                        case TimelineClipType.Subtitle:
                        case TimelineClipType.Text:
                            showSubtitleEditor = true;
                            break;

                        case TimelineClipType.Audio:
                            showAudioEditor = true;
                            if (selectedClips.First().SourceData is TimelineAudioClip audioClip)
                            {
                                AudioEditorPanel.DataContext = audioClip;
                            }
                            else
                            {
                                showAudioEditor = false;
                            }
                            break;

                        case TimelineClipType.Video:
                        case TimelineClipType.Image:
                            showVideoEditor = true;
                            VideoEditorPanel.DataContext = selectedClips.First();
                            break;
                    }
                }
            }
            EditorPanel.Visibility = showSubtitleEditor ? Visibility.Visible : Visibility.Collapsed;
            AudioEditorPanel.Visibility = showAudioEditor ? Visibility.Visible : Visibility.Collapsed;
            VideoEditorPanel.Visibility = showVideoEditor ? Visibility.Visible : Visibility.Collapsed;
            if (showSubtitleEditor)
            {
                UpdateEditorPanelFromState();
                _isUpdatingUiFromCode = true;
                var converter = (IValueConverter)FindResource("NewlineConverter");
                if (selectedClips.Count == 1 && selectedClips.First().SourceData is SrtSubtitleLine srtLine)
                {
                    bool useTranslated = CurrentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated;
                    string textForEditor = useTranslated ? srtLine.TranslatedText : srtLine.OriginalText;
                    SelectedTextEditorTextBox.Text = converter.Convert(textForEditor, typeof(string), null, CultureInfo.CurrentCulture) as string;
                }
                else
                {
                    SelectedTextEditorTextBox.Text = string.Empty;
                    SelectedTextEditorTextBox.IsEnabled = false;
                }
                _isUpdatingUiFromCode = false;
            }
            else
            {
                SelectedTextEditorTextBox.IsEnabled = true;
            }
        }
        private StyleState GetTemplateAsStyleState()
        {


            var style = new StyleState
            {
                X = _currentProject.TemplateX,
                Y = _currentProject.TemplateY,
                ScaleX = _currentProject.TemplateScaleX,
                ScaleY = _currentProject.TemplateScaleY,
                Rotation = _currentProject.TemplateRotation,
                Width = _currentProject.TemplateWidth,

                FontFamilyName = _currentProject.TemplateFontFamily,
                FontSize = _currentProject.TemplateFontSize,
                FontColorHex = _currentProject.TemplateFontColor,
                FontWeightValue = _currentProject.TemplateFontWeight,
                IsItalic = _currentProject.TemplateIsItalic,
                IsUnderlined = _currentProject.TemplateIsUnderlined,
                CharacterSpacing = _currentProject.TemplateCharacterSpacing,
                IsBackgroundEnabled = _currentProject.IsBackgroundEnabled,
                BackgroundColorHex = _currentProject.TemplateBackgroundColor,
                BackgroundPaddingX = _currentProject.TemplateBackgroundPaddingX,
                BackgroundPaddingY = _currentProject.TemplateBackgroundPaddingY,
                BackgroundCornerRadius = _currentProject.TemplateBackgroundCornerRadius,
                OutlineColorHex = _currentProject.TemplateOutlineColor,
                OutlineThickness = _currentProject.TemplateOutlineThickness,
                ShadowColorHex = _currentProject.TemplateShadowColor,
                ShadowBlur = _currentProject.TemplateShadowBlur,
                ShadowDepth = _currentProject.TemplateShadowDepth,
                ShadowDirection = _currentProject.TemplateShadowDirection,
            };
            if (_currentProject.IsBackgroundEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.None;
            }
            else if (_currentProject.IsOutlineEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.Outline;
            }
            else if (_currentProject.IsShadowEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.Shadow;
            }
            else
            {
                style.EdgeStyle = TextEdgeStyle.None;
            }
            return style;
        }
        private void UpdateEditorPanelFromState()
        {
            if (_currentProject == null) return;
            _isUpdatingUiFromCode = true;
            try
            {
                StyleState styleSource = null;
                if (_selectedSubtitle?.IsTextClip == true)
                {
                    styleSource = _selectedSubtitle.Style ?? _currentProject.GetTemplateAsStyleState();
                }
                else
                {
                    styleSource = _currentProject.GetTemplateAsStyleState();
                }
                var fonts = FontFamilyComboBox.ItemsSource as IEnumerable<FontFamily>;
                FontFamilyComboBox.SelectedItem = fonts?.FirstOrDefault(f => f.Source == styleSource.FontFamilyName) ?? fonts?.FirstOrDefault();
                FontSizeSlider.Value = styleSource.FontSize;
                FontSizeTextBox.Text = styleSource.FontSize.ToString("F0");
                BoldButton.IsChecked = styleSource.FontWeightValue > 500;
                ItalicButton.IsChecked = styleSource.IsItalic;
                UnderlineButton.IsChecked = styleSource.IsUnderlined;
                FontColorPicker.SelectedColor = (Color?)ColorConverter.ConvertFromString(styleSource.FontColorHex);
                OpacitySlider.Value = styleSource.Opacity * 100.0;
                OpacityTextBox.Text = (styleSource.Opacity * 100.0).ToString("F0");
                CharacterSpacingSlider.Value = styleSource.CharacterSpacing;
                AlignLeftButton.IsChecked = styleSource.Alignment == 1;
                AlignCenterButton.IsChecked = styleSource.Alignment == 2;
                AlignRightButton.IsChecked = styleSource.Alignment == 3;
                AlignJustifyButton.IsChecked = styleSource.Alignment == 4;

                BackgroundEnabledCheckBox.IsChecked = styleSource.IsBackgroundEnabled;
                var bgColor = (Color)(ColorConverter.ConvertFromString(styleSource.BackgroundColorHex) ?? ColorConverter.ConvertFromString("#80000000"));
                BackgroundColorPicker.SelectedColor = Color.FromArgb(255, bgColor.R, bgColor.G, bgColor.B);
                BackgroundOpacitySlider.Value = bgColor.A * 100.0 / 255.0;
                BackgroundCornerRadiusSlider.Value = styleSource.BackgroundCornerRadius;
                BackgroundPaddingXSlider.Value = styleSource.BackgroundPaddingX;
                BackgroundPaddingYSlider.Value = styleSource.BackgroundPaddingY;

                OutlineEnabledCheckBox.IsChecked = (!styleSource.IsBackgroundEnabled && styleSource.EdgeStyle == TextEdgeStyle.Outline);
                ShadowEnabledCheckBox.IsChecked = (!styleSource.IsBackgroundEnabled && styleSource.EdgeStyle == TextEdgeStyle.Shadow);

                OutlineColorPicker.SelectedColor = (Color?)ColorConverter.ConvertFromString(styleSource.OutlineColorHex);
                OutlineThicknessSlider.Value = styleSource.OutlineThickness;

                var shadowColor = (Color)(ColorConverter.ConvertFromString(styleSource.ShadowColorHex) ?? Colors.Black);
                ShadowColorPicker.SelectedColor = Color.FromRgb(shadowColor.R, shadowColor.G, shadowColor.B);
                ShadowOpacitySlider.Value = shadowColor.A * 100.0 / 255.0;
                ShadowBlurSlider.Value = styleSource.ShadowBlur;
                ShadowDepthSlider.Value = styleSource.ShadowDepth;
                ShadowDirectionSlider.Value = styleSource.ShadowDirection;
                UpdateChildPanelVisibility();
                switch (styleSource.Alignment)
                {
                    case 1: SelectedTextEditorTextBox.TextAlignment = TextAlignment.Left; break;
                    case 2: SelectedTextEditorTextBox.TextAlignment = TextAlignment.Center; break;
                    case 3: SelectedTextEditorTextBox.TextAlignment = TextAlignment.Right; break;
                    case 4: SelectedTextEditorTextBox.TextAlignment = TextAlignment.Justify; break;
                    default: SelectedTextEditorTextBox.TextAlignment = TextAlignment.Left; break;
                }
            }
            finally
            {
                _isUpdatingUiFromCode = false;
            }
        }
        private void VoiceoverModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceoverModeComboBox.SelectedItem is ComboBoxItem selectedItem &&
                Enum.TryParse<VoiceoverExportMode>(selectedItem.Tag.ToString(), out var selectedMode))
            {
                _selectedVoiceoverMode = selectedMode;
            }
        }
        private void UpdateChildPanelVisibility()
        {
            BackgroundSettingsPanel.Visibility = BackgroundEnabledCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            OutlineSettingsPanel.Visibility = OutlineEnabledCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ShadowSettingsPanel.Visibility = ShadowEnabledCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyTemplateToAllSubtitlesAndUpdate(StyleState template, SrtSubtitleLine lineToUpdate)
        {

            foreach (var sub in _currentProject.Subtitles.Where(s => !s.IsTextClip))
            {
                var oldWidth = sub.Style?.Width;
                sub.Style = template.Clone();
            }

            if (lineToUpdate != null && _activeVisuals.TryGetValue(lineToUpdate, out var currentVisual))
            {
                ApplyStyleToVisual(currentVisual, lineToUpdate.Style);
                if (_selectedSubtitle == lineToUpdate)
                {
                    RemoveSubtitleAdorner();
                    AddSubtitleAdorner(currentVisual, lineToUpdate.Style);
                }
                CollectAndSendPaddingSample(lineToUpdate, currentVisual);
            }
        }

        private void SelectedTextEditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUiFromCode)
            {
                return;
            }
            if (_selectedSubtitle == null)
            {
                return;
            }
            if (_imeCompositionActive)
            {
                return;
            }

            try
            {
                var converter = (IValueConverter)FindResource("NewlineConverter");
                string newMultilineText = SelectedTextEditorTextBox.Text;
                string editingText = converter.ConvertBack(newMultilineText, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
                if (CurrentProject?.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated)
                {
                    if (_selectedSubtitle.TranslatedText != editingText)
                    {
                        _selectedSubtitle.TranslatedText = editingText;
                    }
                }
                else
                {
                    if (_selectedSubtitle.OriginalText != editingText)
                    {
                        _selectedSubtitle.OriginalText = editingText;
                    }
                }
                DisplaySubtitleOnPlayer(_selectedSubtitle);
            }
            finally
            {
                if (_imeCompositionJustCommitted)
                {
                    _imeCompositionJustCommitted = false;
                }
            }
        }
        private void DisableBlurFeature()
        {
            if (_currentProject != null)
            {
                _currentProject.BlurMode = ProjectState.BlurApplyMode.None;
                _currentProject.BlurRectNormalized = null;
                SaveProjectCurrent();
            }
            RemoveBlurAdorner();
            if (_blurPreview != null)
            {
                if (SubtitleRenderCanvas.Children.Contains(_blurPreview))
                {
                    SubtitleRenderCanvas.Children.Remove(_blurPreview);
                }
                _blurPreview = null;
            }
        }
        private void ShowAngleDisplay(double angle)
        {
            AngleDisplayText.Text = $"{angle:F2}°";
            AngleDisplay.Visibility = Visibility.Visible;
        }

        private void HideAngleDisplay()
        {
            AngleDisplay.Visibility = Visibility.Collapsed;
        }
        private void ImageAdorner_RotationStarted()
        {
            if (_activeTransformingImageAsset != null)
            {
                ShowAngleDisplay(_activeTransformingImageAsset.Rotation);
            }
        }

        private void ImageAdorner_RotationCompleted()
        {
            HideAngleDisplay();
        }
        private RotateTransform GetOrCreateRotateTransform(FrameworkElement element)
        {
            var transformGroup = element.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                var oldTransform = element.RenderTransform;
                if (oldTransform != null && oldTransform != Transform.Identity)
                {
                    transformGroup.Children.Add(oldTransform);
                }
                element.RenderTransform = transformGroup;
            }

            var rotateTransform = transformGroup.Children.OfType<RotateTransform>().FirstOrDefault();
            if (rotateTransform == null)
            {
                rotateTransform = new RotateTransform();
                transformGroup.Children.Insert(1, rotateTransform);
            }
            return rotateTransform;
        }
        private void SubtitleAdorner_RotationStarted()
        {
            if (_selectedSubtitle?.Style == null || !_activeVisuals.TryGetValue(_selectedSubtitle, out var v))
            {
                return;
            }
            _isAdornerRotating = true;
            _rotatingVisual = v;
            _activeRotateTransform = GetOrCreateRotateTransform(v);
            ShowAngleDisplay(_selectedSubtitle.Style.Rotation);
        }

        private void SubtitleAdorner_RotationCompleted()
        {
            if (!_isAdornerRotating || _selectedSubtitle?.Style == null || _rotatingVisual == null)
            {
                return;
            }
            double finalAngle = _activeRotateTransform.Angle;
            _selectedSubtitle.Style.Rotation = finalAngle;
            _isAdornerRotating = false;
            _rotatingVisual = null;
            _activeRotateTransform = null;
            HideAngleDisplay();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }
        private void SrtLinesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var newlySelected = SrtLinesDataGrid.SelectedItem as SrtSubtitleLine;
            if (SelectedSubtitle != null && _activeVisuals.TryGetValue(SelectedSubtitle, out var oldVisual))
                RemoveSubtitleAdorner();
            SelectedSubtitle = newlySelected;
            _isTextInEditMode = false;
            SelectedAudioClip = null;
            var clipVM = TimelineClips.FirstOrDefault(c => c.SourceData == SelectedSubtitle);
            if (_selectedTimelineClip != clipVM)
            {
                foreach (var c in TimelineClips.Where(c => c.IsSelected)) c.IsSelected = false;
                _selectedTimelineClip = clipVM;
                if (_selectedTimelineClip != null) _selectedTimelineClip.IsSelected = true;
            }

            if (SelectedSubtitle != null)
            {
                if (_activeVisuals.TryGetValue(SelectedSubtitle, out var visual))
                {
                    RemoveVideoAdorner();
                    RemoveImageAdorner();
                    AddSubtitleAdorner(visual, SelectedSubtitle.Style);
                }
                if (_isTimelinePlaying) PauseTimelinePlayback();
            }
            else
            {
                RemoveVideoAdorner();
                RemoveImageAdorner();
                RemoveSubtitleAdorner();
            }
            UpdateEditorPanelVisibility();
        }
        private void RemoveSubtitleAdorner()
        {
            if (_subtitleAdorner != null && _adornerLayer != null)
            {
                _subtitleAdorner.DragDelta -= Adorner_DragDelta;
                _subtitleAdorner.UniformScaleDelta -= Adorner_UniformScaleDelta;
                _subtitleAdorner.Resized -= Adorner_Resized;
                _subtitleAdorner.DragCompleted -= Adorner_DragCompleted;
                _subtitleAdorner.SizeCommitted -= Adorner_SizeCommitted;
                _subtitleAdorner.ResizeStarted -= Adorner_ResizeStarted;
                _adornerLayer.Remove(_subtitleAdorner);
                _subtitleAdorner = null;
                _adornerLayer = null;
            }
        }
        private void Adorner_ResizeStarted(ResizeDirection dir)
        {
            if (_selectedSubtitle == null || !_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual)) return;

            var canvas = currentVisual.Parent as Canvas;
            if (canvas == null || canvas.ActualWidth <= 0) return;

            _resizeCanvasWidth = canvas.ActualWidth;
            var style = _selectedSubtitle.Style;

            currentVisual.UpdateLayout();

            double unscaledTotalWidth = currentVisual.RenderSize.Width;
            double scaledTotalWidth = unscaledTotalWidth * Math.Max(0.001, style.ScaleX);

            double centerPx = style.X * _resizeCanvasWidth;
            _edgeLeftPx = centerPx - scaledTotalWidth / 2.0;
            _edgeRightPx = centerPx + scaledTotalWidth / 2.0;

            _isEdgeResizeActive = true;
            _isAdornerDragging = true;
        }
        private TranslateTransform GetOrCreateTranslateTransform(FrameworkElement element)
        {
            var transformGroup = element.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                transformGroup.Children.Add(element.RenderTransform);
                element.RenderTransform = transformGroup;
            }

            var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (translateTransform == null)
            {
                translateTransform = new TranslateTransform();
                transformGroup.Children.Add(translateTransform);
            }
            return translateTransform;
        }
        private void Adorner_DragDelta(double horizontalChange, double verticalChange)
        {
            if (_selectedSubtitle == null || !_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual))
            {
                return;
            }

            if (!_isAdornerDragging)
            {
                _isAdornerDragging = true;
                _isPreparingToDragClip = false;
                _currentDragMode = DragMode.None;
            }
            var transform = GetOrCreateTranslateTransform(currentVisual);
            transform.X += horizontalChange;
            transform.Y += verticalChange;
            _subtitleAdorner?.InvalidateArrange();
        }
        private void Adorner_UniformScaleDelta(double scaleDelta)
        {
            if (_selectedSubtitle == null) return;

            _isAdornerDragging = true;
            _isPreparingToDragClip = false;
            _currentDragMode = DragMode.None;

            var style = _selectedSubtitle.Style;
            style.ScaleX = Math.Max(0.1, style.ScaleX + scaleDelta);
            style.ScaleY = Math.Max(0.1, style.ScaleY + scaleDelta);
            if (!_selectedSubtitle.IsTextClip)
            {
                _currentProject.TemplateScaleX = style.ScaleX;
                _currentProject.TemplateScaleY = style.ScaleY;
                foreach (var sub in _currentProject.Subtitles.Where(s => !s.IsTextClip && s != _selectedSubtitle))
                {
                    if (sub.Style == null)
                    {
                        sub.Style = _currentProject.GetTemplateAsStyleState();
                    }
                    sub.Style.ScaleX = style.ScaleX;
                    sub.Style.ScaleY = style.ScaleY;
                }
            }
            if (_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual))
            {
                ApplyStyleToVisual(currentVisual, style);
            }
        }
        private void AlignmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUiFromCode) return;

            var clickedButton = sender as ToggleButton;
            if (clickedButton == null) return;

            var buttons = new List<ToggleButton> { AlignLeftButton, AlignCenterButton, AlignRightButton, AlignJustifyButton };
            if (clickedButton.IsChecked == false)
            {
                foreach (var button in buttons)
                {
                    if (button != clickedButton)
                    {
                        button.IsChecked = false;
                    }
                }
            }
            else
            {
                foreach (var button in buttons)
                {
                    if (button != clickedButton)
                    {
                        button.IsChecked = false;
                    }
                }
            }
            EditorControl_ValueChanged(sender, e);
        }
        private void Adorner_Resized(object sender, ResizeEventArgs e)
        {
            if (_selectedSubtitle == null || !_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual)) return;

            var canvas = currentVisual.Parent as Canvas;
            if (canvas == null || canvas.ActualWidth <= 0) return;

            if (!_isEdgeResizeActive)
            {
                Adorner_ResizeStarted(e.Direction);
            }
            _isAdornerDragging = true;
            _isPreparingToDragClip = false;
            _currentDragMode = DragMode.None;

            const double MIN_ADORNER_W_PX = 20.0;

            if (e.Direction == ResizeDirection.Left)
            {
                _edgeLeftPx += e.HorizontalChange;
            }
            else
            {
                _edgeRightPx += e.HorizontalChange;
            }

            _edgeLeftPx = Math.Max(0, _edgeLeftPx);
            _edgeRightPx = Math.Min(_resizeCanvasWidth, _edgeRightPx);

            if (_edgeRightPx - _edgeLeftPx < MIN_ADORNER_W_PX)
            {
                if (e.Direction == ResizeDirection.Left)
                    _edgeLeftPx = _edgeRightPx - MIN_ADORNER_W_PX;
                else
                    _edgeRightPx = _edgeLeftPx + MIN_ADORNER_W_PX;
            }
            double newScaledTotalWidth = _edgeRightPx - _edgeLeftPx;
            double newCenterPx = _edgeLeftPx + newScaledTotalWidth / 2.0;
            var style = _selectedSubtitle.Style;
            double newTotalLogicalWidth = newScaledTotalWidth / Math.Max(0.001, style.ScaleX);
            double logicalPaddingX = (style.IsBackgroundEnabled) ? style.BackgroundPaddingX : 0;
            const double SUBTITLE_REFERENCE_HEIGHT = 720.0;
            double playerCanvasActualHeight = SubtitleRenderCanvas.ActualHeight > 0 ? SubtitleRenderCanvas.ActualHeight : (videoGrid.ActualHeight > 0 ? videoGrid.ActualHeight : SUBTITLE_REFERENCE_HEIGHT);
            double scaleRatio = playerCanvasActualHeight / SUBTITLE_REFERENCE_HEIGHT;
            double newContentWidth = Math.Max(0, (newTotalLogicalWidth / scaleRatio) - (logicalPaddingX * 2));
            style.X = newCenterPx / _resizeCanvasWidth;
            style.Width = newContentWidth;
            ApplyStyleToVisual(currentVisual, style);
        }
        private void Adorner_SizeCommitted(double committedActualWidthPx)
        {
            try
            {
                if (_selectedSubtitle == null) return;
                var style = _selectedSubtitle.Style ?? _currentProject?.GetTemplateAsStyleState();
                if (style == null) return;
                EnsureInitialTemplateWidthFitsVideo();
                ComputeAndStoreManualAssOverrideForResize(_selectedSubtitle, style, committedActualWidthPx);
                if (!_selectedSubtitle.IsTextClip)
                {
                    var newTemplate = _currentProject.GetTemplateAsStyleState() ?? new StyleState();
                    newTemplate.X = style.X;
                    newTemplate.Y = style.Y;
                    newTemplate.ScaleX = style.ScaleX;
                    newTemplate.ScaleY = style.ScaleY;
                    newTemplate.Rotation = style.Rotation;


                    ApplyTemplateToAllSubtitlesAndUpdate(newTemplate, _selectedSubtitle);
                }

                if (_activeVisuals.TryGetValue(_selectedSubtitle, out var visual) && visual != null)
                {
                    ApplyStyleToVisual(visual, style, preserveTranslate: true, skipPositionUpdate: false);
                }

                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        private void DisplaySubtitleOnPlayer(SrtSubtitleLine subtitle)
        {
            if (subtitle == null)
            {
                return;
            }
            if (_activeVisuals.TryGetValue(subtitle, out var existingVisual))
            {
                existingVisual.SizeChanged -= SubtitleVisual_SizeChanged;
                SubtitleRenderCanvas.Children.Remove(existingVisual);
                _activeVisuals.Remove(subtitle);
            }
            var newVisual = new Border { Background = Brushes.Transparent, Padding = new Thickness(0) };
            newVisual.MouseLeftButtonDown += SubtitleVisual_MouseLeftButtonDown;
            newVisual.DataContext = subtitle;

            var backgroundBorder = new Border();
            newVisual.Child = backgroundBorder;

            bool isCurrentlySelected = _selectedSubtitle != null && _selectedSubtitle == subtitle;
            bool isEditing = _isTextInEditMode && isCurrentlySelected;
            double minDimension = (subtitle.Style?.FontSize ?? _currentProject.TemplateFontSize) * 1.2;

            bool useTranslated = CurrentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated && !string.IsNullOrWhiteSpace(subtitle.TranslatedText);
            string textToDisplay = useTranslated ? subtitle.TranslatedText : subtitle.OriginalText;

            if (isEditing)
            {
                var textBox = new TextBox
                {
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.LawnGreen
                };
                string propertyToBind = "OriginalText";
                if (CurrentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated)
                {
                    propertyToBind = "TranslatedText";
                }

                var binding = new Binding(propertyToBind)
                {
                    Source = subtitle,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Converter = (IValueConverter)FindResource("NewlineConverter")
                };

                textBox.SetBinding(TextBox.TextProperty, binding);
                backgroundBorder.Child = textBox;
                Dispatcher.BeginInvoke(new Action(() => { textBox.Focus(); textBox.Select(textBox.Text.Length, 0); }), DispatcherPriority.Input);
            }
            else
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tb.Text = (textToDisplay ?? string.Empty).Replace(@"\N", Environment.NewLine);
                backgroundBorder.Child = tb;
            }
            SubtitleRenderCanvas.Children.Add(newVisual);
            Panel.SetZIndex(newVisual, Z_ORDER_SUBTITLE);
            ApplyZOrderForLayers();
            _activeVisuals[subtitle] = newVisual;
            newVisual.SizeChanged += SubtitleVisual_SizeChanged;
            ApplyStyleToVisual(newVisual, subtitle.Style);
            if (isCurrentlySelected)
            {
                AddSubtitleAdorner(newVisual, subtitle.Style);
            }
        }
        private void SubtitleVisual_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var visual = sender as FrameworkElement;
            if (visual == null) return;

            var subtitle = visual.DataContext as SrtSubtitleLine;

            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && VisualTreeHelper.GetParent(visual) != null)
            {
                visual.SizeChanged -= SubtitleVisual_SizeChanged;

                if (subtitle != null)
                {
                    UpdateVisualPosition(visual, subtitle.Style);
                }
            }
        }
        private void SubtitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!textBox.IsKeyboardFocused)
                    {
                        textBox.Focus();
                    }
                }), DispatcherPriority.Input);
            }
        }

        private void UpdateSubtitleForCurrentTime(TimeSpan timelineTime)
        {
            if (!TimelineClips.Any(c => c.IsVideo))
            {
                return;
            }
            List<SrtSubtitleLine> linesToShow;
            if (_currentSmartCutMode == SmartCutMode.StaticReview
                && _smartCutSubtitleTimeline != null
                && _smartCutSubtitleTimeline.Count > 0)
            {
                linesToShow = _smartCutSubtitleTimeline
                    .Where(m => timelineTime >= m.FinalStart && timelineTime < m.FinalEnd)
                    .Select(m => m.Line)
                    .Distinct()
                    .ToList();
            }
            else
            {
                var allSubtitleAndTextLines = SrtSubtitleLinesView.Concat(_currentProject.TextClips).Distinct().ToList();
                linesToShow = allSubtitleAndTextLines
                    .Where(s => timelineTime >= s.StartTime && timelineTime < s.EndTime)
                    .ToList();
            }

            var visualsToRemove = _activeVisuals.Keys
                .Where(line => !linesToShow.Contains(line))
                .ToList();

            foreach (var line in visualsToRemove)
            {
                var visual = _activeVisuals[line];
                SubtitleRenderCanvas.Children.Remove(visual);
                _activeVisuals.Remove(line);
                if (_selectedSubtitle == line)
                {
                    RemoveSubtitleAdorner();
                }
            }

            foreach (var line in linesToShow)
            {
                if (!_activeVisuals.ContainsKey(line))
                {
                    DisplaySubtitleOnPlayer(line);
                }
            }
        }
        private void UpdateSubtitleForCurrentTime()
        {
            UpdateSubtitleForCurrentTime(_playhead);
        }
        private void SubtitleVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isTimelinePlaying) PauseTimelinePlayback();

            if (sender is FrameworkElement visual && visual.DataContext is SrtSubtitleLine line)
            {
                var clipVM = TimelineClips.FirstOrDefault(c => c.SourceData == line);
                if (clipVM != null)
                {
                    SwitchToClip(clipVM);
                }
            }
            e.Handled = true;
        }
        private System.Windows.Rect GetActualVideoContentRect()
        {
            if (videoGrid == null || VideoContainerBorder == null || !videoGrid.IsMeasureValid || videoGrid.ActualWidth <= 0 || videoGrid.ActualHeight <= 0)
            {
                return new System.Windows.Rect(0, 0, overlayCanvas.ActualWidth, overlayCanvas.ActualHeight);
            }
            try
            {
                GeneralTransform transform = videoGrid.TransformToAncestor(VideoContainerBorder);
                return transform.TransformBounds(new System.Windows.Rect(0, 0, videoGrid.ActualWidth, videoGrid.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                return new System.Windows.Rect(0, 0, overlayCanvas.ActualWidth, overlayCanvas.ActualHeight);
            }
        }
        private void UpdateCropThumbsLayout()
        {
            System.Windows.Rect videoRect = GetActualVideoContentRect();
            if (overlayCanvas.ActualWidth <= 0 || overlayCanvas.ActualHeight <= 0 || videoRect.IsEmpty)
            {
                topThumb.Visibility = Visibility.Collapsed;
                bottomThumb.Visibility = Visibility.Collapsed;
                leftThumb.Visibility = Visibility.Collapsed;
                rightThumb.Visibility = Visibility.Collapsed;
                return;
            }
            topThumb.Visibility = Visibility.Visible;
            bottomThumb.Visibility = Visibility.Visible;
            leftThumb.Visibility = Visibility.Visible;
            rightThumb.Visibility = Visibility.Visible;
            topThumb.Width = videoRect.Width;
            bottomThumb.Width = videoRect.Width;
            leftThumb.Height = videoRect.Height;
            rightThumb.Height = videoRect.Height;
            Canvas.SetLeft(topThumb, videoRect.X);
            Canvas.SetTop(topThumb, videoRect.Y + (videoRect.Height * _topPercent) - (topThumb.Height / 2));
            Canvas.SetLeft(bottomThumb, videoRect.X);
            Canvas.SetTop(bottomThumb, videoRect.Y + (videoRect.Height * _bottomPercent) - (bottomThumb.Height / 2));
            Canvas.SetLeft(leftThumb, videoRect.X + (videoRect.Width * _leftPercent) - (leftThumb.Width / 2));
            Canvas.SetTop(leftThumb, videoRect.Y);
            Canvas.SetLeft(rightThumb, videoRect.X + (videoRect.Width * _rightPercent) - (rightThumb.Width / 2));
            Canvas.SetTop(rightThumb, videoRect.Y);
        }
        private void AddSubtitleAdorner(FrameworkElement visual, StyleState style)
        {
            RemoveVideoAdorner();
            RemoveImageAdorner();
            RemoveSubtitleAdorner();
            if (visual == null)
            {
                return;
            }

            _adornerLayer = AdornerLayer.GetAdornerLayer(visual);
            if (_adornerLayer != null)
            {
                _subtitleAdorner = new SubtitleAdorner(visual, this);
                _subtitleAdorner.DragDelta += Adorner_DragDelta;
                _subtitleAdorner.UniformScaleDelta += Adorner_UniformScaleDelta;
                _subtitleAdorner.RotationChanged += Adorner_RotationChanged;
                _subtitleAdorner.Resized += Adorner_Resized;
                _subtitleAdorner.DragCompleted += Adorner_DragCompleted;
                _subtitleAdorner.SizeCommitted += Adorner_SizeCommitted;
                _subtitleAdorner.ResizeStarted += Adorner_ResizeStarted;
                _subtitleAdorner.RotationStarted += SubtitleAdorner_RotationStarted;
                _subtitleAdorner.RotationCompleted += SubtitleAdorner_RotationCompleted;
                _adornerLayer.Add(_subtitleAdorner);
                visual.UpdateLayout();
                double widthPx = visual.ActualWidth;
                double padLR = 0;
                if (visual is Border outer && outer.Child is Border inner)
                {
                    inner.UpdateLayout();
                    padLR = inner.Padding.Left + inner.Padding.Right;
                }
                if (style != null && style.AllowAutoWrap && style.FixedTextBoxWidth <= 1 && widthPx > 1)
                {
                    style.FixedTextBoxWidth = Math.Max(1, widthPx - padLR);
                    SaveProjectCurrent();
                }
            }
            else { }
        }
        private void Adorner_RotationChanged(double newAngle)
        {
            if (_isAdornerRotating && _activeRotateTransform != null)
            {
                _activeRotateTransform.Angle = newAngle;
                ShowAngleDisplay(newAngle);
            }
        }
        private void UpdateVisualPosition(FrameworkElement visual, StyleState style)
        {
            if (visual == null || style == null) return;

            var canvas = visual.Parent as Canvas;
            if (canvas == null || canvas.ActualWidth == 0 || canvas.ActualHeight == 0)
            {
                return;
            }
            visual.UpdateLayout();
            double actualRenderedWidth = visual.RenderSize.Width;
            double actualRenderedHeight = visual.RenderSize.Height;

            if (actualRenderedWidth == 0 && visual.MinWidth > 0)
            {
                actualRenderedWidth = visual.MinWidth;
            }
            if (actualRenderedHeight == 0 && visual.MinHeight > 0)
            {
                actualRenderedHeight = visual.MinHeight;
            }

            if (actualRenderedWidth == 0 || actualRenderedHeight == 0)
            {
            }

            double left = (canvas.ActualWidth * style.X) - (actualRenderedWidth / 2.0);
            double top = (canvas.ActualHeight * style.Y) - (actualRenderedHeight / 2.0);
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, top);
        }
        private void Adorner_DragCompleted()
        {
            if (_isAdornerRotating)
            {
                return;
            }

            if (!_isAdornerDragging)
            {
                return;
            }

            if (_selectedSubtitle == null || !_activeVisuals.TryGetValue(_selectedSubtitle, out var currentVisual))
            {
                _isAdornerDragging = false;
                _isEdgeResizeActive = false;
                return;
            }

            var transform = GetOrCreateTranslateTransform(currentVisual);
            double totalDeltaX = transform.X;
            double totalDeltaY = transform.Y;
            var canvas = currentVisual.Parent as Canvas;

            if (canvas != null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0 && (Math.Abs(totalDeltaX) > 0.01 || Math.Abs(totalDeltaY) > 0.01))
            {
                var style = _selectedSubtitle.Style;
                style.X += totalDeltaX / canvas.ActualWidth;
                style.Y += totalDeltaY / canvas.ActualHeight;

                transform.X = 0;
                transform.Y = 0;
                UpdateVisualPosition(currentVisual, style);
                if (!_selectedSubtitle.IsTextClip)
                {
                    _currentProject.TemplateX = style.X;
                    _currentProject.TemplateY = style.Y;
                    var updatedTemplate = _currentProject.GetTemplateAsStyleState();
                    ApplyTemplateToAllSubtitlesAndUpdate(updatedTemplate, null);
                }
            }

            _isAdornerDragging = false;
            _isEdgeResizeActive = false;
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }

        private void ApplyTextStylesToControl(FrameworkElement control, StyleState style, double wpfFontSizeCorrectionFactor, double scaledFontSize)
        {
            double correctedFontSize = scaledFontSize * wpfFontSizeCorrectionFactor;

            var fontFamily = new System.Windows.Media.FontFamily(style.FontFamilyName);
            var fontWeight = FontWeight.FromOpenTypeWeight(style.FontWeightValue);
            var fontStyle = style.IsItalic ? FontStyles.Italic : FontStyles.Normal;
            Color fontBaseColor;
            try { fontBaseColor = (Color)ColorConverter.ConvertFromString(style.FontColorHex); }
            catch { fontBaseColor = Colors.White; }
            byte finalFontAlpha = (byte)(fontBaseColor.A * style.Opacity);
            Color finalFontColor = Color.FromArgb(finalFontAlpha, fontBaseColor.R, fontBaseColor.G, fontBaseColor.B);
            Brush foregroundBrush = new SolidColorBrush(finalFontColor);
            foregroundBrush.Freeze();
            var textAlignment = TextAlignment.Left;
            switch (style.Alignment)
            {
                case 1: textAlignment = TextAlignment.Left; break;
                case 2: textAlignment = TextAlignment.Center; break;
                case 3: textAlignment = TextAlignment.Right; break;
                case 4: textAlignment = TextAlignment.Justify; break;
            }
            if (control is TextBlock tb)
            {
                tb.FontFamily = fontFamily;
                tb.FontSize = correctedFontSize;
                tb.FontWeight = fontWeight;
                tb.FontStyle = fontStyle;
                tb.Foreground = foregroundBrush;
                tb.TextDecorations = style.IsUnderlined ? TextDecorations.Underline : null;
                tb.TextAlignment = textAlignment;
            }
            else if (control is TextBox tbox)
            {
                tbox.FontFamily = fontFamily;
                tbox.FontSize = correctedFontSize;
                tbox.FontWeight = fontWeight;
                tbox.FontStyle = fontStyle;
                tbox.Foreground = foregroundBrush;
                tbox.TextAlignment = textAlignment;
            }
            control.Effect = null;
            switch (style.EdgeStyle)
            {
                case TextEdgeStyle.Outline:
                    var outlineEffect = new DropShadowEffect
                    {
                        ShadowDepth = 0,
                        BlurRadius = style.OutlineThickness,

                        Opacity = 1
                    };
                    try
                    {
                        Color outlineBaseColor = (Color)ColorConverter.ConvertFromString(style.OutlineColorHex);
                        byte finalOutlineAlpha = (byte)(outlineBaseColor.A * style.Opacity);
                        outlineEffect.Color = Color.FromArgb(finalOutlineAlpha, outlineBaseColor.R, outlineBaseColor.G, outlineBaseColor.B);
                    }
                    catch { outlineEffect.Color = Colors.Black; }
                    control.Effect = outlineEffect;
                    break;

                case TextEdgeStyle.Shadow:
                    var shadowEffect = new DropShadowEffect
                    {
                        BlurRadius = style.ShadowBlur,
                        ShadowDepth = style.ShadowDepth,
                        Direction = style.ShadowDirection
                    };
                    try
                    {
                        Color shadowBaseColor = (Color)ColorConverter.ConvertFromString(style.ShadowColorHex);
                        shadowEffect.Opacity = (shadowBaseColor.A / 255.0) * style.Opacity;
                        shadowEffect.Color = Color.FromRgb(shadowBaseColor.R, shadowBaseColor.G, shadowBaseColor.B);
                    }
                    catch { shadowEffect.Color = Colors.Black; }
                    control.Effect = shadowEffect;
                    break;
            }
        }
        private double ComputeEffectiveWrapWidthPx(StyleState style, double pxPerLogicalUnit)
        {
            return ComputeEffectiveWrapWidthPx(style, pxPerLogicalUnit, null);
        }

        private void ApplyStyleToVisual(FrameworkElement visual, StyleState style, bool preserveTranslate = true, bool skipPositionUpdate = false)
        {
            if (visual == null || style == null)
            {
                return;
            }
            if (visual is not Border outer || outer.Child is not Border inner || inner.Child is not FrameworkElement text) return;
            outer.Opacity = style.Opacity;
            const double REF_H = 720.0;
            double playerH = SubtitleRenderCanvas.ActualHeight > 0 ? SubtitleRenderCanvas.ActualHeight
                              : (videoGrid.ActualHeight > 0 ? videoGrid.ActualHeight : REF_H);
            double scaleRatio = playerH / REF_H;
            double finalFontSize = style.FontSize * scaleRatio;
            ApplyTextStylesToControl(text, style, WPF_FONT_SIZE_CORRECTION_FACTOR, finalFontSize);
            double minDim = Math.Max(0, finalFontSize * 1.2);
            if (text is TextBlock tb)
            {
                tb.MinWidth = minDim; tb.MinHeight = minDim;
                tb.VerticalAlignment = VerticalAlignment.Center;
                tb.HorizontalAlignment = HorizontalAlignment.Center;
            }
            else if (text is TextBox tbox)
            {
                tbox.MinWidth = minDim; tbox.MinHeight = minDim;
                tbox.VerticalContentAlignment = VerticalAlignment.Center;
            }

            if (style.IsBackgroundEnabled)
            {
                try { inner.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(style.BackgroundColorHex); }
                catch { inner.Background = Brushes.Transparent; }
                inner.CornerRadius = new CornerRadius(style.BackgroundCornerRadius * scaleRatio);
                inner.Padding = new Thickness(
                    style.BackgroundPaddingX * scaleRatio,
                    style.BackgroundPaddingY * scaleRatio,
                    style.BackgroundPaddingX * scaleRatio,
                    style.BackgroundPaddingY * scaleRatio
                );
            }
            else
            {
                inner.Background = Brushes.Transparent;
                inner.Padding = new Thickness(0);
                inner.CornerRadius = new CornerRadius(0);
            }

            inner.HorizontalAlignment = HorizontalAlignment.Center;
            inner.VerticalAlignment = VerticalAlignment.Stretch;

            if (style.Width is double w && w > 0)
            {
                double logicalPadX = style.IsBackgroundEnabled ? style.BackgroundPaddingX : 0;
                double totalLogicalWidth = w + (logicalPadX * 2);
                outer.Width = totalLogicalWidth * scaleRatio;
            }
            else
            {
                double autoWidth = ComputeEffectiveWrapWidthPx(style, scaleRatio);
                if (double.IsPositiveInfinity(autoWidth))
                {
                    outer.Width = double.NaN;
                }
                else
                {
                    double totalAutoWidth = autoWidth;
                    if (style.IsBackgroundEnabled)
                    {
                        totalAutoWidth += (2.0 * style.BackgroundPaddingX * scaleRatio);
                    }
                    outer.Width = totalAutoWidth;
                }
            }
            TransformGroup tg = visual.RenderTransform as TransformGroup ?? new TransformGroup();
            var scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
            var rotate = tg.Children.OfType<RotateTransform>().FirstOrDefault();
            var translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault();

            if (scale == null) { scale = new ScaleTransform(); tg.Children.Insert(0, scale); }
            if (rotate == null)
            {
                int idx = Math.Min(1, tg.Children.Count);
                rotate = new RotateTransform();
                tg.Children.Insert(idx, rotate);
            }
            if (preserveTranslate && translate == null)
            {
                tg.Children.Add(new TranslateTransform());
                translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
            }
            scale.ScaleX = Math.Max(0.0001, style.ScaleX);
            scale.ScaleY = Math.Max(0.0001, style.ScaleY);
            rotate.Angle = style.Rotation;
            visual.RenderTransform = tg;
            visual.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            text.InvalidateMeasure();
            inner.InvalidateMeasure();
            outer.InvalidateMeasure();
            outer.UpdateLayout();
            _subtitleAdorner?.InvalidateArrange();
            _subtitleAdorner?.InvalidateVisual();

            if (!skipPositionUpdate && visual.IsLoaded && visual.ActualWidth > 0 && visual.ActualHeight > 0)
            {
                UpdateVisualPosition(visual, style);
            }
        }
        private void RecalculateTrackAssignments()
        {
        }

        private double GetHeightForType(TimelineClipType type)
        {
            switch (type)
            {
                case TimelineClipType.Video:
                    return TIMELINE_TRACK_HEIGHT;
                case TimelineClipType.Image:
                    return 50;
                case TimelineClipType.Audio:
                    return TIMELINE_AUDIO_TRACK_HEIGHT;
                case TimelineClipType.Subtitle:
                case TimelineClipType.Text:
                    return 20;
                default:
                    return 60;
            }
        }
        private void RenderTimeline(double pixelsPerSecond)
        {
            if (pixelsPerSecond <= 0) return;
            var neededWidth = Math.Max(_totalTimelineDuration.TotalSeconds * pixelsPerSecond, TimelineScrollViewer.ViewportWidth);
            if (double.IsNaN(TracksContainerGrid.Width) || Math.Abs(TracksContainerGrid.Width - neededWidth) > 0.5)
            {
                TracksContainerGrid.Width = neededWidth;
            }
            var imageTrackIndices = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Image)
                .Select(c => c.TrackIndex).Distinct().ToList();
            if (!imageTrackIndices.Any()) imageTrackIndices.Add(-1);
            imageTrackIndices.Sort();

            var subtitleTrackIndices = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Subtitle || c.ClipType == TimelineClipType.Text)
                .Select(c => c.TrackIndex).Distinct().ToList();
            if (!subtitleTrackIndices.Any()) subtitleTrackIndices.Add(-2);
            subtitleTrackIndices.Sort();

            var audioTrackIndices = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Audio)
                .Select(c => c.TrackIndex).Distinct().ToList();
            if (!audioTrackIndices.Any()) audioTrackIndices.Add(1);
            audioTrackIndices.Sort();
            var allTrackLayouts = new List<TrackBackgroundViewModel>();
            foreach (var trackIndex in imageTrackIndices) allTrackLayouts.Add(new TrackBackgroundViewModel { TrackIndex = trackIndex, TrackType = TimelineClipType.Image, Height = GetHeightForType(TimelineClipType.Image) });
            foreach (var trackIndex in subtitleTrackIndices) allTrackLayouts.Add(new TrackBackgroundViewModel { TrackIndex = trackIndex, TrackType = TimelineClipType.Subtitle, Height = GetHeightForType(TimelineClipType.Subtitle) });
            allTrackLayouts.Add(new TrackBackgroundViewModel { TrackIndex = 0, TrackType = TimelineClipType.Video, Height = GetHeightForType(TimelineClipType.Video) });
            foreach (var trackIndex in audioTrackIndices) allTrackLayouts.Add(new TrackBackgroundViewModel { TrackIndex = trackIndex, TrackType = TimelineClipType.Audio, Height = GetHeightForType(TimelineClipType.Audio) });

            var trackPositions = new Dictionary<int, double>();
            double currentY = 0;
            foreach (var layoutItem in allTrackLayouts)
            {
                trackPositions[layoutItem.TrackIndex] = currentY;
                currentY += layoutItem.Height + TIMELINE_TRACK_SPACING;
            }
            foreach (var vm in TimelineClips)
            {
                if (trackPositions.TryGetValue(vm.TrackIndex, out double yPos))
                {
                    vm.Y = yPos;
                }
                else
                {
                    vm.Y = 0;
                }
                vm.Height = GetHeightForType(vm.ClipType);
            }
            var newHeight = Math.Max(currentY, 250);
            if (Math.Abs(TracksContainerGrid.Height - newHeight) > 0.5)
            {
                TracksContainerGrid.Height = newHeight;
            }
            TrackBackgroundsControl.ItemsSource = allTrackLayouts;
            UpdateVisibleClips();
            RenderVisibleClipsOnly();
        }
        private void RenderVisibleClipsOnly()
        {
            double pps = this._pixelsPerSecond;
            if (pps <= 0) return;

            bool staticSmartCutOn =
                _currentSmartCutMode == SmartCutMode.StaticReview &&
                _smartCutSegmentMaps != null &&
                _smartCutSegmentMaps.Count > 0;

            foreach (var vm in VisibleTimelineClips)
            {
                if (vm == null) continue;
                TimeSpan drawStart = vm.StartTime;
                TimeSpan drawDuration = vm.Duration;

                if (staticSmartCutOn)
                {
                    if (vm.SourceData is TimelineAudioClip tts && tts.IsTts)
                    {
                        drawStart = MapSourceTimeToSmartCut(tts.StartTime);
                        drawDuration = tts.EffectiveDuration;
                        vm.SetRenderTimes(drawStart, drawDuration);
                    }
                    else
                    {
                        TimeSpan srcStart = vm.StartTime;
                        TimeSpan srcEnd = vm.EndTime;
                        TimeSpan mappedStart = MapSourceTimeToSmartCut(srcStart);
                        TimeSpan mappedEnd = MapSourceTimeToSmartCut(srcEnd);
                        if (mappedEnd < mappedStart) mappedEnd = mappedStart;
                        drawStart = mappedStart;
                        drawDuration = mappedEnd - mappedStart;
                        vm.SetRenderTimes(drawStart, drawDuration);
                    }
                }
                else
                {
                    vm.SetRenderTimes(TimeSpan.Zero, TimeSpan.Zero);
                }

                double x = drawStart.TotalSeconds * pps;
                double w = Math.Max(1.0, drawDuration.TotalSeconds * pps);
                vm.X = x;
                vm.Width = w;
            }
        }
        private string _staticReviewCacheJson;

        private class StaticReviewCacheData
        {
            public double SlowedFactor { get; set; }
            public long FinalDurationTicks { get; set; }
            public List<StaticReviewSegmentDto> Segments { get; set; }
        }
        private class StaticReviewSegmentDto
        {
            public long SourceStartTicks { get; set; }
            public long SourceEndTicks { get; set; }
            public long FinalStartTicks { get; set; }
            public long FinalEndTicks { get; set; }
            public double Scale { get; set; }
            public bool IsGap { get; set; }
        }

        private void SaveStaticReviewCacheJson(double slowedFactor, TimeSpan finalDuration)
        {
            try
            {
                if (_smartCutSegmentMaps == null || _smartCutSegmentMaps.Count == 0)
                {
                    _staticReviewCacheJson = null;
                    return;
                }
                var dto = new StaticReviewCacheData
                {
                    SlowedFactor = slowedFactor,
                    FinalDurationTicks = finalDuration.Ticks,
                    Segments = _smartCutSegmentMaps.Select(m => new StaticReviewSegmentDto
                    {
                        SourceStartTicks = m.SourceStart.Ticks,
                        SourceEndTicks = m.SourceEnd.Ticks,
                        FinalStartTicks = m.FinalStart.Ticks,
                        FinalEndTicks = m.FinalEnd.Ticks,
                        Scale = m.Scale,
                        IsGap = m.IsGap
                    }).ToList()
                };
                _staticReviewCacheJson = JsonConvert.SerializeObject(dto);
            }
            catch
            {
                _staticReviewCacheJson = null;
            }
        }

        private bool TryRestoreStaticReviewFromCache(double slowedFactor)
        {
            try
            {
                if (string.IsNullOrEmpty(_staticReviewCacheJson)) return false;
                var dto = JsonConvert.DeserializeObject<StaticReviewCacheData>(_staticReviewCacheJson);
                if (dto == null) return false;
                if (Math.Abs(dto.SlowedFactor - slowedFactor) > 1e-6) return false;
                _smartCutSegmentMaps = dto.Segments.Select(s => new SmartCutSegmentMap
                {
                    SourceStart = new TimeSpan(s.SourceStartTicks),
                    SourceEnd = new TimeSpan(s.SourceEndTicks),
                    FinalStart = new TimeSpan(s.FinalStartTicks),
                    FinalEnd = new TimeSpan(s.FinalEndTicks),
                    Scale = s.Scale,
                    IsGap = s.IsGap
                }).ToList();

                var finalDuration = new TimeSpan(dto.FinalDurationTicks);
                _totalTimelineDuration = finalDuration;
                _actualContentDuration = finalDuration;
                return true;
            }
            catch
            {
                return false;
            }
        }
        private void VisibleTimelineClips_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (TimelineClipViewModel vm in e.NewItems)
                    PrepareClipOnEnterViewport(vm);
            }
            if (e.OldItems != null)
            {
                foreach (TimelineClipViewModel vm in e.OldItems)
                    CancelHeavyTasks(vm);
            }
        }

        private void PrepareClipOnEnterViewport(TimelineClipViewModel vm)
        {
            if ((vm.ClipType == TimelineClipType.Video || vm.ClipType == TimelineClipType.Image) && !_preparedFilmstrip.Contains(vm))
            {
                _preparedFilmstrip.Add(vm);
                if (vm.ClipType == TimelineClipType.Video)
                    _ = GenerateAndDisplayFilmstripAsync(vm);
                else
                    PopulateImageFilmstrip(vm);
            }
            if ((vm.ClipType == TimelineClipType.Audio || vm.ClipType == TimelineClipType.Video) && !_preparedWaveform.Contains(vm))
            {
                _preparedWaveform.Add(vm);
                vm.InvalidateWaveform();
            }
        }

        private void CancelHeavyTasks(TimelineClipViewModel vm)
        {
            if (_filmstripTasks == null) return;
            if (_filmstripTasks.TryRemove(vm, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch { }
                cts.Dispose();
            }
        }
        private void AddImageAdorner(MediaAsset asset)
        {
            RemoveImageAdorner();
            RemoveVideoAdorner();
            RemoveSubtitleAdorner();
            if (asset == null)
            {
                return;
            }

            var adornerLayer = AdornerLayer.GetAdornerLayer(PlayerAdornerDecorator);
            if (adornerLayer != null)
            {
                _imageAdorner = new ImageAdorner(PlayerAdornerDecorator, videoGrid, this.CurrentProject, this)
                {
                    DataContext = asset
                };
                _activeTransformingImageAsset = asset;

                _imageAdorner.DragDelta += ImageAdorner_DragDelta;
                _imageAdorner.ScaleChanged += ImageAdorner_ScaleChanged;
                _imageAdorner.RotationChanged += ImageAdorner_RotationChanged;
                _imageAdorner.RotationStarted += ImageAdorner_RotationStarted;
                _imageAdorner.RotationCompleted += ImageAdorner_RotationCompleted;
                _imageAdorner.DragCompleted += ImageAdorner_DragCompleted;

                adornerLayer.Add(_imageAdorner);
            }
            else { }
        }
        private void ImageAdorner_RotationChanged(double newAngle)
        {
            if (_activeTransformingImageAsset == null) return;
            _activeTransformingImageAsset.Rotation = newAngle;
            if (_activeImageOverlays != null && _activeImageOverlays.TryGetValue(_activeTransformingImageAsset, out var visual))
            {
                ApplyTransformToImageVisual(visual, _activeTransformingImageAsset);
            }
            _imageAdorner?.InvalidateArrange();
            _imageAdorner?.InvalidateVisual();
            ShowAngleDisplay(newAngle);
        }

        private void ImageAdorner_DragDelta(double horizontalChange, double verticalChange)
        {
            if (_activeTransformingImageAsset == null) return;
            double refW = Math.Max(1, this.CurrentProject.ProjectReferenceVideoWidth);
            double refH = Math.Max(1, this.CurrentProject.ProjectReferenceVideoHeight);
            double canvasW = Math.Max(1, SubtitleRenderCanvas.ActualWidth);
            double canvasH = Math.Max(1, SubtitleRenderCanvas.ActualHeight);
            double scaleToCanvas = Math.Min(canvasW / refW, canvasH / refH);
            double frameW = refW * scaleToCanvas;
            double frameH = refH * scaleToCanvas;
            double sx = Math.Max(_activeTransformingImageAsset.ScaleX, 0.001);
            double sy = Math.Max(_activeTransformingImageAsset.ScaleY, 0.001);
            double dx = horizontalChange / (frameW * sx);
            double dy = verticalChange / (frameH * sy);
            _activeTransformingImageAsset.PositionX += dx;
            _activeTransformingImageAsset.PositionY += dy;

            if (_activeImageOverlays != null
                && _activeImageOverlays.TryGetValue(_activeTransformingImageAsset, out var visual))
            {
                ApplyTransformToImageVisual(visual, _activeTransformingImageAsset);
            }
            _imageAdorner?.InvalidateArrange();
        }

        private void ImageAdorner_ScaleChanged(double newScale)
        {
            if (_activeTransformingImageAsset == null) return;

            _activeTransformingImageAsset.Scale = newScale;
            if (_activeImageOverlays != null && _activeImageOverlays.TryGetValue(_activeTransformingImageAsset, out var visual))
                ApplyTransformToImageVisual(visual, _activeTransformingImageAsset);

            _imageAdorner?.InvalidateArrange();
        }
        private void ImageAdorner_DragCompleted()
        {
        }
        private void RemoveImageAdorner()
        {
            if (_imageAdorner != null)
            {
                try { _imageAdorner.ReleaseMouseCapture(); } catch { }
                _imageAdorner.DragDelta -= ImageAdorner_DragDelta;
                _imageAdorner.ScaleChanged -= ImageAdorner_ScaleChanged;
                _imageAdorner.DragCompleted -= ImageAdorner_DragCompleted;

                var layer = AdornerLayer.GetAdornerLayer(PlayerAdornerDecorator);
                layer?.Remove(_imageAdorner);

                _imageAdorner = null;
                _activeTransformingImageAsset = null;
            }
        }
        private void UpdateUiForProcessing(bool isProcessing)
        {
            bool anyProcessing = isProcessing || _isAutoProcessing || IsSrtTranslating;

            AutomaticButton.IsEnabled = !anyProcessing;
            RunVSFButton.IsEnabled = !anyProcessing;
            StartButton.IsEnabled = !anyProcessing;
            RunWhisperButton.IsEnabled = !anyProcessing;
            TranslateSrtButton.IsEnabled = !anyProcessing;
            if (anyProcessing)
            {
                _isOverlayVisible = true;
                LoadingOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                _isOverlayVisible = false;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelProcessButton_Click(object sender, RoutedEventArgs e)
        {
            _masterCts?.Cancel();
        }
        private void VsfMode_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            if (VsfModeCleanRadio.IsChecked == true)
            {
                _selectedVsfProcessingMode = VsfProcessingMode.CleanAndCreateTxtImages;
            }
            else if (VsfModeSearchOnlyRadio.IsChecked == true)
            {
                _selectedVsfProcessingMode = VsfProcessingMode.SearchSubtitlesOnly;
            }
        }
        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            var newlyCheckedTab = sender as ToggleButton;
            if (newlyCheckedTab == null || newlyCheckedTab.IsChecked == false)
            {
                return;
            }

            var mainTabs = new List<ToggleButton> { MediaTab, SubtitleTab, TranslateTab, TtsTab };
            foreach (var tab in mainTabs)
            {
                if (tab != null && tab != newlyCheckedTab)
                {
                    if (tab.IsChecked == true)
                    {
                        tab.IsChecked = false;
                    }
                }
            }

            if (newlyCheckedTab == SubtitleTab)
            {
                await UpdateFeaturePermissionsAsync();
            }

            if (newlyCheckedTab == TtsTab)
            {
                if (TtsModeLongTextRadio.IsChecked == false && TtsModeVoiceSubRadio.IsChecked == false)
                {
                    TtsModeLongTextRadio.IsChecked = true;
                }
            }

            if (newlyCheckedTab != SubtitleTab && IsBatchSubtitleMode)
            {
                IsBatchSubtitleMode = false;
                _clipsForBatchSubtitle = null;
            }
            UpdateAdornerSuppression();
        }


        public async Task UpdateFeaturePermissionsAsync()
        {
            if (!App.User.IsLoggedIn)
            {
                RunVSFButton.IsEnabled = false;
                StartButton.IsEnabled = false;
                AutomaticButton.IsEnabled = false;
                RunWhisperButton.IsEnabled = false;
                return;
            }
            OcrSettingsPanel.IsEnabled = false;
            SpeechToTextSettingsPanel.IsEnabled = false;

            var (hasSubPhimAccess, subPhimMessage) = await ApiService.CheckFeatureAccessAsync("SubPhim");
            ModeOcrRadio.IsEnabled = hasSubPhimAccess;
            if (!hasSubPhimAccess)
            {
            }
            var (hasDichThuatAccess, dichThuatMessage) = await ApiService.CheckFeatureAccessAsync("DichThuat");
            ModeSpeechToTextRadio.IsEnabled = hasDichThuatAccess;
            if (!hasDichThuatAccess)
            {
            }

            OcrSettingsPanel.IsEnabled = true;
            SpeechToTextSettingsPanel.IsEnabled = true;

            ProcessingMode_Changed(null, null);

        }
        #region Video Loading, Player & Crop Thumbs Logic (Ported)

        private async void LoadVideoForCrop_Click(object sender, RoutedEventArgs e)
        {

            CustomMessageBox.Show(
                "Để bắt đầu, vui lòng sử dụng nút 'Nhập' trong tab 'Tệp' để thêm video của bạn vào Media Bin.\n\nSau đó, bạn có thể kéo video vào timeline hoặc chuột phải vào nó và chọn 'Tạo phụ đề'.",
                "Hướng dẫn",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

        }
        private void VideoContainerBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePlayerLayout();
            UpdateCropThumbsLayout();
            if (SubtitleRenderCanvas != null)
            {
                foreach (var visual in _activeVisuals.Values)
                {
                    if (visual.DataContext is SrtSubtitleLine line)
                    {
                        UpdateVisualPosition(visual, line.Style);
                    }
                }
            }
        }
        private (double w, double h) GetReferenceSurfaceSize()
        {
            var cp = CurrentProject;

            double rw = (cp != null && cp.ProjectReferenceVideoWidth > 0)
                ? cp.ProjectReferenceVideoWidth
                : 1920.0;

            double rh = (cp != null && cp.ProjectReferenceVideoHeight > 0)
                ? cp.ProjectReferenceVideoHeight
                : 1080.0;

            return (rw, rh);
        }
        private void ApplyReferenceSurfaceSize()
        {
            var (w, h) = GetReferenceSurfaceSize();
            videoGrid.Width = w; videoGrid.Height = h;
            overlayCanvas.Width = w; overlayCanvas.Height = h;
        }
        private async Task SetupProjectWithFirstVideoAsync(string videoPath)
        {
            ResetApplicationState(false);
            _playhead = TimeSpan.Zero;
            _currentVideoPath = videoPath;
            VideoEntry.Text = _currentVideoPath;

            try
            {
                IMediaAnalysis mediaInfo = await FFProbe.AnalyseAsync(_currentVideoPath);
                var videoStream = mediaInfo.PrimaryVideoStream;
                if (videoStream == null) throw new InvalidDataException("File không chứa luồng video hợp lệ.");

                _videoNativeWidth = videoStream.Width;
                _videoNativeHeight = videoStream.Height;
                ApplyReferenceSurfaceSize();
                UpdateCropThumbsLayout();
                var asset = new MediaAsset
                {
                    FilePath = videoPath,
                    Type = AssetType.Video,
                    Duration = mediaInfo.Duration,
                    Width = videoStream.Width,
                    Height = videoStream.Height
                };
                VideoImageAssets.Add(asset);
                var clipVM = new TimelineClipViewModel(asset);
                TimelineClips.Add(clipVM);
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                UpdatePlaybackUI(TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                ResetApplicationState(true);
            }
        }
        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            string errorMessage = e?.ErrorException?.Message ?? "Không rõ nguyên nhân.";
            CustomMessageBox.Show($"Không thể phát video này. Lỗi: {errorMessage}", "Lỗi Media", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetVideoPlayerState();
        }
        private async Task ResetVideoPlayerState()
        {
            await PauseTimelinePlayback();
            await FFMEPlayer.Close();
            _currentVideoPath = null;
            _videoNativeWidth = 0;
            _videoNativeHeight = 0;
            playPauseButton.Content = "\uE768";
            totalDurationTextBlock.Text = "00:00:00.000";
            currentTimeTextBlock.Text = "00:00:00.000";
            UpdatePlayerLayout();
        }
        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTimelinePlaying)
            {
                await PauseTimelinePlayback();
            }
            else
            {
                await StartTimelinePlayback();
            }
        }
        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!(sender is Thumb thumb)) return;
            System.Windows.Rect videoRect = GetActualVideoContentRect();
            if (videoRect.IsEmpty || videoRect.Width == 0 || videoRect.Height == 0) return;

            if (thumb.Cursor == Cursors.SizeNS)
            {
                double currentTop = Canvas.GetTop(thumb);
                double newTop = Math.Clamp(currentTop + e.VerticalChange, videoRect.Y, videoRect.Bottom - thumb.ActualHeight);
                Canvas.SetTop(thumb, newTop);
                double relativeY = (newTop + thumb.ActualHeight / 2) - videoRect.Y;
                double percent = Math.Clamp(relativeY / videoRect.Height, 0.0, 1.0);
                if (thumb.Name == "topThumb") _topPercent = percent; else _bottomPercent = percent;
            }
            else if (thumb.Cursor == Cursors.SizeWE)
            {
                double currentLeft = Canvas.GetLeft(thumb);
                double newLeft = Math.Clamp(currentLeft + e.HorizontalChange, videoRect.X, videoRect.Right - thumb.ActualWidth);
                Canvas.SetLeft(thumb, newLeft);
                double relativeX = (newLeft + thumb.ActualWidth / 2) - videoRect.X;
                double percent = Math.Clamp(relativeX / videoRect.Width, 0.0, 1.0);
                if (thumb.Name == "leftThumb") _leftPercent = percent; else _rightPercent = percent;
            }
            UpdateCropValues();
        }

        private void UpdateCropValues()
        {
            double validTopPercent = Math.Min(_topPercent, _bottomPercent);
            double validBottomPercent = Math.Max(_topPercent, _bottomPercent);
            double validLeftPercent = Math.Min(_leftPercent, _rightPercent);
            double validRightPercent = Math.Max(_leftPercent, _rightPercent);
            double top_video_image_percent_end = 1.0 - validBottomPercent;
            double bottom_video_image_percent_end = 1.0 - validTopPercent;
            _finalCropTop = Math.Min(top_video_image_percent_end, bottom_video_image_percent_end);
            _finalCropBottom = Math.Max(top_video_image_percent_end, bottom_video_image_percent_end);
            _finalCropLeft = validLeftPercent;
            _finalCropRight = validRightPercent;
            UpdateResultsText();
        }
        private void UpdateResultsText()
        {
            if (resultsTextBlock != null)
            {
                string output = $"Thông số OCR\nTop: {_finalCropTop:F6} | Bottom: {_finalCropBottom:F6} | Left: {_finalCropLeft:F6} | Right: {_finalCropRight:F6}";
                resultsTextBlock.Text = output;
            }
        }
        #endregion

        #region AIOSubPhim & Whisper Process Execution (Ported)

        private async void RunVSFButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckGoogleAccountsAndShowGuideAsync()) return;
            var (canProcess, message) = await ApiService.TryStartProcessingAsync();
            if (!canProcess)
            {
                CustomMessageBox.Show(message, "Đã đạt giới hạn", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!await EnsureVsfIsAvailableAsync()) return;

            _masterCts = new CancellationTokenSource();
            UpdateUiForProcessing(true);

            try
            {
                VsfService.VsfParameters vsfParams;
                var selectedVideoClips = TimelineClips.Where(c => c.IsSelected && c.ClipType == TimelineClipType.Video).ToList();

                if (selectedVideoClips.Count == 1)
                {
                    var clipVM = selectedVideoClips.First();
                    var mediaAsset = clipVM.SourceData as MediaAsset;
                    if (mediaAsset != null)
                    {
                        vsfParams = GetVsfParametersFromUi();
                        vsfParams.VideoPath = mediaAsset.FilePath;
                        vsfParams.StartTime = mediaAsset.TrimStartOffset.ToString(@"hh\:mm\:ss\:fff");
                        var effectiveDurationInSource = mediaAsset.Duration - mediaAsset.TrimStartOffset - mediaAsset.TrimEndOffset;
                        vsfParams.EndTime = (mediaAsset.TrimStartOffset + effectiveDurationInSource).ToString(@"hh\:mm\:ss\:fff");
                    }
                    else
                    {
                        vsfParams = GetVsfParametersFromUi();
                    }
                }
                else
                {
                    vsfParams = GetVsfParametersFromUi();
                }

                var (success, resultPath) = await _vsfService.RunVSFProcessAsync(vsfParams, _masterCts.Token);

                if (!success || _masterCts.IsCancellationRequested)
                {
                    if (!_masterCts.IsCancellationRequested) { }
                    return;
                }

                ImagesEntry.Text = resultPath;
                TranslateTab.IsChecked = true;

                Action<int, string> updateProgressAction = (percent, msg) =>
                {
                };

                bool ocrResult = await StartOcrProcessAsync(_masterCts.Token, updateProgressAction);

                if (ocrResult) { }
                else if (!_masterCts.IsCancellationRequested)
                {
                    CustomMessageBox.Show("Có lỗi xảy ra trong quá trình OCR. Vui lòng kiểm tra lại các dòng phụ đề.", "Lỗi OCR", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
            }
            finally
            {
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }
        private async void RunWhisperButton_Click(object sender, RoutedEventArgs e)
        {
            _masterCts = new CancellationTokenSource();
            try
            {
                await RunWhisperProcess(_masterCts.Token);
            }
            finally
            {
                _masterCts?.Dispose(); _masterCts = null;
            }
        }

        private async Task RunWhisperProcess(CancellationToken token)
        {
            string baseName = Path.GetFileNameWithoutExtension(VideoEntry.Text);
            string outputSrtPath = Path.Combine(GetResolvedSubtitleOutputDirectory(), $"{baseName}.srt");
            var selectedModelInfo = WhisperModelComboBox.SelectedItem as WhisperModelInfo;
            var modelName = selectedModelInfo?.Name.Replace("* ", "").Trim();

            var parameters = new WhisperService.WhisperParameters
            {
                Engine = WhisperEngineComboBox.SelectedItem.ToString(),
                VideoPath = VideoEntry.Text,
                ModelName = modelName,
                Language = WhisperLanguageComboBox.SelectedValue.ToString(),
                OutputSrtPath = outputSrtPath,
                Device = WhisperDeviceComboBox.SelectedItem.ToString(),
                ComputeType = WhisperComputeTypeComboBox.SelectedItem.ToString()
            };

            if (string.IsNullOrEmpty(parameters.ModelName))
            {
            }

            bool success = await _whisperService.RunAsync(parameters, token);
            if (success && !token.IsCancellationRequested)
            {
            }
            else if (!token.IsCancellationRequested)
            {
                CustomMessageBox.Show("Quá trình thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private VsfService.VsfParameters GetVsfParametersFromUi()
        {
            UpdateCropValues();
            string startTime = null, endTime = null;
            if (EnableStartTimeCheckBox.IsChecked == true)
            {
                startTime = GetFormattedTimeArgument(StartTimeH, StartTimeM, StartTimeS, StartTimeMS);
                if (EnableEndTimeCheckBox.IsChecked == true)
                {
                    endTime = GetFormattedTimeArgument(EndTimeH, EndTimeM, EndTimeS, EndTimeMS);
                }
            }
            return new VsfService.VsfParameters
            {
                VsfPath = _pathVSF,
                VideoPath = VideoEntry.Text,
                OutputBaseDirectory = GetResolvedVSFOutputDirectory(),
                OcrImagesFolderName = _ocrTextFolderName,
                CropTop = (float)_finalCropTop,
                CropBottom = (float)_finalCropBottom,
                CropLeft = (float)_finalCropLeft,
                CropRight = (float)_finalCropRight,
                ProcessingMode = _selectedVsfProcessingMode,
                OpenMethod = _selectedVsfOpenMethod,
                UseCuda = _useCuda,
                NumThreadsSearch = _vsfNumThreadsSearch,
                NumThreadsClean = _vsfNumThreadsClean,
                AdditionalArgs = _cmdVsfArgs,
                StartTime = startTime,
                EndTime = endTime
            };
        }

        private async Task<bool> EnsureVsfIsAvailableAsync()
        {
            if (File.Exists(_pathVSF)) return true;
            string downloadUrl = "https://github.com/visecal/qidian_vp_website/releases/download/VSF1.0/Subphim.zip";
            string appRootPath = AppDomain.CurrentDomain.BaseDirectory;
            string tempZipPath = Path.Combine(Path.GetTempPath(), "AIOSubPhim.zip");
            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, appRootPath, true);
            }
            catch (Exception ex)
            {
            }
            finally { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); }
            return File.Exists(_pathVSF);
        }

        #endregion

        #region UI Event Handlers & Helpers (Ported)

        private void ProcessingMode_Changed(object sender, RoutedEventArgs e)
        {
            if (OcrSettingsPanel == null || SpeechToTextSettingsPanel == null || !this.IsLoaded) return;

            bool isOcrMode = ModeOcrRadio.IsChecked == true;

            OcrSettingsPanel.Visibility = isOcrMode ? Visibility.Visible : Visibility.Collapsed;
            SpeechToTextSettingsPanel.Visibility = isOcrMode ? Visibility.Collapsed : Visibility.Visible;

            if (isOcrMode)
            {
                if (_selectedTimelineClip != null)
                {
                    _selectedTimelineClip.IsSelected = false;
                    _selectedTimelineClip = null;
                }
                RemoveVideoAdorner();
                UpdateVideoTransform(null);
                UpdateCropThumbsLayout();
            }

            bool canUseOcr = ModeOcrRadio.IsEnabled && isOcrMode;
            RunVSFButton.IsEnabled = canUseOcr;
            StartButton.IsEnabled = canUseOcr;
            AutomaticButton.IsEnabled = canUseOcr;
            bool canUseStt = ModeSpeechToTextRadio.IsEnabled && !isOcrMode;
            RunWhisperButton.IsEnabled = canUseStt;
            UpdateAdornerSuppression();
        }

        private void TimeCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateTimeInputStates();

        private void UpdateTimeInputStates()
        {
            bool startChecked = EnableStartTimeCheckBox.IsChecked == true;
            bool endChecked = EnableEndTimeCheckBox.IsChecked == true;
            StartTimeH.IsEnabled = startChecked; StartTimeM.IsEnabled = startChecked; StartTimeS.IsEnabled = startChecked; StartTimeMS.IsEnabled = startChecked;
            EnableEndTimeCheckBox.IsEnabled = startChecked;
            EndTimeH.IsEnabled = startChecked && endChecked; EndTimeM.IsEnabled = startChecked && endChecked; EndTimeS.IsEnabled = startChecked && endChecked; EndTimeMS.IsEnabled = startChecked && endChecked;
        }

        private string GetFormattedTimeArgument(TextBox h, TextBox m, TextBox s, TextBox ms) => $"{h.Text}:{m.Text}:{s.Text}:{ms.Text}";

        private void InitializeWhisperControls()
        {
            WhisperEngineComboBox.ItemsSource = WhisperEngineManager.SupportedEngines.Keys;
            WhisperEngineComboBox.SelectedIndex = 0;
            WhisperLanguageComboBox.ItemsSource = new[] { new { Code = "auto", Name = "Auto Detect" }, new { Code = "en", Name = "English" }, new { Code = "vi", Name = "Vietnamese" } };
            WhisperLanguageComboBox.DisplayMemberPath = "Name";
            WhisperLanguageComboBox.SelectedValuePath = "Code";
            WhisperLanguageComboBox.SelectedValue = "vi";
            WhisperDeviceComboBox.ItemsSource = new List<string> { "cpu", "cuda" };
            WhisperDeviceComboBox.SelectedIndex = 0;
            WhisperComputeTypeComboBox.ItemsSource = new List<string> { "default", "auto", "int8", "float16" };
            WhisperComputeTypeComboBox.SelectedIndex = 1;
        }

        private void WhisperEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || !(WhisperEngineComboBox.SelectedItem is string selectedEngine)) return;
            var provider = WhisperModelManager.GetProvider(selectedEngine);
            provider.CreateModelFolder();
            WhisperModelComboBox.ItemsSource = provider.GetModels();
            WhisperModelComboBox.DisplayMemberPath = "DisplayName";
            if (WhisperModelComboBox.Items.Count > 0) WhisperModelComboBox.SelectedIndex = 0;
        }

        private void DownloadWhisperModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(WhisperEngineComboBox.SelectedItem is string selectedEngine)) return;
            var provider = WhisperModelManager.GetProvider(selectedEngine);
            var downloaderWindow = new WhisperModelDownloaderWindow(provider) { Owner = this };
            downloaderWindow.ShowDialog();
            WhisperEngineComboBox_SelectionChanged(null, null);
        }
        #endregion

        #region Configuration (Load/Save) (Ported)

        private void MainWindow_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {


            var result = CustomMessageBox.Show(
                "Bạn có muốn đặt tên và lưu lại project hiện tại không?",
                "Lưu Project",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                string defaultName = _currentProject.ProjectName.StartsWith("Untitled") ? "" : _currentProject.ProjectName;
                string newProjectName = InputBox.Show("Nhập tên project:", "Lưu Project", defaultName);

                if (!string.IsNullOrWhiteSpace(newProjectName))
                {
                    string oldPath = _currentProjectFilePath;
                    bool isRenamingUntitled = Path.GetFileNameWithoutExtension(oldPath).StartsWith("Untitled Project") || Path.GetFileNameWithoutExtension(oldPath).StartsWith("Project_");

                    _currentProject.ProjectName = newProjectName;
                    UpdateProjectStateBeforeSave();
                    SaveProjectCurrent();
                    if (isRenamingUntitled && File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); }
                        catch (Exception ex) { }
                    }
                }
                else { }

            }
            else { }
            _audioEngine?.Dispose();
            SaveConfiguration();
            TempFileManager.CleanupCurrentSessionFiles();
        }
        private void CreateDefaultSettingsFile()
        {
            if (File.Exists(_settingsFilePath)) return;
            var defaultConfig = new Dictionary<string, Dictionary<string, string>>
            {
                ["settings"] = new Dictionary<string, string>
                {
                    { "path_VSF", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIOSubphim.exe") },
                    { "OCR_Text", "TXTImages" }, { "cmd_vsf", "" }, { "VSF_Output_Base_Directory", "" },
                    { "Subtitle_Output_Directory", Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
                    { "ocr_mode", OcrMode.GeminiApi.ToString() },
                    { "vsf_video_open_method", VsfVideoOpenMethod.Default.ToString() },
                    { "vsf_processing_mode", VsfProcessingMode.CleanAndCreateTxtImages.ToString() },
                    { "vsf_num_threads_search", "-1" }, { "vsf_num_threads_clean", "-1" }, { "use_cuda", "True" },
                }
            };
            IniConfig.Save(_settingsFilePath, defaultConfig);
        }

        private void LoadConfiguration()
        {
            if (!File.Exists(_settingsFilePath)) CreateDefaultSettingsFile();

            var config = IniConfig.Load(_settingsFilePath);
            char[] keySeparators = { ',', ';', '\n', '\r' };
            _googleDriveFolderId = config.GetValue("settings", "folder_id", "");

            _pathVSF = config.GetValue("settings", "path_VSF", "");
            if (string.IsNullOrWhiteSpace(_pathVSF))
            {
                _pathVSF = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIOSubphim.exe");
            }
            _ocrTextFolderName = config.GetValue("settings", "OCR_Text", "TXTImages");
            _cmdVsfArgs = config.GetValue("settings", "cmd_vsf", "");
            _customVsfOutputBaseDir = config.GetValue("settings", "VSF_Output_Base_Directory", "");
            _customSubtitleOutputDir = config.GetValue("settings", "Subtitle_Output_Directory", "");
            _lastImagesFolderPath = config.GetValue("settings", "Last_Images_Folder_Path", "");
            _deleteRawTexts = config.GetBooleanValue("settings", "delete_raw_texts", true);
            _deleteTexts = config.GetBooleanValue("settings", "delete_texts", true);
            _compressRawTexts = config.GetBooleanValue("settings", "nen_raw_texts", true);
            _useCuda = config.GetBooleanValue("settings", "use_cuda", false);
            _vsfNumThreadsSearch = config.GetValue("settings", "vsf_num_threads_search", "-1");
            _vsfNumThreadsClean = config.GetValue("settings", "vsf_num_threads_clean", "-1");

            Enum.TryParse(config.GetValue("settings", "vsf_video_open_method"), out _selectedVsfOpenMethod);
            Enum.TryParse(config.GetValue("settings", "vsf_processing_mode"), out _selectedVsfProcessingMode);
            Enum.TryParse(config.GetValue("settings", "ocr_mode"), out _currentOcrMode);
            UpdateOcrModeMenuState();

            _geminiApiKeysOcr = config.GetValue("settings", "gemini_api_keys_ocr", "")
                                    .Split(keySeparators, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            _selectedGeminiModelOcr = config.GetValue("settings", "gemini_model_ocr", ApiConfig.DefaultGeminiModelOcr);
            _geminiCustomModelsOcr = config.GetValue("settings", "gemini_custom_models_ocr", "").Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();
            int.TryParse(config.GetValue("settings", "gemini_images_per_request_ocr", "15"), out _geminiImagesPerRequestOcr);
            int.TryParse(config.GetValue("settings", "gemini_requests_per_minute_ocr", "8"), out _geminiRequestsPerMinuteOcr);
            _geminiEnableMultiKeyOcr = config.GetBooleanValue("settings", "gemini_enable_multi_key_ocr", false);

            _chutesApiKeySrt = config.GetValue("settings_srt_translation", "chutes_api_key_srt", "");
            _selectedChutesModelSrt = config.GetValue("settings_srt_translation", "chutes_model_srt", ApiConfig.DefaultChutesModelSrt);
            _chutesCustomModelsSrt = config.GetValue("settings_srt_translation", "chutes_custom_models_srt", "").Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();

            _geminiApiKeysSrt = config.GetValue("settings_srt_translation", "gemini_api_keys_srt", "")
                                    .Split(keySeparators, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            _selectedGeminiModelSrt = config.GetValue("settings_srt_translation", "gemini_model_srt", ApiConfig.DefaultGeminiModelSrt);
            _geminiCustomModelsSrt = config.GetValue("settings_srt_translation", "gemini_custom_models_srt", "").Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();
            _geminiEnableMultiKeySrt = config.GetBooleanValue("settings_srt_translation", "gemini_enable_multi_key_srt", false);
            Enum.TryParse(config.GetValue("settings_srt_translation", "api_provider_srt"), out _currentSrtApiProvider);
            int.TryParse(config.GetValue("settings_srt_translation", "gemini_srt_rpm", "8"), out _geminiSrtTranslationRpm);
            int.TryParse(config.GetValue("settings_srt_translation", "gemini_srt_batch_size", "40"), out _geminiSrtTranslationBatchSize);
            int.TryParse(config.GetValue("settings_srt_translation", "gemini_srt_thinking_budget", "8192"), out _geminiSrtThinkingBudget);
            _selectedChatGPTModelSrt = config.GetValue("settings_srt_translation", "chatgpt_model_srt", "gpt-4o");
            _chatGptCustomModelsSrt = config.GetValue("settings_srt_translation", "chatgpt_custom_models_srt", "").Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();
            int.TryParse(config.GetValue("settings_srt_translation", "chatgpt_srt_batch_size", "40"), out _chatGptSrtBatchSize);
            _selectedSrtGenreValue = config.GetValue("settings_srt_translation", "last_selected_srt_genre", "H.Huyễn Tiên Hiệp");
            _selectedSrtTargetLanguage = config.GetValue("settings_srt_translation", "last_selected_srt_target_language", "Tiếng Việt");
            string promptsJson = config.GetValue("settings_srt_translation", "custom_prompts", "{}");
            try
            {
                _customPrompts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(promptsJson) ?? new Dictionary<string, string>();
            }
            catch
            {
                _customPrompts = new Dictionary<string, string>();
            }
            CustomPromptNames.Clear();
            foreach (var name in _customPrompts.Keys.OrderBy(k => k))
            {
                CustomPromptNames.Add(name);
            }
            SrtApiProviderComboBox.SelectedItem = _currentSrtApiProvider;
            SrtGenreComboBox.SelectedItem = _selectedSrtGenreValue;
            SrtTargetLanguageComboBox.SelectedItem = _selectedSrtTargetLanguage;
            UpdateSrtApiProviderControls();
            _lastExportPath = config.GetValue("settings", "Last_Export_Path", "");
            LogMessage("[CONFIG] Cấu hình đã được tải.");
        }
        private void SaveConfiguration()
        {
            var config = IniConfig.Load(_settingsFilePath);
            if (!config.ContainsKey("settings")) config["settings"] = new Dictionary<string, string>();
            if (!config.ContainsKey("settings_srt_translation")) config["settings_srt_translation"] = new Dictionary<string, string>();
            if (!config.ContainsKey("settings_tts")) config["settings_tts"] = new Dictionary<string, string>();
            var settings = config["settings"];
            settings["folder_id"] = _googleDriveFolderId;
            settings["path_VSF"] = _pathVSF;
            settings["OCR_Text"] = _ocrTextFolderName;
            settings["cmd_vsf"] = _cmdVsfArgs;
            settings["VSF_Output_Base_Directory"] = _customVsfOutputBaseDir;
            settings["Subtitle_Output_Directory"] = _customSubtitleOutputDir;
            settings["Last_Images_Folder_Path"] = ImagesEntry.Text;
            settings["delete_raw_texts"] = _deleteRawTexts.ToString();
            settings["delete_texts"] = _deleteTexts.ToString();
            settings["nen_raw_texts"] = _compressRawTexts.ToString();
            settings["use_cuda"] = _useCuda.ToString();
            settings["vsf_num_threads_search"] = _vsfNumThreadsSearch;
            settings["vsf_num_threads_clean"] = _vsfNumThreadsClean;
            settings["vsf_video_open_method"] = _selectedVsfOpenMethod.ToString();
            settings["vsf_processing_mode"] = _selectedVsfProcessingMode.ToString();
            settings["ocr_mode"] = _currentOcrMode.ToString();
            settings["gemini_api_keys_ocr"] = string.Join(Environment.NewLine, _geminiApiKeysOcr);
            settings["gemini_model_ocr"] = _selectedGeminiModelOcr;
            settings["gemini_custom_models_ocr"] = string.Join(",", _geminiCustomModelsOcr);
            settings["gemini_images_per_request_ocr"] = _geminiImagesPerRequestOcr.ToString();
            settings["gemini_requests_per_minute_ocr"] = _geminiRequestsPerMinuteOcr.ToString();
            settings["gemini_enable_multi_key_ocr"] = _geminiEnableMultiKeyOcr.ToString();

            var srtSettings = config["settings_srt_translation"];
            srtSettings["chatgpt_model_srt"] = _selectedChatGPTModelSrt;
            srtSettings["chatgpt_custom_models_srt"] = string.Join(",", _chatGptCustomModelsSrt);
            srtSettings["chatgpt_srt_batch_size"] = _chatGptSrtBatchSize.ToString();
            srtSettings["chutes_api_key_srt"] = _chutesApiKeySrt;
            srtSettings["chutes_model_srt"] = _selectedChutesModelSrt;
            srtSettings["chutes_custom_models_srt"] = string.Join(",", _chutesCustomModelsSrt);

            srtSettings["gemini_api_keys_srt"] = string.Join(Environment.NewLine, _geminiApiKeysSrt);
            srtSettings["gemini_model_srt"] = _selectedGeminiModelSrt;
            srtSettings["gemini_custom_models_srt"] = string.Join(",", _geminiCustomModelsSrt);
            srtSettings["gemini_enable_multi_key_srt"] = _geminiEnableMultiKeySrt.ToString();
            srtSettings["api_provider_srt"] = _currentSrtApiProvider.ToString();
            srtSettings["gemini_srt_rpm"] = _geminiSrtTranslationRpm.ToString();
            srtSettings["gemini_srt_batch_size"] = _geminiSrtTranslationBatchSize.ToString();
            srtSettings["gemini_srt_thinking_budget"] = _geminiSrtThinkingBudget.ToString();
            srtSettings["last_selected_srt_genre"] = _selectedSrtGenreValue;
            srtSettings["last_selected_srt_target_language"] = _selectedSrtTargetLanguage;
            srtSettings["custom_prompts"] = Newtonsoft.Json.JsonConvert.SerializeObject(_customPrompts);
            var ttsSettings = config["settings_tts"];
            settings["Last_Export_Path"] = _lastExportPath;
            IniConfig.Save(_settingsFilePath, config);
            LogMessage("[CONFIG] Cấu hình đã được lưu.");
        }
        private void SaveCustomPromptButton_Click(object sender, RoutedEventArgs e)
        {
            NewPromptName = "";
            IsSavePromptPopupOpen = true;
            NewPromptNameTextBox.Focus();
        }

        private void PopupSaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NewPromptName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                CustomMessageBox.Show("Tên prompt không được để trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentCustomPromptText))
            {
                CustomMessageBox.Show("Nội dung prompt không được để trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _customPrompts[name] = CurrentCustomPromptText;

            if (!CustomPromptNames.Contains(name))
            {
                CustomPromptNames.Add(name);
                var sorted = CustomPromptNames.OrderBy(n => n).ToList();
                CustomPromptNames.Clear();
                foreach (var sortedName in sorted)
                {
                    CustomPromptNames.Add(sortedName);
                }
            }

            SelectedCustomPromptName = name;
            IsSavePromptPopupOpen = false;
            SaveConfiguration();
        }

        private void PopupCancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsSavePromptPopupOpen = false;
        }
        private string GetResolvedVSFOutputDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_customVsfOutputBaseDir) && Directory.Exists(_customVsfOutputBaseDir))
                return Path.GetFullPath(_customVsfOutputBaseDir);
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private string GetResolvedSubtitleOutputDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_customSubtitleOutputDir) && Directory.Exists(_customSubtitleOutputDir))
                return Path.GetFullPath(_customSubtitleOutputDir);
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        #endregion

        #region Menu Logic (Ported)

        private void SettingsApiMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new Settings(
                _googleDriveFolderId, _pathVSF, _ocrTextFolderName, _cmdVsfArgs, _customVsfOutputBaseDir,
                _customSubtitleOutputDir, _selectedVsfOpenMethod, _selectedVsfProcessingMode, _vsfNumThreadsSearch,
                _vsfNumThreadsClean, _useCuda, _deleteRawTexts, _deleteTexts, _compressRawTexts,
                _geminiApiKeysOcr, _geminiImagesPerRequestOcr, _geminiRequestsPerMinuteOcr, _geminiEnableMultiKeyOcr,
                _selectedGeminiModelOcr, _geminiCustomModelsOcr,
                _chutesApiKeySrt, _geminiApiKeysSrt, _geminiEnableMultiKeySrt, _currentSrtApiProvider,
            _selectedChutesModelSrt, _selectedGeminiModelSrt, _selectedChatGPTModelSrt,
            _chutesCustomModelsSrt, _geminiCustomModelsSrt, _chatGptCustomModelsSrt,
            _geminiSrtTranslationRpm, _geminiSrtTranslationBatchSize, _geminiSrtThinkingBudget,
            _chatGptSrtBatchSize);
            settingsWin.Owner = this;

            if (settingsWin.ShowDialog() == true)
            {
                _googleDriveFolderId = settingsWin.GoogleDriveFolderId;
                _pathVSF = settingsWin.PathVSF;
                _ocrTextFolderName = settingsWin.OcrTextFolderName;
                _cmdVsfArgs = settingsWin.CmdVsfArgs;
                _customVsfOutputBaseDir = settingsWin.CustomVsfOutputBaseDir;
                _customSubtitleOutputDir = settingsWin.CustomSubtitleOutputDir;
                _selectedVsfOpenMethod = settingsWin.SelectedVsfOpenMethod;
                _selectedVsfProcessingMode = settingsWin.SelectedVsfProcessingMode;
                _vsfNumThreadsSearch = settingsWin.VsfNumThreadsSearch;
                _vsfNumThreadsClean = settingsWin.VsfNumThreadsClean;
                _useCuda = settingsWin.UseCuda;
                _deleteRawTexts = settingsWin.DeleteRawTexts;
                _deleteTexts = settingsWin.DeleteTexts;
                _compressRawTexts = settingsWin.CompressRawTexts;
                _geminiApiKeysOcr = settingsWin.GeminiApiKeysOcr;
                _geminiImagesPerRequestOcr = settingsWin.GeminiImagesPerRequestOcr;
                _geminiRequestsPerMinuteOcr = settingsWin.GeminiRequestsPerMinuteOcr;
                _geminiEnableMultiKeyOcr = settingsWin.GeminiEnableMultiKeyOcr;
                _selectedGeminiModelOcr = settingsWin.SelectedGeminiModelOcr;
                _geminiCustomModelsOcr = settingsWin.GeminiCustomModelsOcr;
                _chutesApiKeySrt = settingsWin.ChutesApiKeySrt;
                _geminiApiKeysSrt = settingsWin.GeminiApiKeysSrt;
                _geminiEnableMultiKeySrt = settingsWin.GeminiEnableMultiKeySrt;
                _currentSrtApiProvider = settingsWin.SelectedSrtApiProvider;
                _selectedChutesModelSrt = settingsWin.SelectedChutesModelSrt;
                _selectedGeminiModelSrt = settingsWin.SelectedGeminiModelSrt;
                _selectedChatGPTModelSrt = settingsWin.SelectedChatGPTModelSrt;
                _chutesCustomModelsSrt = settingsWin.ChutesCustomModelsSrt;
                _geminiCustomModelsSrt = settingsWin.GeminiCustomModelsSrt;
                _chatGptCustomModelsSrt = settingsWin.ChatGPTCustomModelsSrt;
                _geminiSrtTranslationRpm = settingsWin.GeminiSrtTranslationRpm;
                _geminiSrtTranslationBatchSize = settingsWin.GeminiSrtTranslationBatchSize;
                _geminiSrtThinkingBudget = settingsWin.GeminiSrtThinkingBudget;
                _chatGptSrtBatchSize = settingsWin.ChatGPTSrtBatchSize;

                SaveConfiguration();
                RebuildServicesFromConfig();
                UpdateSrtApiProviderControls();
                UpdateVsfProcessingModeRadioButtons();
            }
        }
        private void RebuildServicesFromConfig()
        {
            _geminiServiceOcr = new GeminiOcrService(
                _geminiApiKeysOcr, _geminiRequestsPerMinuteOcr, _geminiEnableMultiKeyOcr, _selectedGeminiModelOcr
            )
            { LogMessage = (msg) => LogMessage(msg) };

            _srtTranslationService = new SrtTranslationService(
                new SrtTranslationService.SrtApiConfig
                {
                    ChutesApiKey = _chutesApiKeySrt,
                    ChutesModel = _selectedChutesModelSrt,
                    GeminiApiKeys = _geminiApiKeysSrt,
                    GeminiModel = _selectedGeminiModelSrt,
                    UseGeminiMultiKey = _geminiEnableMultiKeySrt,
                    GeminiRpm = _geminiSrtTranslationRpm,
                    GeminiBatchSize = _geminiSrtTranslationBatchSize,
                    GeminiThinkingBudget = _geminiSrtThinkingBudget,
                    ChatGPTBatchSize = _chatGptSrtBatchSize,
                    ChatGPTModel = _selectedChatGPTModelSrt
                }
            )
            { LogMessage = (msg, isErr) => LogMessage(msg, isErr) };
            LoadGoogleAccounts();
        }
        private void UpdateVsfProcessingModeRadioButtons()
        {
            VsfModeCleanRadio.IsChecked = (_selectedVsfProcessingMode == VsfProcessingMode.CleanAndCreateTxtImages);
            VsfModeSearchOnlyRadio.IsChecked = (_selectedVsfProcessingMode == VsfProcessingMode.SearchSubtitlesOnly);
        }


        private void ShowShortcuts_Click(object sender, RoutedEventArgs e)
        {
            ShortcutsWindow shortcutsWin = new ShortcutsWindow { Owner = this };
            shortcutsWin.ShowDialog();
        }

        private void MenuM3u8Downloader_Click(object sender, RoutedEventArgs e)
        {
            m3u8 m3u8DownloaderWin = new m3u8 { Owner = this };
            m3u8DownloaderWin.Show();
        }

        #endregion

        #region Logging

        private void LogMessage(string message, bool isError = false)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }

        #endregion

        #region Window Title Bar (Ported)
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) MaximizeButton_Click(sender, e); else DragMove();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {

        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        #endregion
        #region SRT Translation Logic (Ported)
        private bool IsSrtTranslatedTextError(string translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText)) return true;

            string t = translatedText.Trim();
            try
            {
                if (_srtErrorMarkers != null && _srtErrorMarkers.Contains(t)) return true;
            }
            catch { /* an toàn */ }
            string[] errorKeywords =
            {
        "lỗi api", "too many requests", "forbidden", "service unavailable",
        "bad gateway", "gateway timeout", "rate limit", "overload",
        "http 429", "http 502", "http 503", "http 504", "429", "502", "503", "504"
    };

            foreach (var kw in errorKeywords)
            {
                if (t.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            if (t.StartsWith("[Lỗi", StringComparison.OrdinalIgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(HTTP|Status)\s*(429|502|503|504)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            return false;
        }
        private List<SrtSubtitleLine> GetFailedTranslatedSrtLinesFromGrid()
        {
            var failed = SrtSubtitleLinesView
                .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) && IsSrtTranslatedTextError(l.TranslatedText))
                .ToList();
            return failed;
        }
        private async Task AutoRetryFailedSrtTranslationsAsync(CancellationToken token, int maxPasses = 3)
        {
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                if (token.IsCancellationRequested) return;
                var failedLines = GetFailedTranslatedSrtLinesFromGrid();
                if (failedLines == null || failedLines.Count == 0) return;
                LogMessage($"[AutoRetry] Phát hiện {failedLines.Count} dòng lỗi. Thử lại lần {pass}/{maxPasses}.");
                await TranslateSrtLogic(token, failedLines);
                if (token.IsCancellationRequested) return;
                var remain = GetFailedTranslatedSrtLinesFromGrid();
                if (remain.Count == 0) return;

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(4, pass)), token);
            }
        }

        private async void TranslateSrtButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SrtSubtitleLinesView.Any())
            {
                CustomMessageBox.Show("Vui lòng tải file SRT trước khi dịch.", "Chưa có phụ đề", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _masterCts = new CancellationTokenSource();

            try
            {
                var linesToTranslate = new List<SrtSubtitleLine>();
                var linesWithTranslation = SrtSubtitleLinesView.Where(l => !string.IsNullOrWhiteSpace(l.TranslatedText) && !_srtErrorMarkers.Contains(l.TranslatedText.Trim())).ToList();
                var linesWithErrorOrEmpty = SrtSubtitleLinesView.Where(l => string.IsNullOrWhiteSpace(l.TranslatedText) || _srtErrorMarkers.Contains(l.TranslatedText.Trim())).ToList();

                if (!linesWithErrorOrEmpty.Any() && linesWithTranslation.Any())
                {
                    var result = CustomMessageBox.Show("Tất cả các dòng đã có bản dịch. Bạn có muốn dịch lại toàn bộ không?", "Xác nhận dịch lại", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        linesToTranslate = SrtSubtitleLinesView.ToList();
                    }
                    else
                    {
                        _masterCts.Dispose();
                        _masterCts = null;
                        return;
                    }
                }
                else if (linesWithTranslation.Any() && linesWithErrorOrEmpty.Any())
                {
                    var choice = CustomMessageBox.Show("Phát hiện có dòng đã dịch và dòng bị lỗi/trống.\n\n- Chọn 'Yes' để DỊCH LẠI TOÀN BỘ.\n- Chọn 'No' để CHỈ DỊCH CÁC DÒNG LỖI/TRỐNG.", "Tùy chọn dịch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.Yes)
                    {
                        linesToTranslate = SrtSubtitleLinesView.ToList();
                    }
                    else
                    {
                        linesToTranslate = linesWithErrorOrEmpty;
                    }
                }
                else
                {
                    linesToTranslate = SrtSubtitleLinesView.ToList();
                }

                if (linesToTranslate.Any())
                {
                    IsSrtTranslating = true;
                    UpdateUiForProcessing(true);
                    await TranslateSrtLogic(_masterCts.Token, linesToTranslate);
                    await AutoRetryFailedSrtTranslationsAsync(_masterCts.Token);

                    IsSrtTranslating = false;
                    UpdateUiForProcessing(false);
                }
            }
            catch (OperationCanceledException)
            {
                IsSrtTranslating = false;
                UpdateUiForProcessing(false);
            }
            catch (Exception ex)
            {
                IsSrtTranslating = false;
                UpdateUiForProcessing(false);
                CustomMessageBox.Show($"Đã xảy ra lỗi khi dịch phụ đề: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }

        private async void MergeDuplicateSrtOriginalsButton_Click(object sender, RoutedEventArgs e)
        {
            List<SrtSubtitleLine> removedLines = SrtFileUtils.MergeDuplicates(SrtSubtitleLinesView);
            int mergedCount = removedLines.Count;

            if (mergedCount > 0)
            {
                var clipsToRemoveFromTimeline = new List<TimelineClipViewModel>();
                foreach (var removedLine in removedLines)
                {
                    _currentProject.Subtitles.Remove(removedLine);

                    var clipVM = TimelineClips.FirstOrDefault(c => c.SourceData == removedLine);
                    if (clipVM != null)
                    {
                        clipsToRemoveFromTimeline.Add(clipVM);
                    }
                }

                foreach (var clipVM in clipsToRemoveFromTimeline)
                {
                    TimelineClips.Remove(clipVM);
                }

                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                _undoRedoService.AddState(CaptureEditorSnapshot());
                await SaveProjectCurrentAsync();

            }
            else
            {
            }
        }
        private void ExportTranslatedSrtButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SrtSubtitleLinesView.Any()) return;
            var choice = CustomMessageBox.Show("Bạn muốn xuất file phụ đề chứa nội dung nào?\n\n- Chọn 'Yes' để xuất BẢN DỊCH.\n- Chọn 'No' để xuất VĂN BẢN GỐC.", "Tùy chọn xuất file", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            bool exportTranslated = (choice == MessageBoxResult.Yes);

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "SRT files (*.srt)|*.srt|VTT files (*.vtt)|*.vtt",
                Title = "Lưu file phụ đề",
                FileName = string.IsNullOrWhiteSpace(_currentSrtFilePath)
                    ? (exportTranslated ? "translated_sub.srt" : "original_sub.srt")
                    : $"{Path.GetFileNameWithoutExtension(_currentSrtFilePath)}{(exportTranslated ? "_translated" : "_original")}.srt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var linesToExport = new List<SrtSubtitleLine>();
                    foreach (var line in SrtSubtitleLinesView)
                    {

                        var exportLine = new SrtSubtitleLine
                        {
                            Index = line.Index,
                            StartTime = line.StartTime,
                            Duration = line.Duration,
                            OriginalText = line.OriginalText,
                            TranslatedText = exportTranslated
                                ? (string.IsNullOrWhiteSpace(line.TranslatedText) ? line.OriginalText : line.TranslatedText)
                                : line.OriginalText
                        };
                        linesToExport.Add(exportLine);
                    }

                    SrtFileUtils.SaveToFile(saveFileDialog.FileName, linesToExport);
                    CustomMessageBox.Show("Xuất file thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void StopSrtTranslationButton_Click(object sender, RoutedEventArgs e)
        {
            _masterCts?.Cancel();
        }

        private async Task TranslateSrtLogic(CancellationToken token, List<SrtSubtitleLine> specificLines = null)
        {

            RebuildSrtTranslationService();
            var linesToTranslate = (specificLines != null && specificLines.Any())
                ? specificLines
                : SrtSubtitleLinesView
                    .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) && (string.IsNullOrWhiteSpace(l.TranslatedText) || _srtErrorMarkers.Contains(l.TranslatedText.Trim())))
                    .ToList();

            if (!linesToTranslate.Any())
            {
                LogMessage("[Translate] Không có dòng nào hợp lệ để dịch.");
                return;
            }
            if (_currentSrtApiProvider == SrtApiProvider.AIOLauncher)
            {
                await TranslateWithAioLauncherApi(linesToTranslate, token);
            }
            else
            {
                await TranslateWithThirdPartyApi(linesToTranslate, token);
            }
        }
        private async Task TranslateWithAioLauncherApi(List<SrtSubtitleLine> linesToTranslate, CancellationToken token)
        {
            bool acceptPartial = false;
            string systemInstruction;
            if (IsCustomPromptEnabled)
            {
                if (string.IsNullOrWhiteSpace(CurrentCustomPromptText))
                {
                    CustomMessageBox.Show("Prompt tùy chỉnh đang được bật nhưng nội dung lại trống. Vui lòng nhập prompt.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                systemInstruction = CurrentCustomPromptText.Trim() + CUSTOM_PROMPT_SUFFIX;
            }
            else
            {
                systemInstruction = _srtTranslationService.GetSystemInstructionForGeminiSrtTranslation(_selectedSrtGenreValue, _selectedSrtTargetLanguage);
            }
            while (!token.IsCancellationRequested)
            {
                var srtLinesForRequest = linesToTranslate.Select(l => new SrtLine(l.Index, l.OriginalText)).ToList();

                var response = await ApiService.StartTranslationJobAsync(_selectedSrtGenreValue, _selectedSrtTargetLanguage, srtLinesForRequest, systemInstruction, acceptPartial);

                switch (response.Status)
                {
                    case "Accepted":
                        await PollForAioResults(response.SessionId, token);
                        return;

                    case "PartialContent":
                        var userChoice = CustomMessageBox.Show(response.Message, "Không đủ lượt dịch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (userChoice == MessageBoxResult.Yes)
                        {
                            acceptPartial = true;
                            continue;
                        }
                        else
                        {
                            return;
                        }

                    case "Error":
                    default:
                        CustomMessageBox.Show(response.Message, "Lỗi Dịch", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                }
            }
        }
        private async Task PollForAioResults(string sessionId, CancellationToken token)
        {
            var totalLines = SrtSubtitleLinesView.Count;
            var translatedCount = 0;

            while (!token.IsCancellationRequested)
            {
                var result = await ApiService.GetTranslationResultsAsync(sessionId);

                if (result.NewLines != null && result.NewLines.Any())
                {
                    foreach (var newLine in result.NewLines)
                    {
                        var lineToUpdate = SrtSubtitleLinesView.FirstOrDefault(l => l.Index == newLine.Index);
                        if (lineToUpdate != null)
                        {
                            lineToUpdate.TranslatedText = newLine.TranslatedText;
                        }
                    }
                    translatedCount += result.NewLines.Count;
                }

                if (result.IsCompleted)
                {
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        CustomMessageBox.Show($"Quá trình dịch đã hoàn thành nhưng có lỗi xảy ra trên server:\n\n{result.ErrorMessage}", "Lỗi Server", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    await AutoRetryFailedSrtTranslationsAsync(token);
                    return;
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task TranslateWithThirdPartyApi(List<SrtSubtitleLine> linesToTranslate, CancellationToken token)
        {
            var (canTranslate, message, remaining) = await ApiService.PreSrtTranslateCheckAsync(linesToTranslate.Count);

            if (!canTranslate)
            {
                CustomMessageBox.Show(message, "Không đủ lượt dịch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var results = await _srtTranslationService.TranslateAllSrtLinesAsync(
                linesToTranslate, _currentSrtApiProvider, _selectedSrtGenreValue, _selectedSrtTargetLanguage,
                (progress) => Dispatcher.Invoke(() => { /*  */ }),
                token
            );

            foreach (var result in results)
            {
                var lineToUpdate = SrtSubtitleLinesView.FirstOrDefault(l => l.Index == result.Key);
                if (lineToUpdate != null)
                {
                    lineToUpdate.TranslatedText = result.Value.text;
                }
            }
            LogMessage("[Translate3rdParty] Dịch hoàn tất.");
        }
        private void SrtApiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || !(SrtApiProviderComboBox.SelectedItem is SrtApiProvider selectedProvider)) return;
            _currentSrtApiProvider = selectedProvider;
            UpdateSrtApiProviderControls();
        }

        private void SrtTranslateModel_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || SrtTranslateModelComboBox.SelectedItem == null) return;
            string selectedModel = SrtTranslateModelComboBox.SelectedItem.ToString();

            if (_currentSrtApiProvider == SrtApiProvider.ChutesAI)
                _selectedChutesModelSrt = selectedModel;
            else if (_currentSrtApiProvider == SrtApiProvider.Gemini)
                _selectedGeminiModelSrt = selectedModel;
            else if (_currentSrtApiProvider == SrtApiProvider.ChatGPT)
                _selectedChatGPTModelSrt = selectedModel;
        }

        private void SrtTargetLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (SrtTargetLanguageComboBox.SelectedItem != null)
            {
                _selectedSrtTargetLanguage = SrtTargetLanguageComboBox.SelectedItem.ToString();
            }
        }
        private void SrtGenre_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || SrtGenreComboBox.SelectedItem == null) return;
            _selectedSrtGenreValue = SrtGenreComboBox.SelectedItem.ToString();
        }

        private void UpdateSrtApiProviderControls()
        {
            SrtApiProviderComboBox.SelectedItem = _currentSrtApiProvider;

            bool isGemini = _currentSrtApiProvider == SrtApiProvider.Gemini;
            bool isChatGPT = _currentSrtApiProvider == SrtApiProvider.ChatGPT;
            bool isChutes = _currentSrtApiProvider == SrtApiProvider.ChutesAI;
            bool isAIO = _currentSrtApiProvider == SrtApiProvider.AIOLauncher;
            SrtTranslateModelComboBox.IsEnabled = !isAIO;
            SrtTranslateModelComboBox.Items.Clear();

            if (isAIO)
            {
                SrtTranslateModelComboBox.ItemsSource = null;
            }
            else if (isGemini)
            {
                SrtTranslateModelComboBox.Items.Add(ApiConfig.DefaultGeminiModelSrt);
                foreach (var model in _geminiCustomModelsSrt) SrtTranslateModelComboBox.Items.Add(model);
                SrtTranslateModelComboBox.SelectedItem = _selectedGeminiModelSrt;
            }
            else if (isChatGPT)
            {
                foreach (var model in ApiConfig.DefaultChatGPTModelsSrt) SrtTranslateModelComboBox.Items.Add(model);
                foreach (var model in _chatGptCustomModelsSrt) SrtTranslateModelComboBox.Items.Add(model);
                SrtTranslateModelComboBox.SelectedItem = _selectedChatGPTModelSrt;
            }
            else
            {
                SrtTranslateModelComboBox.Items.Add(ApiConfig.DefaultChutesModelSrt);
                foreach (var model in _chutesCustomModelsSrt) SrtTranslateModelComboBox.Items.Add(model);
                SrtTranslateModelComboBox.SelectedItem = _selectedChutesModelSrt;
            }

            if (SrtTranslateModelComboBox.SelectedIndex == -1 && SrtTranslateModelComboBox.Items.Count > 0)
            {
                SrtTranslateModelComboBox.SelectedIndex = 0;
            }
        }

        private void RebuildSrtTranslationService()
        {
            _srtTranslationService = new SrtTranslationService(
                new SrtTranslationService.SrtApiConfig
                {
                    ChutesApiKey = _chutesApiKeySrt,
                    ChutesModel = _selectedChutesModelSrt,
                    GeminiApiKeys = _geminiApiKeysSrt,
                    GeminiModel = _selectedGeminiModelSrt,
                    UseGeminiMultiKey = _geminiEnableMultiKeySrt,
                    GeminiRpm = _geminiSrtTranslationRpm,
                    GeminiBatchSize = _geminiSrtTranslationBatchSize,
                    GeminiThinkingBudget = _geminiSrtThinkingBudget,
                    ChatGPTBatchSize = _chatGptSrtBatchSize,
                    ChatGPTModel = _selectedChatGPTModelSrt
                }
            )
            { LogMessage = (msg, isErr) => LogMessage(msg, isErr) };
        }

        #endregion
        #region File Handling (SRT Load, Drop) - Ported

        private async void BrowseSrtFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Subtitle files (*.srt;*.vtt;*.ass)|*.srt;*.vtt;*.ass|All files (*.*)|*.*",
                Title = "Chọn file phụ đề để dịch"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                await LoadSrtFile(openFileDialog.FileName);
            }
        }


        private async Task LoadSrtFile(string filePath, bool addToTimelineAndProject = true)
        {
            try
            {
                var lines = SrtFileUtils.LoadFromFile(filePath);
                _currentSrtFilePath = filePath;
                if (addToTimelineAndProject)
                {
                    TimeSpan timeOffset = TimeSpan.Zero;
                    var videoClips = TimelineClips
                        .Where(c => c.ClipType == TimelineClipType.Video)
                        .OrderBy(c => c.StartTime)
                        .ToList();

                    if (videoClips.Any())
                    {
                        timeOffset = videoClips.First().StartTime;
                    }
                    var oldSubtitleClips = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle).ToList();
                    foreach (var clip in oldSubtitleClips)
                    {
                        TimelineClips.Remove(clip);
                    }
                    _currentProject.Subtitles.Clear();
                    var nonTextLines = SrtSubtitleLinesView.Where(l => !l.IsTextClip).ToList();
                    foreach (var line in nonTextLines)
                    {
                        SrtSubtitleLinesView.Remove(line);
                    }

                    var projectTemplate = GetTemplateAsStyleState();

                    foreach (var line in lines)
                    {
                        line.StartTime += timeOffset;
                        line.Style = projectTemplate.Clone();
                        line.Style.Width = null;
                        line.Style.FixedTextBoxWidth = 0;

                        SrtSubtitleLinesView.Add(line);
                        TimelineClips.Add(new TimelineClipViewModel(line));
                        _currentProject.Subtitles.Add(line);
                    }

                    RecalculateTrackAssignments();
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                    UpdateSubtitleForCurrentTime();
                    if (TranslateTab != null) TranslateTab.IsChecked = true;
                }
                else
                {
                    SrtSubtitleLinesView.Clear();
                    foreach (var line in _currentProject.TextClips) SrtSubtitleLinesView.Add(line);
                    foreach (var line in lines) SrtSubtitleLinesView.Add(line);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể tải hoặc parse file phụ đề.\nLỗi: {ex.Message}", "Lỗi Tải File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void MediaPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void MediaPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || !files.Any()) return;

                LoadingOverlay.Visibility = Visibility.Visible;
                try
                {
                    foreach (var filePath in files)
                    {
                        var newAsset = await AddFileToMediaBinAsync(filePath);
                        if (newAsset != null && newAsset.Type == AssetType.Subtitle)
                        {
                            await LoadSrtFile(filePath, addToTimelineAndProject: false);
                            if (TranslateTab != null) TranslateTab.IsChecked = true;
                        }
                    }
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
        private async Task<MediaAsset> AddFileToMediaBinAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var asset = new MediaAsset { FilePath = filePath };

            try
            {
                if (new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" }.Contains(extension))
                {
                    asset.Type = AssetType.Video;

                    if (!_filmstripServiceInstances.ContainsKey(filePath))
                    {
                        try
                        {
                            _filmstripServiceInstances[filePath] = FilmstripService.Open(filePath);
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    }

                    if (_filmstripServiceInstances.TryGetValue(filePath, out var filmstripService))
                    {
                        var mediaInfoForThumb = await FFProbe.AnalyseAsync(filePath);
                        var durationForThumb = mediaInfoForThumb.Duration;
                        var captureTime = durationForThumb.TotalSeconds > 10 ? TimeSpan.FromSeconds(durationForThumb.TotalSeconds * 0.1) : TimeSpan.FromSeconds(1);

                        BitmapImage thumbnailBitmap = await filmstripService.GetThumbnailAtAsync(captureTime, 120, CancellationToken.None);
                        if (thumbnailBitmap != null)
                        {
                            asset.ThumbnailSource = thumbnailBitmap;
                            asset.ThumbnailBase64 = Services.MediaProcessingService.ConvertBitmapImageToBase64(thumbnailBitmap);
                        }
                    }

                    var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                    asset.Duration = mediaInfo.Duration;
                    var videoStream = mediaInfo.PrimaryVideoStream;
                    if (videoStream != null)
                    {
                        asset.Width = videoStream.Width;
                        asset.Height = videoStream.Height;
                    }

                    VideoImageAssets.Add(asset);
                }
                else if (new[] { ".mp3", ".wav", ".aac", ".m4a", ".ogg", ".flac" }.Contains(extension))
                {
                    asset.Type = AssetType.Audio;
                    var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                    asset.Duration = mediaInfo.Duration;
                    AudioAssets.Add(asset);
                    QueueWaveformGeneration(asset);
                }
                else if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" }.Contains(extension))
                {
                    asset.Type = AssetType.Image;
                    asset.ThumbnailSource = LoadBitmapImage(filePath);
                    if (asset.ThumbnailSource != null)
                    {
                        asset.Width = asset.ThumbnailSource.PixelWidth;
                        asset.Height = asset.ThumbnailSource.PixelHeight;
                        asset.ThumbnailBase64 = Services.MediaProcessingService.ConvertBitmapImageToBase64(asset.ThumbnailSource);
                    }
                    asset.Duration = TimeSpan.FromSeconds(5);
                    VideoImageAssets.Add(asset);
                }
                else if (new[] { ".srt", ".ass", ".vtt" }.Contains(extension))
                {
                    asset.Type = AssetType.Subtitle;
                    try
                    {
                        var lines = await Task.Run(() => SrtFileUtils.LoadFromFile(filePath));
                        if (lines.Any())
                        {
                            var startTime = lines.Min(l => l.StartTime);
                            var endTime = lines.Max(l => l.EndTime);
                            asset.Duration = endTime - startTime;
                        }
                        else
                        {
                            asset.Duration = TimeSpan.FromSeconds(5);
                        }
                    }
                    catch (Exception)
                    {
                        asset.Duration = TimeSpan.FromSeconds(5);
                    }
                    SubtitleAssets.Add(asset);
                }
                else
                {
                    return null;
                }

                return asset;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddFileToMediaBinAsync] Error processing file {filePath}: {ex.Message}");
                return null;
            }
        }

        #endregion
        #region OCR Mode Selection Logic (Ported)

        private void CopyFailedImage(string imagePath, string reason)
        {
            try
            {
                if (!Directory.Exists(_desktopErrorFolderPath))
                {
                    Directory.CreateDirectory(_desktopErrorFolderPath);
                }

                if (File.Exists(imagePath))
                {
                    File.Copy(imagePath, Path.Combine(_desktopErrorFolderPath, Path.GetFileName(imagePath)), true);
                }
            }
            catch (Exception ex)
            {
            }
        }
        private void OcrMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem selectedMenu)
            {
                if (selectedMenu == OcrModeGoogleCloudMenuItem)
                {
                    _currentOcrMode = OcrMode.GoogleCloud;
                }
                else if (selectedMenu == OcrModeGeminiApiMenuItem)
                {
                    _currentOcrMode = OcrMode.GeminiApi;
                }
                UpdateOcrModeMenuState();
                SaveConfiguration();
            }
        }
        private void UpdateOcrModeMenuState()
        {
            if (OcrModeGoogleCloudMenuItem == null || OcrModeGeminiApiMenuItem == null || OcrProviderGoogleCloudRadio == null || OcrProviderGeminiApiRadio == null)
            {
                return;
            }

            bool isGoogle = (_currentOcrMode == OcrMode.GoogleCloud);
            OcrModeGoogleCloudMenuItem.IsChecked = isGoogle;
            OcrModeGeminiApiMenuItem.IsChecked = !isGoogle;
            OcrProviderGoogleCloudRadio.IsChecked = isGoogle;
            OcrProviderGeminiApiRadio.IsChecked = !isGoogle;
        }
        #endregion
        #region Menu Logic (File, Tools) - Ported

        private void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var result = CustomMessageBox.Show("Tạo project mới sẽ xóa toàn bộ công việc chưa lưu. Bạn có chắc chắn?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                ResetApplicationState(true);
            }
        }
        private void ExportVideoButton_Click(object sender, RoutedEventArgs e)
        {
            CustomMessageBox.Show("Chức năng Xuất Video sẽ được tích hợp sau khi hoàn thiện tab Edit Sub.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void DownloadDefaultFonts_Click(object sender, RoutedEventArgs e)
        {
            var fontUrl = "https://github.com/google/fonts/raw/main/ofl/roboto/Roboto-Regular.ttf";
            try
            {
                await FontManager.DownloadFontAsync(fontUrl, new Progress<double>(p => { /* Cập nhật progress bar */ }));
                CustomMessageBox.Show("Tải font thành công!");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Lỗi tải font: {ex.Message}");
            }
        }

        private void ImportFonts_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "Chọn thư mục chứa các file font (.ttf, .otf)",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                var fontFiles = Directory.GetFiles(dialog.SelectedPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

                int importedCount = 0;
                foreach (var fontFile in fontFiles)
                {
                    try
                    {
                        var destPath = Path.Combine(FontManager.FontFolder, Path.GetFileName(fontFile));
                        File.Copy(fontFile, destPath, true);
                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                    }
                }

                if (importedCount > 0)
                {
                    CustomMessageBox.Show($"Đã import thành công {importedCount} font.");
                }
            }
        }

        #endregion
        #region New Project, Preset, and State Management Logic

        private async void ResetApplicationState(bool createNewProjectState)
        {
            await PauseTimelinePlayback();
            await FFMEPlayer.Close();
            foreach (var service in _filmstripServiceInstances.Values)
            {
                service.Dispose();
            }
            _filmstripServiceInstances.Clear();

            if (createNewProjectState)
            {
                CurrentProject = ProjectManager.CreateNewProject();
                _currentProjectFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects", $"{_currentProject.ProjectName}.json");
            }
            CurrentProject.ProjectReferenceVideoWidth = DEFAULT_REFERENCE_WIDTH;
            CurrentProject.ProjectReferenceVideoHeight = DEFAULT_REFERENCE_HEIGHT;
            videoGrid.Width = DEFAULT_REFERENCE_WIDTH;
            videoGrid.Height = DEFAULT_REFERENCE_HEIGHT;
            SrtSubtitleLinesView.Clear();
            TimelineClips.Clear();
            VisibleTimelineClips.Clear();
            VideoImageAssets.Clear();
            AudioAssets.Clear();
            SubtitleAssets.Clear();
            TimelineAudioClips.Clear();

            _selectedSubtitle = null;
            _selectedTimelineClip = null;
            SelectedAudioClip = null;
            ActiveMediaAsset = null;
            _activeVideoClip = null;
            foreach (var visual in _activeVisuals.Values)
            {
                SubtitleRenderCanvas.Children.Remove(visual);
            }
            _activeVisuals.Clear();
            _playhead = TimeSpan.Zero;
            _totalTimelineDuration = TimeSpan.FromMinutes(5);
            _actualContentDuration = TimeSpan.Zero;
            _currentSrtFilePath = "";
            _currentVideoPath = null;
            _undoRedoService.Clear();

            VideoEntry.Text = "";
            ImagesEntry.Text = "";
            SubtitleEntry.Text = "";
            totalDurationTextBlock.Text = "00:00:00.000";
            UpdateEditorPanelFromState();
            UpdateTimelineScaleAndRender();
            UpdatePlaybackUI(TimeSpan.Zero);
            UpdateEditorPanelVisibility();

            if (createNewProjectState)
            {
                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
            }
        }
        private void UpdateProjectStateBeforeSave()
        {
            if (_currentProject == null)
            {
                _currentProject = new ProjectState();
            }
            var timelineAssets = TimelineClips
                .Select(vm => vm?.SourceData as MediaAsset)
                .Where(ma => ma != null)
                .ToList();
            _currentProject.TimelineMediaClips = timelineAssets;
            var subs = SrtSubtitleLinesView.Where(line => line != null && !line.IsTextClip).ToList();
            var texts = SrtSubtitleLinesView.Where(line => line != null && line.IsTextClip).ToList();
            _currentProject.Subtitles = subs;
            _currentProject.TextClips = texts;
            var allAudioClips = TimelineClips
                .Select(vm => vm?.SourceData as TimelineAudioClip)
                .Where(tac => tac != null)
                .ToList();
            _currentProject.VoicedSubtitles = allAudioClips;
            if (CurrentProject.ProjectReferenceVideoWidth <= 0) CurrentProject.ProjectReferenceVideoWidth = 1280;
            if (CurrentProject.ProjectReferenceVideoHeight <= 0) CurrentProject.ProjectReferenceVideoHeight = 720;
        }
        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Header is string presetName)
            {
                var presetState = PresetManager.LoadPreset(presetName);
                if (presetState != null)
                {
                    ApplyPreset(presetState);
                }
            }
        }
        private void DeleteCurrentProject_Click()
        {
            var result = CustomMessageBox.Show(
                $"Bạn có chắc chắn muốn xoá vĩnh viễn project hiện tại '{_currentProject.ProjectName}' không? Hành động này không thể hoàn tác.",
                "Xác nhận Xoá Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                string projectToDeleteName = _currentProject.ProjectName;
                string projectToDeletePath = _currentProjectFilePath;
                ProjectManager.DeleteProject(projectToDeletePath);
                ResetApplicationState(true);
                CustomMessageBox.Show($"Đã xoá thành công project '{projectToDeleteName}'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
            }
        }

        private void ApplyPreset(ProjectState preset)
        {
            _currentProject.VsfCropTop = preset.VsfCropTop;
            _currentProject.VsfCropBottom = preset.VsfCropBottom;
            _currentProject.VsfCropLeft = preset.VsfCropLeft;
            _currentProject.VsfCropRight = preset.VsfCropRight;
            _topPercent = _currentProject.VsfCropTop;
            _bottomPercent = _currentProject.VsfCropBottom;
            _leftPercent = _currentProject.VsfCropLeft;
            _rightPercent = _currentProject.VsfCropRight;
            UpdateCropThumbsLayout();
            UpdateResultsText();

            _currentProject.TemplateX = preset.TemplateX;
            _currentProject.TemplateY = preset.TemplateY;
            _currentProject.TemplateScaleX = preset.TemplateScaleX;
            _currentProject.TemplateScaleY = preset.TemplateScaleY;
            _currentProject.TemplateRotation = preset.TemplateRotation;
            _currentProject.TemplateWidth = null;
            _currentProject.TemplateOpacity = preset.TemplateOpacity;
            _currentProject.TemplateFontFamily = preset.TemplateFontFamily;
            _currentProject.TemplateFontSize = preset.TemplateFontSize;
            _currentProject.TemplateFontColor = preset.TemplateFontColor;
            _currentProject.TemplateFontWeight = preset.TemplateFontWeight;
            _currentProject.TemplateIsItalic = preset.TemplateIsItalic;
            _currentProject.TemplateIsUnderlined = preset.TemplateIsUnderlined;
            _currentProject.TemplateCharacterSpacing = preset.TemplateCharacterSpacing;
            _currentProject.IsBackgroundEnabled = preset.IsBackgroundEnabled;
            _currentProject.TemplateBackgroundColor = preset.TemplateBackgroundColor;
            _currentProject.TemplateBackgroundPaddingX = preset.TemplateBackgroundPaddingX;
            _currentProject.TemplateBackgroundPaddingY = preset.TemplateBackgroundPaddingY;
            _currentProject.TemplateBackgroundCornerRadius = preset.TemplateBackgroundCornerRadius;
            _currentProject.TemplateBackgroundOpacity = preset.TemplateBackgroundOpacity;
            _currentProject.IsOutlineEnabled = preset.IsOutlineEnabled;
            _currentProject.TemplateOutlineColor = preset.TemplateOutlineColor;
            _currentProject.TemplateOutlineThickness = preset.TemplateOutlineThickness;
            _currentProject.IsShadowEnabled = preset.IsShadowEnabled;
            _currentProject.TemplateShadowColor = preset.TemplateShadowColor;
            _currentProject.TemplateShadowBlur = preset.TemplateShadowBlur;
            _currentProject.TemplateShadowDepth = preset.TemplateShadowDepth;
            _currentProject.TemplateShadowDirection = preset.TemplateShadowDirection;

            UpdateEditorPanelFromState();
            var newTemplate = _currentProject.GetTemplateAsStyleState();
            ApplyTemplateToAllSubtitlesAndUpdate(newTemplate, _selectedSubtitle);

            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }

        private void LoadProjectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open project",
                Filter = "Project Files (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects"),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    var loadedSnap = subphimv1.Services.ProjectManager.LoadSnapshot(dialog.FileName);
                    if (loadedSnap != null)
                    {
                        ApplySnapshot(loadedSnap);
                    }
                    else
                    {
                        CustomMessageBox.Show("Không thể đọc file project. File có thể bị lỗi hoặc không đúng định dạng.", "Lỗi Tải Project", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Đã xảy ra lỗi khi tải project:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void DeleteProjectMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            DeleteProjectMenuItem.Items.Clear();
            var projectPaths = ProjectManager.GetAllProjectPaths();

            if (!projectPaths.Any())
            {
                var noProjectsItem = new MenuItem { Header = "(Không có project nào)", IsEnabled = false };
                DeleteProjectMenuItem.Items.Add(noProjectsItem);
                return;
            }

            foreach (var path in projectPaths)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileNameWithoutExtension(path),
                    Tag = path
                };
                menuItem.Click += DeleteProjectFile_Click;
                DeleteProjectMenuItem.Items.Add(menuItem);
            }
        }
        private void UpdatePresetMenu()
        {
            PresetMenuItem.Items.Clear();

            var presetNames = PresetManager.GetAllPresetNames();

            if (!presetNames.Any())
            {
                var createFirstItem = new MenuItem { Header = "Tạo preset mới..." };
                createFirstItem.Click += SaveCurrentPreset_Click;
                PresetMenuItem.Items.Add(createFirstItem);
            }
            else
            {
                var saveCurrentItem = new MenuItem { Header = "Lưu preset hiện tại..." };
                saveCurrentItem.Click += SaveCurrentPreset_Click;
                PresetMenuItem.Items.Add(saveCurrentItem);
                PresetMenuItem.Items.Add(new Separator());
                foreach (var name in presetNames)
                {
                    var loadItem = new MenuItem { Header = name };
                    loadItem.Click += LoadPreset_Click;
                    PresetMenuItem.Items.Add(loadItem);
                }
            }
        }

        private void SaveCurrentPreset_Click(object sender, RoutedEventArgs e)
        {

            if (!PresetManager.GetAllPresetNames().Any())
            {
                var result = CustomMessageBox.Show(
                   "Bạn có muốn sử dụng các cài đặt (thông số crop, style phụ đề) của project hiện tại để tạo preset mới không?",
                   "Tạo Preset",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Question
               );
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            string presetName = InputBox.Show("Nhập tên cho preset:", "Lưu Preset");

            if (!string.IsNullOrWhiteSpace(presetName))
            {
                UpdateProjectStateBeforeSave();
                PresetManager.SavePreset(_currentProject, presetName);
                UpdatePresetMenu();
            }
            else
            {
            }
        }

        private void DeleteProjectFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string path)
            {
                string projectName = Path.GetFileNameWithoutExtension(path);
                bool isDeletingCurrentProject = string.Equals(_currentProjectFilePath, path, StringComparison.OrdinalIgnoreCase);
                ProjectManager.DeleteProject(path);
                if (isDeletingCurrentProject)
                {
                    ResetApplicationState(true);
                }

            }
        }

        #endregion
        #region OCR Process Logic (Full Original Logic Ported)

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckGoogleAccountsAndShowGuideAsync()) return;

            TranslateTab.IsChecked = true;
            _masterCts = new CancellationTokenSource();
            UpdateUiForProcessing(true);

            try
            {
                Action<int, string> updateProgressAction = (percent, message) =>
                {
                };

                bool ocrResult = await StartOcrProcessAsync(_masterCts.Token, updateProgressAction);
                UpdateUiForProcessing(false);

                if (ocrResult) { }
                else if (_masterCts.IsCancellationRequested)
                {
                    LogMessage("[OCR] Quá trình OCR đã bị hủy bởi người dùng.", isError: true);
                }
                else
                {
                    UpdateUiForProcessing(false);
                    CustomMessageBox.Show("Có lỗi xảy ra trong quá trình OCR.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateUiForProcessing(false);
                CustomMessageBox.Show($"Lỗi không mong muốn: {ex.Message}", "Lỗi nghiêm trọng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }

        private async Task<bool> HandleOcrPostProcessingAsync(CancellationToken cancellationToken)
        {
            var failedLines = SrtSubtitleLinesView
                .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) &&
                            (l.OriginalText.Contains("HTTP 429") || l.OriginalText.Contains("HTTP 503") || l.OriginalText.Contains("Lỗi API không thể phục hồi")))
                .ToList();

            if (!failedLines.Any())
            {
                return true;
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            foreach (var line in failedLines)
            {
                line.OriginalText = "[Đang OCR lại...]";
            }
            SrtLinesDataGrid.Items.Refresh();
            Action<int, string> dummyProgress = (percent, message) => { };
            bool retrySuccess = await StartOcrProcessAsync(cancellationToken, failedLines, dummyProgress);

            if (retrySuccess)
            {
            }
            else
            {
            }

            return retrySuccess;
        }
        private async Task<bool> StartOcrProcessAsync(CancellationToken cancellationToken, Action<int, string> updateProgressAction)
        {
            updateProgressAction(0, "Đang kiểm tra điều kiện...");
            if (!ValidateOcrPrerequisites(out string srtFilePath, out string imagesDirPath)) return false;

            var initialImageFiles = Directory.GetFiles(imagesDirPath, "*.*", SearchOption.TopDirectoryOnly)
                                           .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                               f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                               f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                               f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                   .OrderBy(f => f, new NaturalStringComparer()).ToList();

            if (!initialImageFiles.Any())
            {
                CustomMessageBox.Show("Không tìm thấy file ảnh nào trong thư mục được chỉ định.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            updateProgressAction(5, "Đang tạo danh sách SRT từ ảnh...");
            var oldSubtitleClips = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle || c.ClipType == TimelineClipType.Text).ToList();
            foreach (var clip in oldSubtitleClips)
            {
                TimelineClips.Remove(clip);
            }
            _currentProject.Subtitles.Clear();
            SrtSubtitleLinesView.Clear();
            int initialSubtitleTrack = -2;
            var newLines = await PrepareSrtViewFromImages(initialImageFiles, srtFilePath, cancellationToken, initialSubtitleTrack);
            if (cancellationToken.IsCancellationRequested) return false;
            await StartOcrProcessAsync(cancellationToken, newLines, updateProgressAction);
            if (cancellationToken.IsCancellationRequested) return false;
            await HandleOcrPostProcessingAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return false;
            if (_currentOcrMode == OcrMode.GoogleCloud)
            {
                var linesToRemove = SrtSubtitleLinesView
                    .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText) && line.OriginalText.Trim().Length == 1)
                    .ToList();

                if (linesToRemove.Any())
                {
                    LogMessage($"[GDRIVE_FILTER] Phát hiện {linesToRemove.Count} dòng có 1 ký tự, tiến hành xóa...");
                    foreach (var line in linesToRemove)
                    {
                        SrtSubtitleLinesView.Remove(line);
                    }
                }
            }
            var validLinesFromOcr = SrtSubtitleLinesView
                .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) &&
                            l.OriginalText != GeminiOcrService.NO_TEXT_INDICATOR &&
                            l.OriginalText != "[Đang chờ OCR...]")
                .ToList();

            var removedAfterMerge = SrtFileUtils.MergeDuplicates(new System.Collections.ObjectModel.ObservableCollection<SrtSubtitleLine>(validLinesFromOcr));
            var finalLines = validLinesFromOcr.Except(removedAfterMerge).ToList();
            SrtFileUtils.ReIndex(new System.Collections.ObjectModel.ObservableCollection<SrtSubtitleLine>(finalLines));
            SrtSubtitleLinesView.Clear();
            _currentProject.Subtitles.Clear();
            var clipsToRemove = TimelineClips.Where(c => c.SourceData is SrtSubtitleLine).ToList();
            foreach (var clip in clipsToRemove) TimelineClips.Remove(clip);

            if (finalLines.Any())
            {
                foreach (var srtLine in finalLines)
                {
                    SrtSubtitleLinesView.Add(srtLine);
                    _currentProject.Subtitles.Add(srtLine);
                    TimelineClips.Add(new TimelineClipViewModel(srtLine));
                }

                RecalculateTrackAssignments();
                UpdateTimelineScaleAndRender();
                SrtFileUtils.SaveToFile(srtFilePath, SrtSubtitleLinesView.ToList());
                return true;
            }
            else
            {
                RecalculateTrackAssignments();
                UpdateTimelineScaleAndRender();
                return true;
            }
        }
        private async Task<bool> StartOcrProcessAsync(CancellationToken cancellationToken, List<SrtSubtitleLine> linesToProcess, Action<int, string> updateProgressAction)
        {
            string tempRetryDir = null;
            var imagePathsToProcess = new List<string>();
            var pathToLineMap = new Dictionary<string, SrtSubtitleLine>();

            bool isRetrySession = linesToProcess.Any(l => l.OriginalText == "[Đang OCR lại...]");

            if (isRetrySession)
            {
                tempRetryDir = Path.Combine(Path.GetTempPath(), $"ocr_retry_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempRetryDir);
                foreach (var line in linesToProcess)
                {
                    try
                    {
                        string newPath = Path.Combine(tempRetryDir, Path.GetFileName(line.ImagePath));
                        File.Copy(line.ImagePath, newPath, true);
                        imagePathsToProcess.Add(newPath);
                        pathToLineMap[newPath] = line;
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
            else
            {
                imagePathsToProcess = linesToProcess.Select(l => l.ImagePath).ToList();
                foreach (var line in linesToProcess)
                {
                    pathToLineMap[line.ImagePath] = line;
                }
            }

            if (!imagePathsToProcess.Any())
            {
                if (tempRetryDir != null) Directory.Delete(tempRetryDir, true);
                return false;
            }
            string rawTextsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "raw_texts_ocr");
            try { if (Directory.Exists(rawTextsDir)) Directory.Delete(rawTextsDir, true); Directory.CreateDirectory(rawTextsDir); }
            catch (Exception ex) { LogMessage($"[OCR][ERROR] Không thể tạo thư mục tạm: {ex.Message}", true); return false; }

            var filesToOcr = await PreprocessImagesAsync(imagePathsToProcess, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !filesToOcr.Any())
            {
                if (tempRetryDir != null) Directory.Delete(tempRetryDir, true);
                return false;
            }

            bool success = await RunOcrTasks(filesToOcr, rawTextsDir, cancellationToken, updateProgressAction, pathToLineMap);


            if (tempRetryDir != null)
            {
                try
                {
                    Directory.Delete(tempRetryDir, true);
                }
                catch (Exception ex)
                {
                }
            }

            return success;
        }
        private Task<bool> StartOcrProcessAsync(CancellationToken cancellationToken)
        {
            Action<int, string> dummyProgress = (percent, message) => { };
            return StartOcrProcessAsync(cancellationToken, dummyProgress);
        }
        private async void AutomaticButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckGoogleAccountsAndShowGuideAsync()) return;
            var (canProcess, message) = await ApiService.TryStartProcessingAsync();
            if (!canProcess)
            {
                CustomMessageBox.Show(message, "Đã đạt giới hạn", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!await EnsureVsfIsAvailableAsync()) return;
            TranslateTab.IsChecked = true;
            _masterCts = new CancellationTokenSource();
            var token = _masterCts.Token;

            _isAutoProcessing = true;
            UpdateUiForProcessing(true);
            bool vsfSuccess = false;
            bool ocrSuccess = false;
            bool srtTranslateSuccess = false;

            try
            {
                VsfService.VsfParameters vsfParams;
                var selectedVideoClips = TimelineClips.Where(c => c.IsSelected && c.ClipType == TimelineClipType.Video).ToList();

                if (selectedVideoClips.Count == 1)
                {
                    var clipVM = selectedVideoClips.First();
                    var mediaAsset = clipVM.SourceData as MediaAsset;
                    if (mediaAsset != null)
                    {
                        vsfParams = GetVsfParametersFromUi();
                        vsfParams.VideoPath = mediaAsset.FilePath;
                        vsfParams.StartTime = mediaAsset.TrimStartOffset.ToString(@"hh\:mm\:ss\:fff");
                        var effectiveDurationInSource = mediaAsset.Duration - mediaAsset.TrimStartOffset - mediaAsset.TrimEndOffset;
                        vsfParams.EndTime = (mediaAsset.TrimStartOffset + effectiveDurationInSource).ToString(@"hh\:mm\:ss\:fff");
                    }
                    else
                    {
                        vsfParams = GetVsfParametersFromUi();
                    }
                }
                else
                {
                    vsfParams = GetVsfParametersFromUi();
                }

                (vsfSuccess, string vsfResultPath) = await _vsfService.RunVSFProcessAsync(vsfParams, token);
                if (!vsfSuccess) throw new Exception("Tìm Sub thất bại. Kiểm tra Log.");
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                ImagesEntry.Text = vsfResultPath;

                ocrSuccess = await StartOcrProcessAsync(token);

                if (!ocrSuccess && !token.IsCancellationRequested) throw new Exception("OCR thất bại. Kiểm tra Log.");
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                if (!SrtSubtitleLinesView.Any(l => !string.IsNullOrWhiteSpace(l.OriginalText) && !l.OriginalText.StartsWith("[Lỗi")))
                {
                    UpdateUiForProcessing(false);
                    CustomMessageBox.Show("Quá trình OCR không tìm thấy văn bản nào để dịch.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                    throw new InvalidOperationException("OCR did not produce any valid lines.");
                }

                IsSrtTranslating = true;
                await TranslateSrtLogic(token);
                srtTranslateSuccess = !SrtSubtitleLinesView.Any(l => l.TranslatedText != null && _srtErrorMarkers.Contains(l.TranslatedText));
                IsSrtTranslating = false;

                if (srtTranslateSuccess && !string.IsNullOrWhiteSpace(SubtitleEntry.Text))
                {
                    try
                    {
                        SrtFileUtils.SaveToFile(SubtitleEntry.Text, SrtSubtitleLinesView.ToList());
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"[AUTOMATIC] Lỗi lưu file SRT cuối cùng: {ex.Message}", true);
                    }
                }

                if (vsfSuccess && ocrSuccess && srtTranslateSuccess)
                {
                    UpdateUiForProcessing(false);

                }
                else
                {
                    throw new Exception("Một hoặc nhiều bước trong quy trình tự động đã thất bại.");
                }
            }
            catch (InvalidOperationException ocrEx) when (ocrEx.Message.Contains("OCR did not produce"))
            {
                LogMessage("[AUTOMATIC] OCR không tạo ra dòng nào, kết thúc quy trình.");
            }
            catch (OperationCanceledException)
            {
                LogMessage("[AUTOMATIC] Quy trình tự động đã bị hủy bởi người dùng.");
            }
            catch (Exception ex)
            {
                UpdateUiForProcessing(false);
                CustomMessageBox.Show($"Quy trình tự động đã thất bại.\n\nLỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isAutoProcessing = false;
                IsSrtTranslating = false;
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }
        private bool ValidateOcrPrerequisites(out string srtFilePath, out string imagesDirPath)
        {
            srtFilePath = SubtitleEntry.Text;
            imagesDirPath = ImagesEntry.Text;

            if (string.IsNullOrWhiteSpace(srtFilePath))
            {
                string baseName = !string.IsNullOrWhiteSpace(VideoEntry.Text)
                    ? Path.GetFileNameWithoutExtension(VideoEntry.Text)
                    : (!string.IsNullOrWhiteSpace(imagesDirPath) ? new DirectoryInfo(imagesDirPath).Name : "untitled_ocr");
                srtFilePath = Path.Combine(GetResolvedSubtitleOutputDirectory(), $"{baseName}.srt");
                SubtitleEntry.Text = srtFilePath;
            }

            if (string.IsNullOrWhiteSpace(imagesDirPath) || !Directory.Exists(imagesDirPath))
            {
                CustomMessageBox.Show("Thư mục ảnh không có ảnh.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (_currentOcrMode == OcrMode.GeminiApi && !_geminiApiKeysOcr.Any())
            {
                CustomMessageBox.Show("Chế độ Gemini API OCR yêu cầu API Key. Vui lòng vào Cài đặt.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private async Task<List<SrtSubtitleLine>> PrepareSrtViewFromImages(List<string> imageFiles, string srtFilePath, CancellationToken token, int initialTrackIndex, TimeSpan? timeOffset = null, int startIndex = 1)
        {
            if (timeOffset == null)
            {
                await Dispatcher.InvokeAsync(() => {
                    SrtSubtitleLinesView.Clear();
                    _currentSrtFilePath = srtFilePath;
                });
            }

            var newLines = SrtFileUtils.CreateSrtLinesFromImageFiles(imageFiles, timeOffset ?? TimeSpan.Zero, startIndex);
            var projectTemplate = GetTemplateAsStyleState();
            foreach (var srtLine in newLines)
            {
                srtLine.TrackIndex = initialTrackIndex;

                srtLine.Style = projectTemplate.Clone();
                srtLine.Style.Width = null;
                srtLine.Style.FixedTextBoxWidth = 0;
            }
            if (timeOffset == null)
            {
                foreach (var srtLine in newLines)
                {
                    if (token.IsCancellationRequested) break;
                    await Dispatcher.InvokeAsync(() => SrtSubtitleLinesView.Add(srtLine));
                }
            }
            return newLines;
        }
        private async Task<List<string>> PreprocessImagesAsync(List<string> imageFiles, CancellationToken token)
        {
            var filesToOcr = new System.Collections.Concurrent.ConcurrentBag<string>();

            if (_selectedVsfProcessingMode == VsfProcessingMode.CleanAndCreateTxtImages)
            {
                int processedCount = 0;
                await Task.Run(() =>
                {
                    Parallel.ForEach(imageFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token }, imagePath =>
                    {
                        if (ImageProcessingUtils.CropHorizontalTextRegion(imagePath, imagePath, 128, true) || File.Exists(imagePath))
                        {
                            filesToOcr.Add(imagePath);
                        }
                        Interlocked.Increment(ref processedCount);
                    });
                }, token);
            }
            else
            {
                foreach (var imagePath in imageFiles) filesToOcr.Add(imagePath);
            }

            return filesToOcr.ToList();
        }
        private async Task<bool> RunOcrTasks(List<string> imagePathsToProcess, string rawTextsDir, CancellationToken token, Action<int, string> updateProgressAction, Dictionary<string, SrtSubtitleLine> pathToLineMap = null)
        {
            long totalCompleted = 0;
            bool anyBatchFailed = false;
            if (pathToLineMap == null)
            {
                pathToLineMap = SrtSubtitleLinesView.Where(l => !string.IsNullOrEmpty(l.ImagePath))
                                                     .ToDictionary(l => l.ImagePath, l => l);
            }

            if (_currentOcrMode == OcrMode.GeminiApi)
            {
                int batchSize = _geminiImagesPerRequestOcr;
                var batches = imagePathsToProcess
                    .Select((file, index) => new { file, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.file).ToList())
                    .ToList();

                using SemaphoreSlim limiter = new SemaphoreSlim(5);
                var tasks = new List<Task>();
                RebuildOcrServices();

                foreach (var batch in batches)
                {
                    if (token.IsCancellationRequested) break;
                    await limiter.WaitAsync(token);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var batchResults = await _geminiServiceOcr.ProcessImagesAsync(batch, token);
                            if (token.IsCancellationRequested) return;

                            await Dispatcher.InvokeAsync(() =>
                            {
                                foreach (var result in batchResults)
                                {
                                    if (pathToLineMap.TryGetValue(result.imagePath, out var lineToUpdate))
                                    {
                                        if (result.error == null)
                                        {
                                            lineToUpdate.OriginalText = SrtFileUtils.ProcessOcrText(result.ocrText, _currentOcrMode);
                                        }
                                        else
                                        {
                                            lineToUpdate.OriginalText = $"[Lỗi OCR: {result.error}]";
                                            anyBatchFailed = true;
                                        }
                                        var clipVMToUpdate = TimelineClips.FirstOrDefault(c => c.SourceData == lineToUpdate);
                                        if (clipVMToUpdate != null)
                                        {
                                            clipVMToUpdate.DisplayName = lineToUpdate.OriginalText.Replace(@"\N", " ").Replace(Environment.NewLine, " ");
                                        }
                                    }
                                }
                                Interlocked.Add(ref totalCompleted, batch.Count);
                                SrtLinesDataGrid.Items.Refresh();
                                int percentage = (int)((double)totalCompleted / imagePathsToProcess.Count * 90) + 10;
                                updateProgressAction?.Invoke(percentage, $"Đang xử lý... {totalCompleted} / {imagePathsToProcess.Count}");
                            });
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception) { anyBatchFailed = true; }
                        finally { limiter.Release(); }
                    }, token));
                }
                await Task.WhenAll(tasks);
                return !anyBatchFailed && !token.IsCancellationRequested;
            }
            else
            {
                RebuildOcrServices();

                if (_googleAccounts == null || !_googleAccounts.Any())
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CustomMessageBox.Show(
                            "Lỗi",
                            "Lỗi Cấu Hình",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    });
                    return false;
                }
                var activeAccounts = await SelectActiveAccountsForThisRunAsync();
                if (activeAccounts == null || !activeAccounts.Any())
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CustomMessageBox.Show(
                            "Không thể OCR.",
                            "Lỗi OCR",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    });
                    return false;
                }
                LogMessage(
                    $"[OCR][AutoTune] Sử dụng {activeAccounts.Count}/{_googleAccounts.Count} tài khoản Google cho lượt OCR này.",
                    false
                );
                var assignments = new Dictionary<GoogleAccountContext, List<string>>();
                foreach (var acc in activeAccounts)
                {
                    assignments[acc] = new List<string>();
                }

                for (int i = 0; i < imagePathsToProcess.Count; i++)
                {
                    var account = activeAccounts[i % activeAccounts.Count];
                    assignments[account].Add(imagePathsToProcess[i]);
                }
                var allAccountTasks = new List<Task>();

                foreach (var kvp in assignments)
                {
                    var account = kvp.Key;
                    var imagesForThisAccount = kvp.Value;
                    if (imagesForThisAccount == null || imagesForThisAccount.Count == 0)
                        continue;

                    allAccountTasks.Add(Task.Run(async () =>
                    {
                        var results = await account.OcrProcessor.ProcessBatchAsync(
                            imagesForThisAccount,
                            rawTextsDir,
                            account.DriveFolderId,
                            token
                        );

                        if (token.IsCancellationRequested) return;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var result in results)
                            {
                                if (pathToLineMap.TryGetValue(result.imagePath, out var lineToUpdate))
                                {
                                    if (result.error == null)
                                    {
                                        lineToUpdate.OriginalText =
                                            SrtFileUtils.ProcessOcrText(result.ocrText, _currentOcrMode);
                                    }
                                    else
                                    {
                                        lineToUpdate.OriginalText = $"[Lỗi OCR: {result.error}]";
                                        anyBatchFailed = true;
                                    }
                                    var clipVMToUpdate = TimelineClips.FirstOrDefault(c => c.SourceData == lineToUpdate);
                                    if (clipVMToUpdate != null)
                                    {
                                        clipVMToUpdate.DisplayName = lineToUpdate.OriginalText
                                            .Replace(@"\N", " ")
                                            .Replace(Environment.NewLine, " ");
                                    }
                                }
                            }

                            long completedInThisTask = results.Count;
                            Interlocked.Add(ref totalCompleted, completedInThisTask);

                            SrtLinesDataGrid.Items.Refresh();

                            int percentage = (int)((double)totalCompleted / imagePathsToProcess.Count * 90) + 10;
                            updateProgressAction?.Invoke(
                                percentage,
                                $"Đang xử lý... {totalCompleted} / {imagePathsToProcess.Count}"
                            );
                        });
                    }, token));
                }

                await Task.WhenAll(allAccountTasks);

                return !anyBatchFailed && !token.IsCancellationRequested;
            }
        }
        /// <summary>
        /// Đồng bộ lựa chọn của VsfThreadComboBox với cấu hình đã lưu.
        /// </summary>
        private void InitializeVsfThreadComboBox()
        {
            try
            {
                int parsedSearch = int.TryParse(_vsfNumThreadsSearch, out var s) ? s : -1;
                int parsedClean = int.TryParse(_vsfNumThreadsClean, out var c) ? c : -1;

                string valueToSelect;
                if (parsedSearch == -1 && parsedClean == -1)
                {
                    valueToSelect = "-1"; // Tự động
                }
                else if (parsedSearch > 0 && parsedSearch == parsedClean)
                {
                    valueToSelect = parsedSearch.ToString();
                }
                else
                {
                    // Nếu giá trị không đồng nhất, chọn "Tự động" làm mặc định
                    valueToSelect = "-1";
                }

                if (VsfThreadComboBox != null)
                {
                    foreach (var item in VsfThreadComboBox.Items)
                    {
                        if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == valueToSelect)
                        {
                            VsfThreadComboBox.SelectedItem = cbi;
                            return;
                        }
                    }
                    // Nếu không tìm thấy, chọn "Tự động"
                    if (VsfThreadComboBox.Items.Count > 0)
                    {
                        VsfThreadComboBox.SelectedIndex = 0;
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi giao diện
            }
        }
        private void VsfThreadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Chỉ xử lý sau khi UI đã được tải hoàn toàn
                if (!(sender is ComboBox cb) || !this.IsLoaded) return;

                int selectedThreads;

                // Lấy giá trị Tag từ ComboBoxItem đã chọn
                string tag = (cb.SelectedItem as ComboBoxItem)?.Tag?.ToString();

                if (string.Equals(tag, "-1", StringComparison.OrdinalIgnoreCase))
                {
                    // Chế độ "Tự động": tính toán dựa trên CPU và RAM
                    selectedThreads = EstimateRecommendedThreadCountForVsf();
                    if (VsfThreadNoteText != null)
                    {
                        VsfThreadNoteText.Text = $"Tự động: {selectedThreads} luồng.";
                    }
                }
                else
                {
                    // Người dùng chọn số luồng cụ thể
                    if (!int.TryParse(tag, out selectedThreads)) selectedThreads = 2; // Giá trị dự phòng
                    if (VsfThreadNoteText != null)
                    {
                        VsfThreadNoteText.Text = ""; // Ẩn ghi chú khi người dùng chọn thủ công
                    }
                }

                // Cập nhật cả hai tham số cho VSF (Search và Clean)
                _vsfNumThreadsSearch = selectedThreads.ToString();
                _vsfNumThreadsClean = selectedThreads.ToString();

                // Lưu cấu hình ngay lập tức để thay đổi có hiệu lực
                SaveConfiguration();
            }
            catch
            {
                // Bỏ qua lỗi để tránh crash ứng dụng
            }
        }
        private int EstimateRecommendedThreadCountForVsf()
        {
            try
            {
                int logicalProcessorCount = Environment.ProcessorCount;

                // Các mức luồng được phép mà người dùng có thể chọn
                int[] allowedThreads = new[] { 2, 4, 6, 8, 12, 16 };

                // Mục tiêu là sử dụng khoảng 50% số luồng CPU, tối thiểu là 2
                int targetThreads = Math.Max(2, logicalProcessorCount / 2);

                // Tìm mức luồng LỚN NHẤT trong danh sách cho phép nhưng KHÔNG VƯỢT QUÁ mục tiêu.
                // Điều này đảm bảo chế độ tự động luôn an toàn và chừa lại tài nguyên.
                int recommendedThreads = allowedThreads
                    .Where(threads => threads <= targetThreads)
                    .DefaultIfEmpty(2) // Nếu không có mức nào phù hợp (ví dụ CPU 3 luồng), dùng 2.
                    .Max();

                return recommendedThreads;
            }
            catch
            {
                // Fallback an toàn nếu có lỗi
                return 4;
            }
        }
        private async Task<int> EstimateRecommendedAccountCountAsync()
        {
            int logicalCores = Environment.ProcessorCount;
            float cpuUsage = 0f;
            using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
            {
                cpuCounter.NextValue();
                await Task.Delay(250);
                cpuUsage = cpuCounter.NextValue();
            }
            var ci = new ComputerInfo();
            ulong availableBytes = ci.AvailablePhysicalMemory;
            double availableGB = availableBytes / (1024.0 * 1024.0 * 1024.0);
            int baseAccounts;
            if (logicalCores <= 4 || availableGB < 4.0)
            {
                baseAccounts = 4;
            }
            else if (logicalCores <= 8 || availableGB < 8.0)
            {
                baseAccounts = 6;
            }
            else
            {
                baseAccounts = 10;
            }
            if (cpuUsage > 60f && baseAccounts > 4)
            {
                baseAccounts -= 2;
            }
            return baseAccounts;
        }
        private async Task<List<GoogleAccountContext>> SelectActiveAccountsForThisRunAsync()
        {
            if (_googleAccounts == null || _googleAccounts.Count == 0)
            {
                return new List<GoogleAccountContext>();
            }
            int recommendedAccounts = await EstimateRecommendedAccountCountAsync();
            int takeCount = Math.Min(recommendedAccounts, _googleAccounts.Count);
            var ordered = _googleAccounts
                .OrderBy(acc => acc.AccountName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selected = ordered.Take(takeCount).ToList();
            return selected;
        }

        private void RebuildOcrServices()
        {
            _geminiServiceOcr = new GeminiOcrService(_geminiApiKeysOcr, _geminiRequestsPerMinuteOcr, _geminiEnableMultiKeyOcr, _selectedGeminiModelOcr)
            {
                LogMessage = (msg) => LogMessage(msg)
            };
            LoadGoogleAccounts();
        }
        public class NaturalStringComparer : IComparer<string>
        {
            private static readonly Regex _numberRegex = new Regex(@"\d+", RegexOptions.Compiled);

            public int Compare(string x, string y)
            {
                if (x == null) return y == null ? 0 : -1;
                if (y == null) return 1;

                var xParts = _numberRegex.Split(x);
                var yParts = _numberRegex.Split(y);

                int minParts = Math.Min(xParts.Length, yParts.Length);
                for (int i = 0; i < minParts; i++)
                {
                    int comparison = string.Compare(xParts[i], yParts[i], StringComparison.OrdinalIgnoreCase);
                    if (comparison != 0) return comparison;

                    if (i < xParts.Length - 1 && i < yParts.Length - 1)
                    {
                        var xNumber = ExtractNumber(x, i);
                        var yNumber = ExtractNumber(y, i);

                        comparison = xNumber.CompareTo(yNumber);
                        if (comparison != 0) return comparison;
                    }
                }

                return xParts.Length.CompareTo(yParts.Length);
            }

            private static int ExtractNumber(string input, int index)
            {
                var match = _numberRegex.Match(input, index);
                return match.Success ? int.Parse(match.Value) : 0;
            }
        }

        #endregion
        #region DataGrid Context Menu Logic (OCR/Translate Retry)
        private void DataGridRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is DataGridRow row) || !(row.DataContext is SrtSubtitleLine srtLineOver))
            {
                e.Handled = true;
                return;
            }

            var rowContextMenu = new ContextMenu();
            var selectedItems = SrtLinesDataGrid.SelectedItems.Cast<SrtSubtitleLine>().ToList();
            bool isCurrentRowSelected = selectedItems.Contains(srtLineOver);
            if (!string.IsNullOrWhiteSpace(srtLineOver.OriginalText))
            {
                MenuItem retrySingle = new MenuItem { Header = $"Dịch lại dòng {srtLineOver.Index}", DataContext = srtLineOver };
                retrySingle.Click += RetryTranslateSingleSrtLine_Click;
                rowContextMenu.Items.Add(retrySingle);
            }
            if (selectedItems.Count > 1 && isCurrentRowSelected)
            {
                var validItemsToTranslate = selectedItems.Where(l => !string.IsNullOrWhiteSpace(l.OriginalText)).ToList();
                if (validItemsToTranslate.Any())
                {
                    MenuItem retrySelected = new MenuItem { Header = $"Dịch lại {validItemsToTranslate.Count} dòng đã chọn" };
                    retrySelected.Click += RetryTranslateSelectedSrtLines_Click;
                    rowContextMenu.Items.Add(retrySelected);
                }
            }
            bool hasOcrableItems = false;
            if (!string.IsNullOrWhiteSpace(srtLineOver.ImagePath) && File.Exists(srtLineOver.ImagePath))
            {
                if (rowContextMenu.HasItems) rowContextMenu.Items.Add(new Separator());
                MenuItem ocrSingle = new MenuItem { Header = $"OCR lại dòng {srtLineOver.Index}", DataContext = srtLineOver };
                ocrSingle.Click += ReOcrSingleSrtLine_Click;
                rowContextMenu.Items.Add(ocrSingle);
                hasOcrableItems = true;
            }
            if (selectedItems.Count > 1 && isCurrentRowSelected)
            {
                var validItemsToOcr = selectedItems.Where(l => !string.IsNullOrWhiteSpace(l.ImagePath) && File.Exists(l.ImagePath)).ToList();
                if (validItemsToOcr.Any())
                {
                    if (!hasOcrableItems && rowContextMenu.HasItems) rowContextMenu.Items.Add(new Separator());
                    MenuItem ocrSelected = new MenuItem { Header = $"OCR lại {validItemsToOcr.Count} dòng đã chọn" };
                    ocrSelected.Click += ReOcrSelectedSrtLines_Click;
                    rowContextMenu.Items.Add(ocrSelected);
                }
            }
            row.ContextMenu = rowContextMenu.HasItems ? rowContextMenu : null;
        }
        private async void RetryTranslateSingleSrtLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SrtSubtitleLine lineToRetry)
            {
                await RunTranslationForSpecificLines(new List<SrtSubtitleLine> { lineToRetry });
            }
        }

        private async void RetryTranslateSelectedSrtLines_Click(object sender, RoutedEventArgs e)
        {
            var linesToRetry = SrtLinesDataGrid.SelectedItems.Cast<SrtSubtitleLine>()
                                 .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText))
                                 .ToList();
            if (linesToRetry.Any())
            {
                await RunTranslationForSpecificLines(linesToRetry);
            }
        }

        private async void ReOcrSingleSrtLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SrtSubtitleLine lineToOcr)
            {
                await RunOcrForSpecificLines(new List<SrtSubtitleLine> { lineToOcr });
            }
        }

        private async void ReOcrSelectedSrtLines_Click(object sender, RoutedEventArgs e)
        {
            var linesToOcr = SrtLinesDataGrid.SelectedItems.Cast<SrtSubtitleLine>().ToList();
            if (linesToOcr.Any())
            {
                await RunOcrForSpecificLines(linesToOcr);
            }
        }
        private async Task RunTranslationForSpecificLines(List<SrtSubtitleLine> linesToTranslate)
        {
            if (IsSrtTranslating)
            {
                CustomMessageBox.Show("Một tác vụ dịch đang chạy, vui lòng chờ hoàn tất.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _masterCts = new CancellationTokenSource();
            IsSrtTranslating = true;
            UpdateUiForProcessing(true);

            try
            {
                foreach (var line in linesToTranslate) line.TranslatedText = "[Đang dịch lại...]";
                await TranslateSrtLogic(_masterCts.Token, linesToTranslate);
            }
            finally
            {
                IsSrtTranslating = false;
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }
        private async Task RunOcrForSpecificLines(List<SrtSubtitleLine> linesToOcr)
        {
            if (_isAutoProcessing || IsSrtTranslating)
            {
                CustomMessageBox.Show("Một tác vụ khác đang chạy. Vui lòng chờ hoàn tất.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var validLines = linesToOcr
                .Where(l => !string.IsNullOrWhiteSpace(l.ImagePath) && File.Exists(l.ImagePath))
                .ToList();

            if (!validLines.Any())
            {
                return;
            }
            UpdateUiForProcessing(true);
            _masterCts = new CancellationTokenSource();

            try
            {
                foreach (var line in validLines)
                {
                    line.OriginalText = "[Đang OCR lại...]";
                }
                string rawTextsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "raw_texts_re_ocr");
                if (Directory.Exists(rawTextsDir)) Directory.Delete(rawTextsDir, true);
                Directory.CreateDirectory(rawTextsDir);

                var imagePaths = validLines.Select(l => l.ImagePath).ToList();
                Action<int, string> dummyUpdateProgressAction = (percent, message) => {
                };
                bool ocrSuccess = await RunOcrTasks(imagePaths, rawTextsDir, _masterCts.Token, dummyUpdateProgressAction);
                UpdateUiForProcessing(false);
                if (ocrSuccess && !_masterCts.IsCancellationRequested)
                {
                    CustomMessageBox.Show("Hoàn tất OCR lại các dòng đã chọn.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    CustomMessageBox.Show("Quá trình OCR lại đã bị hủy hoặc gặp lỗi.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                UpdateUiForProcessing(false);
                CustomMessageBox.Show($"Lỗi nghiêm trọng khi OCR lại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
            }
        }
        #endregion
        #region Timeline, Waveform & Volume Logic (New Implementation)

        private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;

                double newPixelsPerSecond;
                if (e.Delta > 0)
                {
                    newPixelsPerSecond = this._pixelsPerSecond * ZOOM_STEP;
                }
                else
                {
                    newPixelsPerSecond = this._pixelsPerSecond / ZOOM_STEP;
                }
                UpdateZoom(newPixelsPerSecond, e);
            }
        }
        private double GetDynamicMinPixelsPerSecond()
        {
            // Nếu chưa có viewport, dùng min
            if (_totalTimelineDuration.TotalSeconds <= 0 || TimelineScrollViewer.ViewportWidth <= 0)
                return MIN_PIXELS_PER_SECOND;

            // Fit toàn bộ timeline vào viewport khi zoom-out hết cỡ
            double fitAll = TimelineScrollViewer.ViewportWidth / _totalTimelineDuration.TotalSeconds;

            // Không cho nhỏ quá 0.01 px/s không vượt quá min cố định
            double softLowerBound = Math.Max(0.01, fitAll * 0.95);
            return Math.Min(MIN_PIXELS_PER_SECOND, softLowerBound);
        }
        private void UpdateZoom(double newPixelsPerSecond, MouseWheelEventArgs e)
        {
            if (_totalTimelineDuration.TotalSeconds <= 0 || !IsLoaded) return;

            double oldPixelsPerSecond = this._pixelsPerSecond;
            if (Math.Abs(oldPixelsPerSecond) < 0.001) return;

            // Dùng min động để có thể fit toàn timeline khi zoom-out
            double minDyn = GetDynamicMinPixelsPerSecond();
            double clampedPps = Math.Clamp(newPixelsPerSecond, minDyn, MAX_PIXELS_PER_SECOND);

            System.Windows.Point mousePosInViewport = e.GetPosition(TimelineScrollViewer);
            double mouseAbsoluteX_Before = TimelineScrollViewer.HorizontalOffset + mousePosInViewport.X;
            TimeSpan timeAtMouse = TimeSpan.FromSeconds(mouseAbsoluteX_Before / oldPixelsPerSecond);

            this._pixelsPerSecond = clampedPps;

            double mouseAbsoluteX_After = timeAtMouse.TotalSeconds * this._pixelsPerSecond;
            double newScrollOffset = mouseAbsoluteX_After - mousePosInViewport.X;
            TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newScrollOffset));

            UpdateTimelineScaleAndRender();
        }
        void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0 || e.ViewportWidthChange != 0 || e.ExtentWidthChange != 0)
            {
                _scrollDirty = true;
                if (!_scrollRenderDebounce.IsEnabled)
                    _scrollRenderDebounce.Start();
            }
        }
        private void UpdatePositionMarkerVisuals()
        {
            if (this._pixelsPerSecond <= 0 || TracksContainerGrid.ActualWidth <= 0 || _totalTimelineDuration.Ticks <= 0)
            {
                PositionMarkerThumb.Visibility = Visibility.Collapsed;
                return;
            }

            double absoluteX = _playhead.TotalSeconds * this._pixelsPerSecond;
            double visualX = absoluteX - TimelineScrollViewer.HorizontalOffset;

            if (double.IsNaN(visualX) || double.IsInfinity(visualX))
            {
                PositionMarkerThumb.Visibility = Visibility.Collapsed;
                return;
            }
            PositionMarkerThumb.Visibility = Visibility.Visible;
            if (PositionMarkerThumb.RenderTransform is TranslateTransform t)
            {
                t.X = visualX;
                t.Y = 0;
            }
            else
            {
                PositionMarkerThumb.RenderTransform = new TranslateTransform(visualX, 0);
            }
        }
        private void UpdatePlaybackUI(TimeSpan currentTime)
        {
            currentTimeTextBlock.Text = currentTime.ToString(@"hh\:mm\:ss\.fff");
            totalDurationTextBlock.Text = _actualContentDuration > TimeSpan.Zero ? _actualContentDuration.ToString(@"hh\:mm\:ss\.fff") : "00:00:00.000";
            UpdatePositionMarkerVisuals();
        }
        private TimeSpan ComputePositionInClip(TimeSpan timelineTime, TimelineClipViewModel clip)
        {
            if (clip == null) return TimeSpan.Zero;
            var pos = timelineTime - clip.StartTime;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (clip.SourceData is MediaAsset ma)
                pos = pos + ma.TrimStartOffset;

            var clipDur = clip.Duration;
            var guard = TimeSpan.FromMilliseconds(40);
            if (clipDur <= guard) return TimeSpan.Zero;
            if (pos > clipDur - guard) pos = clipDur - guard;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            return pos;
        }
        private async void FFMEPlayer_MediaEnded(object sender, EventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (_isSwitchingVideoSource)
                {
                    return;
                }
                var currentClip = _activeVideoClip;
                if (currentClip == null)
                {
                    await PauseTimelinePlayback();
                    return;
                }
                var nextClip = FindNextVideoClipAfter(currentClip.EndTime - _clipEpsilon);
                if (nextClip != null)
                {
                    _isSwitchingSourceDueToPlayback = true;
                    try
                    {
                        _playhead = nextClip.StartTime;
                        UpdatePlaybackUI(_playhead);
                        UpdateSubtitleForCurrentTime(_playhead);
                        await SwitchActiveVideoClip(nextClip, resume: true);
                    }
                    finally
                    {
                        _isSwitchingSourceDueToPlayback = false;
                    }
                }
                else
                {
                    _playhead = currentClip.EndTime;
                    UpdatePlaybackUI(_playhead);
                    UpdateSubtitleForCurrentTime(_playhead);
                    await PauseTimelinePlayback();
                }
            });
        }
        private TimelineClipViewModel FindNextVideoClipAfter(TimeSpan timeOnTimeline)
        {
            var orderedVideoClips = TimelineClips
                .Where(c => c != null && c.ClipType == TimelineClipType.Video)
                .OrderBy(c => c.StartTime)
                .ToList();
            var threshold = timeOnTimeline + _clipEpsilon;
            foreach (var clip in orderedVideoClips)
            {
                if (clip.StartTime > threshold)
                {
                    return clip;
                }
            }
            return null;
        }
        private async void FFMEPlayer_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            if (FFMEPlayer.NaturalVideoWidth > 0 && FFMEPlayer.NaturalVideoHeight > 0)
            {
                _videoNativeWidth = FFMEPlayer.NaturalVideoWidth;
                _videoNativeHeight = FFMEPlayer.NaturalVideoHeight;
            }
            UpdatePlayerLayout();
            UpdateCropThumbsLayout();
            EnsureInitialTemplateWidthFitsVideo();
            var targetTimelineTime = _pendingSeekForNewSource ?? _playhead;
            _pendingSeekForNewSource = null;

            await Dispatcher.InvokeAsync(async () =>
            {
                var token = NewTransportVersion();
                try
                {
                    if (_activeVideoClip == null) return;

                    if (_activeVideoClip.SourceData is MediaAsset mediaAsset)
                    {
                        FFMEPlayer.SpeedRatio = mediaAsset.Speed;
                        FFMEPlayer.Volume = Math.Pow(10, mediaAsset.VolumeDb / 20.0);
                    }

                    var positionInClip = ComputePositionInClip(targetTimelineTime, _activeVideoClip);

                    BlackoutOn();
                    await FFMEPlayer.Seek(positionInClip);
                    await WaitForSeekSettleAsync(positionInClip, token);

                    _playhead = targetTimelineTime;
                    UpdatePlaybackUI(_playhead);
                    UpdateSubtitleForCurrentTime(_playhead);

                    if (_pendingWasPlaying)
                    {
                        await FFMEPlayer.Play();
                        if (!_isTimelinePlaying)
                        {
                            _isTimelinePlaying = true;
                            playPauseButton.Content = "\uE769";
                            _renderStopwatch.Reset();
                            _renderStopwatch.Start();
                        }
                    }
                }
                finally
                {
                    BlackoutOff();
                    _isSwitchingVideoSource = false;
                }
            });
        }
        private void FFMEPlayer_PositionChanged(object sender, Unosquare.FFME.Common.PositionChangedEventArgs e)
        {
            if (!_isTimelinePlaying || _isUserDraggingPlayhead || _activeVideoClip == null || _isSwitchingVideoSource || _isExplicitSeekInProgress)
            {
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                if (!_isSwitchingVideoSource)
                {
                    var mediaAsset = _activeVideoClip.SourceData as MediaAsset;
                    var trimStart = mediaAsset?.TrimStartOffset ?? TimeSpan.Zero;
                    _playhead = _activeVideoClip.StartTime + e.Position - trimStart;

                    if (_actualContentDuration > TimeSpan.Zero && _playhead > _actualContentDuration)
                    {
                        _playhead = _actualContentDuration;
                    }
                }
            });
        }

        private void SelectedClip_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var changedAsset = sender as MediaAsset;
            if (changedAsset == null) return;
            var selectedClips = TimelineClips
                .Where(c => c.IsSelected && c.SourceData is MediaAsset)
                .ToList();
            if (!selectedClips.Any()) return;
            var transformProperties = new[] { nameof(MediaAsset.Scale), nameof(MediaAsset.ScaleX), nameof(MediaAsset.ScaleY), nameof(MediaAsset.PositionX), nameof(MediaAsset.PositionY), nameof(MediaAsset.Rotation), nameof(MediaAsset.IsUniformScale), nameof(MediaAsset.Speed), nameof(MediaAsset.VolumeDb) };
            if (!transformProperties.Contains(e.PropertyName)) return;
            object newValue = e.PropertyName switch
            {
                nameof(MediaAsset.Scale) => changedAsset.Scale,
                nameof(MediaAsset.ScaleX) => changedAsset.ScaleX,
                nameof(MediaAsset.ScaleY) => changedAsset.ScaleY,
                nameof(MediaAsset.PositionX) => changedAsset.PositionX,
                nameof(MediaAsset.PositionY) => changedAsset.PositionY,
                nameof(MediaAsset.Rotation) => changedAsset.Rotation,
                nameof(MediaAsset.IsUniformScale) => changedAsset.IsUniformScale,
                nameof(MediaAsset.Speed) => changedAsset.Speed,
                nameof(MediaAsset.VolumeDb) => changedAsset.VolumeDb,
                _ => null
            };
            if (newValue == null) return;
            foreach (var clipVM in selectedClips)
            {
                if (clipVM.SourceData is MediaAsset asset && asset != changedAsset)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(MediaAsset.IsUniformScale): asset.IsUniformScale = (bool)newValue; break;
                        case nameof(MediaAsset.Scale): asset.Scale = (double)newValue; break;
                        case nameof(MediaAsset.ScaleX): asset.ScaleX = (double)newValue; break;
                        case nameof(MediaAsset.ScaleY): asset.ScaleY = (double)newValue; break;
                        case nameof(MediaAsset.Speed): asset.Speed = (double)newValue; break;
                        case nameof(MediaAsset.VolumeDb): asset.VolumeDb = (double)newValue; break;
                    }
                }
            }
            foreach (var clipVM in selectedClips)
            {
                if (clipVM.SourceData is MediaAsset asset)
                {
                    if (asset.Type == AssetType.Video)
                    {
                        UpdateVideoTransform(asset);
                        _audioEngine.UpdateClipProperties(clipVM);
                    }
                    else if (asset.Type == AssetType.Image)
                    {
                        if (_activeImageOverlays.TryGetValue(asset, out var imageVisual))
                        {
                            ApplyTransformToImageVisual(imageVisual, asset);
                            _imageAdorner?.InvalidateVisual();
                        }
                    }
                    if (e.PropertyName == nameof(MediaAsset.Speed))
                    {
                        clipVM.RefreshPropertiesFromSource();
                    }
                }
            }

            if (e.PropertyName == nameof(MediaAsset.Speed))
            {
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
            }
        }
        private void TimelineScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_totalTimelineDuration.TotalSeconds <= 0 || e.OriginalSource is Thumb)
            {
                return;
            }
            double absoluteClickX = e.GetPosition(TracksContainerGrid).X;
            TimeSpan clickedTime = TimeSpan.FromSeconds(absoluteClickX / this._pixelsPerSecond);
            SeekTimeline(clickedTime);
        }
        #endregion

        #region Waveform & Ruler Drawing (New)
        public string GetFfmpegVolumeArgument()
        {
            if (_mainAudioTrack != null && Math.Abs(_mainAudioTrack.VolumeDb) > 0.05)
            {
                string volumeArg = _mainAudioTrack.VolumeDb.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string ffmpegArg = $"-af \"volume={volumeArg}dB\"";
                return ffmpegArg;
            }
            return string.Empty;
        }
        private void PositionMarker_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }


        private void TimelineContainerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
        private void InitializeSubtitleStyler()
        {
            var controls = new Dictionary<string, FrameworkElement>
    {
        { "fontFamilyComboBox", FontFamilyComboBox },
        { "fontSizeSlider", FontSizeSlider },
        { "fontSizeTextBox", FontSizeTextBox },
        { "fontColorPicker", FontColorPicker },
        { "boldButton", BoldButton },
        { "italicButton", ItalicButton },
        { "underlineButton", UnderlineButton },
        { "characterSpacingSlider", CharacterSpacingSlider },
        { "backgroundSettingsPanel", BackgroundSettingsPanel },
        { "backgroundEnabledCheckBox", BackgroundEnabledCheckBox },
        { "backgroundColorPicker", BackgroundColorPicker },
        { "backgroundOpacitySlider", BackgroundOpacitySlider },
        { "backgroundCornerRadiusSlider", BackgroundCornerRadiusSlider },
        { "backgroundPaddingXSlider", BackgroundPaddingXSlider },
        { "backgroundPaddingYSlider", BackgroundPaddingYSlider },
        { "outlineEnabledCheckBox", OutlineEnabledCheckBox },
        { "shadowEnabledCheckBox", ShadowEnabledCheckBox },
        { "outlineSettingsPanel", OutlineSettingsPanel },
        { "shadowSettingsPanel", ShadowSettingsPanel },
        { "outlineColorPicker", OutlineColorPicker },
        { "outlineThicknessSlider", OutlineThicknessSlider },
        { "shadowColorPicker", ShadowColorPicker },
        { "shadowOpacitySlider", ShadowOpacitySlider },
        { "shadowBlurSlider", ShadowBlurSlider },
        { "shadowDepthSlider", ShadowDepthSlider },
        { "shadowDirectionSlider", ShadowDirectionSlider }
    };

        }
        private void DrawRuler(double pixelsPerSecond)
        {
            if (TimelineRulerHost == null || !TimelineRulerHost.IsVisible)
            {
                return;
            }

            _timelineRulerVisual.Redraw(
                pixelsPerSecond,
                TimelineScrollViewer.HorizontalOffset,
                TimelineScrollViewer.ViewportWidth,
                _totalTimelineDuration,
                _actualContentDuration);
        }

        #endregion
        private sealed class OverlayGeometry
        {
            public int W { get; set; }
            public int H { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_actualContentDuration.TotalSeconds <= 0)
            {
                MessageBox.Show("Timeline trống hoặc không hợp lệ, không thể xuất video.", "Lỗi Timeline", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TimelineClips.Any(c => c.ClipType == TimelineClipType.Video || c.ClipType == TimelineClipType.Image))
            {
                MessageBox.Show("Timeline phải chứa ít nhất một clip Video hoặc Ảnh để có thể xuất.", "Thiếu nội dung hình ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var firstVideo = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
            int defaultWidth = 1280;
            int defaultHeight = 720;

            if (firstVideo != null && firstVideo.SourceData is MediaAsset sourceAsset)
            {
                defaultWidth = sourceAsset.Width;
                defaultHeight = sourceAsset.Height;
            }

            var config = IniConfig.Load(_settingsFilePath);
            string lastGpu = config.GetValue("settings_export", "Last_Gpu_Accel", "auto");
            bool lastThreadLimitEnabled = config.GetBooleanValue("settings_export", "Last_Thread_Limit_Enabled", false);
            int lastThreadLimit = int.TryParse(config.GetValue("settings_export", "Last_Thread_Limit", "0"), out int threads) ? threads : 0;

            var exportWindow = new GenerateVideoWindow(
                _currentProject.ProjectName,
                _lastExportPath,
                defaultWidth,
                defaultHeight,
                _actualContentDuration,
                lastGpu,
                lastThreadLimitEnabled,
                lastThreadLimit
            )
            { Owner = this };

            exportWindow.OnGenerateClicked += async (settings) =>
            {
                _lastExportPath = settings.OutputPath;

                if (!config.ContainsKey("settings_export"))
                {
                    config["settings_export"] = new Dictionary<string, string>();
                }
                config["settings_export"]["Last_Gpu_Accel"] = settings.GpuAcceleration;
                config["settings_export"]["Last_Thread_Limit_Enabled"] = settings.IsThreadLimitEnabled.ToString();
                config["settings_export"]["Last_Thread_Limit"] = settings.ThreadLimit.ToString();
                IniConfig.Save(_settingsFilePath, config);

                SaveConfiguration();
                await ExecuteVideoExportAsync(settings, exportWindow);
            };

            exportWindow.Show();
        }
        private sealed class SmartCutMapItem
        {
            public SrtSubtitleLine Line { get; init; }
            public TimeSpan FinalStart { get; init; }
            public TimeSpan FinalEnd { get; init; }
        }

        private async Task<List<SmartCutMapItem>> BuildSmartCutTimelineForDynamicVideo()
        {
            var mapItems = await BuildSmartCutMapItemsAsync();
            return mapItems.OrderBy(i => i.FinalStart).ToList();
        }
        private static bool FileNameLooksLikeTts(string fileNameWithoutExt)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExt)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(
                    fileNameWithoutExt,
                    @"^(?<idx>\d{1,5})(?:\D|_|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(
                    fileNameWithoutExt,
                    @"_(\d{6,})_(\d{6,})(?:\D|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(
                    fileNameWithoutExt,
                    @"\b(tts|voice[_\-]?sub|narration)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool PathLooksLikeProjectTts(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return false;
            var marker = System.IO.Path.DirectorySeparatorChar + "Projects" + System.IO.Path.DirectorySeparatorChar + "TTS" + System.IO.Path.DirectorySeparatorChar;
            return absolutePath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyTtsClip(TimelineAudioClip clip, IReadOnlyList<SrtSubtitleLine> allSrtLines)
        {
            if (clip == null || string.IsNullOrWhiteSpace(clip.FilePath) || !System.IO.File.Exists(clip.FilePath))
                return false;
            if (clip.IsTts) return true;
            if (clip.SourceSubtitleIndexSnapshot.HasValue) return true;
            if (!string.IsNullOrWhiteSpace(clip.CaptionTextSnapshot)) return true;
            var name = System.IO.Path.GetFileNameWithoutExtension(clip.FilePath);
            if (FileNameLooksLikeTts(name)) return true;
            if (PathLooksLikeProjectTts(clip.FilePath)) return true;
            if (allSrtLines != null && allSrtLines.Count > 0)
            {
                var isExactMatch = allSrtLines.Any(l =>
                    !string.IsNullOrWhiteSpace(l.VoicedAudioPath) &&
                    string.Equals(l.VoicedAudioPath, clip.FilePath, System.StringComparison.OrdinalIgnoreCase));
                if (isExactMatch) return true;
            }
            return false;
        }

        private (List<TimelineAudioClip> ttsClips, List<TimelineAudioClip> bgFxClips) ClassifyAudioClipsForExport()
        {
            var allSrt = (_currentProject?.Subtitles ?? new List<SrtSubtitleLine>())
                         .Concat(_currentProject?.TextClips ?? new List<SrtSubtitleLine>())
                         .ToList();
            var timelineAudios = _currentProject?.VoicedSubtitles ?? new List<TimelineAudioClip>();
            var all = timelineAudios
                        .GroupBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .Where(c => !string.IsNullOrWhiteSpace(c.FilePath) && System.IO.File.Exists(c.FilePath))
                        .ToList();

            var tts = new List<TimelineAudioClip>();
            var bg = new List<TimelineAudioClip>();
            foreach (var clip in all)
            {
                if (IsLikelyTtsClip(clip, allSrt))
                {
                    tts.Add(clip);
                }
                else
                {
                    bg.Add(clip);
                }
            }
            return (tts, bg);
        }

        private Dictionary<TimelineAudioClip, (TimeSpan Start, TimeSpan End)> ResolveTtsPlacementsForDynamic(
            List<TimelineAudioClip> ttsClips,
            List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)> smartCutTimeline)
        {
            var map = new Dictionary<TimelineAudioClip, (TimeSpan, TimeSpan)>();

            static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "" :
                System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

            foreach (var clip in ttsClips)
            {
                if (clip.SourceSubtitleIndexSnapshot.HasValue)
                {
                    var hit = smartCutTimeline.FirstOrDefault(x => x.Line.Index == clip.SourceSubtitleIndexSnapshot.Value);
                    if (hit.Line != null)
                    {
                        map[clip] = (hit.FinalStart, hit.FinalEnd);
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(clip.CaptionTextSnapshot))
                {
                    var normalizedSnapshot = Norm(clip.CaptionTextSnapshot);
                    var hit = smartCutTimeline.FirstOrDefault(x =>
                        string.Equals(Norm(x.Line.OriginalText), normalizedSnapshot, StringComparison.OrdinalIgnoreCase) ||
                        Norm(x.Line.OriginalText).Contains(normalizedSnapshot));
                    if (hit.Line != null)
                    {
                        map[clip] = (hit.FinalStart, hit.FinalEnd);
                        continue;
                    }
                }
                var dur = clip.EffectiveDuration;
                map[clip] = (clip.StartTime, clip.StartTime + dur);
            }
            return map;
        }
        private async Task RunSmartCutDynamicPass2Async(string ffmpegPath, MediaAsset mainVideoAsset, string pass1WavPath, string tempAssPath, VideoExportSettings settings, GenerateVideoWindow progressWindow, CancellationToken cancellationToken)
        {
            if (mainVideoAsset == null || string.IsNullOrWhiteSpace(mainVideoAsset.FilePath) || !File.Exists(mainVideoAsset.FilePath))
                throw new FileNotFoundException("Không tìm thấy video chính cho pass 2.");
            if (string.IsNullOrWhiteSpace(pass1WavPath) || !File.Exists(pass1WavPath))
                throw new FileNotFoundException("Không tìm thấy WAV pass 1.");
            if (string.IsNullOrWhiteSpace(tempAssPath) || !File.Exists(tempAssPath))
                throw new FileNotFoundException("Không tìm thấy file ASS tạm.");
            var args = new StringBuilder();
            var inputMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int inputIndex = 0;
            args.Append("-y -hide_banner ");
            args.Append($"-i \"{mainVideoAsset.FilePath}\" ");
            inputMap[mainVideoAsset.FilePath] = inputIndex++;
            args.Append($"-i \"{pass1WavPath}\" ");
            inputMap[pass1WavPath] = inputIndex++;
            int wavInputIndex = inputMap[pass1WavPath];
            var imageClips = TimelineClips.Where(c => c.ClipType == TimelineClipType.Image && c.SourceData is MediaAsset).ToList();
            var distinctImages = imageClips
                .Select(c => ((MediaAsset)c.SourceData).FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var imgPath in distinctImages)
            {
                args.Append($"-stream_loop -1 -i \"{imgPath}\" ");
                inputMap[imgPath] = inputIndex++;
            }
            var (_, bgFxClips) = ClassifyAudioClipsForExport();
            var distinctBgFx = bgFxClips
                .Select(b => b.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var bg in distinctBgFx)
            {
                args.Append($"-i \"{bg}\" ");
                inputMap[bg] = inputIndex++;
            }
            var filterChains = new List<string>();
            string panZoomFilter;
            double zoom = mainVideoAsset.Scale;
            double posX = mainVideoAsset.PositionX;
            double posY = mainVideoAsset.PositionY;
            int W = settings.ResolutionWidth;
            int H = settings.ResolutionHeight;

            if (Math.Abs(zoom - 1.0) > 0.001 || Math.Abs(posX - 0.5) > 0.001 || Math.Abs(posY - 0.5) > 0.001)
            {
                string cover = $"max({W}/iw\\,{H}/ih)";
                string zoomExpr = $"max({zoom.ToString(CultureInfo.InvariantCulture)}\\,1)";
                string eff = $"({cover})*({zoomExpr})";
                string scaleFilter = $"scale=w=ceil(iw*{eff}):h=ceil(ih*{eff})";
                string cx = $"(iw - {W})/2 - ({posX.ToString(CultureInfo.InvariantCulture)} - 0.5)*{W}";
                string cy = $"(ih - {H})/2 - ({posY.ToString(CultureInfo.InvariantCulture)} - 0.5)*{H}";
                string cropX = $"x='max(0\\,min(iw-{W}\\,({cx})))'";
                string cropY = $"y='max(0\\,min(ih-{H}\\,({cy})))'";
                string cropFilter = $"crop={W}:{H}:{cropX}:{cropY}";
                panZoomFilter = $"{scaleFilter},{cropFilter}";
            }
            else
            {
                string cover = $"max({W}/iw\\,{H}/ih)";
                string scaleFilter = $"scale=w=ceil(iw*({cover})):h=ceil(ih*({cover}))";
                panZoomFilter = scaleFilter;
            }
            string currentVideoTag = "v_base";
            double sourceTotalSec = await ProbeDurationSecondsAsync(mainVideoAsset.FilePath);
            double baseTrimStartSec = mainVideoAsset?.TrimStartOffset.TotalSeconds ?? 0.0;
            double baseTrimEndSec = mainVideoAsset?.TrimEndOffset.TotalSeconds ?? 0.0;
            double effectiveEndSec = Math.Max(baseTrimStartSec, sourceTotalSec - baseTrimEndSec);
            filterChains.Add(
                $"[0:v]" +
                $"trim=start={baseTrimStartSec.ToString(CultureInfo.InvariantCulture)}:end={effectiveEndSec.ToString(CultureInfo.InvariantCulture)}," +
                $"setpts=PTS-STARTPTS," +
                $"{panZoomFilter}" +
                $"[v_base]");
            currentVideoTag = "v_base";

            double refW = CurrentProject?.ProjectReferenceVideoWidth > 0 ? CurrentProject.ProjectReferenceVideoWidth : 1280.0;
            double refH = CurrentProject?.ProjectReferenceVideoHeight > 0 ? CurrentProject.ProjectReferenceVideoHeight : 720.0;
            double scaleXFactor = settings.ResolutionWidth / refW;
            double scaleYFactor = settings.ResolutionHeight / refH;
            var overlayClips = imageClips.Where(c => c.TrackIndex != 0).OrderBy(c => c.StartTime).ToList();

            for (int i = 0; i < overlayClips.Count; i++)
            {
                var clip = overlayClips[i];
                var img = (MediaAsset)clip.SourceData;

                if (!inputMap.TryGetValue(img.FilePath, out int imgInputIndex))
                    continue;
                baseTrimEndSec = mainVideoAsset?.TrimStartOffset.TotalSeconds ?? 0.0;
                double tStart = Math.Max(0, clip.StartTime.TotalSeconds - baseTrimStartSec);
                double tEnd = Math.Max(tStart, clip.EndTime.TotalSeconds - baseTrimStartSec);

                if (tEnd <= tStart + 1e-6) continue;
                double imgAR = img.Height > 0 ? (double)img.Width / img.Height : 1.0;
                double initW_ref, initH_ref;
                if ((refW / refH) > imgAR) { initH_ref = refH; initW_ref = initH_ref * imgAR; }
                else { initW_ref = refW; initH_ref = initW_ref / imgAR; }
                double finalW_out = initW_ref * Math.Max(img.ScaleX, 0.0001) * scaleXFactor;
                double finalH_out = initH_ref * Math.Max(img.ScaleY, 0.0001) * scaleYFactor;
                int outW = Math.Max(1, (int)Math.Round(finalW_out));
                int outH = Math.Max(1, (int)Math.Round(finalH_out));
                double centerX_out = img.PositionX * refW * scaleXFactor;
                double centerY_out = img.PositionY * refH * scaleYFactor;
                double radians = (img.Rotation) * Math.PI / 180.0;
                string processedTag = $"processed_img_{i}";
                string nextVideoTag = $"v_with_img_{i}";
                if (Math.Abs(radians) < 1e-6)
                {
                    filterChains.Add(
                        $"[{imgInputIndex}:v]" +
                        $"scale={outW}:{outH}:flags=lanczos," +
                        $"format=rgba," +
                        $"fifo" +
                        $"[{processedTag}]"
                    );
                }
                else
                {
                    filterChains.Add(
                        $"[{imgInputIndex}:v]" +
                        $"scale={outW}:{outH}:flags=lanczos," +
                        $"format=rgba," +
                        $"rotate={radians.ToString(CultureInfo.InvariantCulture)}:c=black@0:ow='hypot(iw,ih)':oh='hypot(iw,ih)'," +
                        $"fifo" +
                        $"[{processedTag}]"
                    );
                }

                string ox = $"{centerX_out.ToString(CultureInfo.InvariantCulture)}-overlay_w/2";
                string oy = $"{centerY_out.ToString(CultureInfo.InvariantCulture)}-overlay_h/2";
                filterChains.Add(
                    $"[{currentVideoTag}][{processedTag}]" +
                    $"overlay=x='{ox}':y='{oy}':enable='between(t,{tStart.ToString(CultureInfo.InvariantCulture)},{tEnd.ToString(CultureInfo.InvariantCulture)})':shortest=1:eof_action=pass" +
                    $"[{nextVideoTag}]"
                );

                currentVideoTag = nextVideoTag;
            }
            string escapedAss = tempAssPath.Replace(@"\", @"\\").Replace(":", @"\:");
            string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
            if (!Directory.Exists(fontsDir)) fontsDir = @"C:\Windows\Fonts";
            string escapedFonts = fontsDir.Replace(@"\", @"\\").Replace(":", @"\:");
            if (CurrentProject?.Subtitles != null && CurrentProject.Subtitles.Count > 0)
            {
                _exportCachedBlurIntervals = CurrentProject.Subtitles
                    .OrderBy(s => s.StartTime)
                    .Select(s => (s.StartTime, s.EndTime))
                    .ToList();
            }
            else
            {
                _exportCachedBlurIntervals = null;
            }
            bool burnSubsForDynamic = CurrentProject?.Subtitles != null && CurrentProject.Subtitles.Count > 0;
            string blurChainDyn = BuildBlurBoxFilter(
                currentVideoTag,
                "video_blur_e",
                CurrentProject,
                burnSubsForDynamic,
                null
            );
            filterChains.Add(blurChainDyn);
            currentVideoTag = "video_blur_e";
            filterChains.Add(
                $"[{currentVideoTag}]setsar=1[video_out_scaled];" +
                $"[video_out_scaled]subtitles=filename='{escapedAss}':fontsdir='{escapedFonts}':charenc=UTF-8[video_out]"
            );
            if (bgFxClips.Count == 0)
            {
                filterChains.Add($"[{wavInputIndex}:a]anull,alimiter=limit=0.97[final_audio]");
            }
            else
            {
                filterChains.Add($"[{wavInputIndex}:a]anull[a_pass1]");
                var mixInputs = new List<string> { "[a_pass1]" };

                for (int i = 0; i < bgFxClips.Count; i++)
                {
                    var bg = bgFxClips[i];
                    if (!inputMap.TryGetValue(bg.FilePath, out var bgIndex)) continue;

                    string processed = $"bg_audio_{i}";
                    string delayed = $"bg_delayed_{i}";

                    var clipFilters = new List<string>
            {
                $"volume={DbToGain(bg.VolumeDb).ToString(CultureInfo.InvariantCulture)}"
            };

                    var trimDuration = bg.OriginalDuration - bg.TrimStartOffset - bg.TrimEndOffset;
                    if (trimDuration > TimeSpan.Zero)
                        clipFilters.Add($"atrim=start={bg.TrimStartOffset.TotalSeconds.ToString(CultureInfo.InvariantCulture)}:duration={trimDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");

                    clipFilters.Add("asetpts=PTS-STARTPTS");

                    if (Math.Abs(bg.Speed - 1.0) > 0.01)
                    {
                        string atempo = BuildAtempoChain(bg.Speed);
                        if (!string.IsNullOrEmpty(atempo)) clipFilters.Add(atempo);
                    }

                    filterChains.Add($"[{bgIndex}:a]{string.Join(",", clipFilters)}[{processed}]");

                    long delayMs = (long)bg.StartTime.TotalMilliseconds;
                    filterChains.Add($"[{processed}]adelay={delayMs}|{delayMs}[{delayed}]");

                    mixInputs.Add($"[{delayed}]");
                }

                filterChains.Add($"{string.Join("", mixInputs)}amix=inputs={mixInputs.Count}:duration=first:normalize=0,alimiter=limit=0.97[final_audio]");
            }
            string finalFilter = string.Join(";", filterChains);
            args.Append($"-filter_complex \"{finalFilter}\" ");
            args.Append("-map \"[video_out]\" -map \"[final_audio]\" ");
            // CHÈN SAU KHI ĐÃ map video_out và final_audio, TRƯỚC -threads

            // Xác định encoder phần cứng
            bool isHardwareEncoder = false;
            if (!string.IsNullOrWhiteSpace(settings.VideoCodec))
            {
                string lowerCodec = settings.VideoCodec.ToLowerInvariant();
                if (lowerCodec.Contains("_nvenc") ||
                    lowerCodec.Contains("_qsv") ||
                    lowerCodec.Contains("_amf"))
                {
                    isHardwareEncoder = true;
                }
            }

            if (isHardwareEncoder)
            {
                // GPU encode (ví dụ CUDA/NVENC):
                args.Append($"-c:v {settings.VideoCodec} ");
                args.Append($"-cq {settings.Crf} ");
                if (!string.IsNullOrEmpty(settings.Preset))
                {
                    args.Append($"-preset {settings.Preset} ");
                }
                args.Append("-vsync 0 ");
            }
            else
            {
                // CPU encode fallback:
                args.Append("-c:v libx264 ");
                args.Append($"-crf {settings.Crf} ");
                if (!string.IsNullOrEmpty(settings.Preset))
                {
                    args.Append($"-preset {settings.Preset} ");
                }
            }


            if (settings.IsThreadLimitEnabled && !isHardwareEncoder)
            {
                args.Append($"-threads {settings.ThreadLimit} ");
            }
            if (!string.IsNullOrEmpty(settings.Tune)) args.Append($"-tune {settings.Tune} ");
            if (!string.IsNullOrEmpty(settings.PixelFormat)) args.Append($"-pix_fmt {settings.PixelFormat} ");

            args.Append($"-c:a aac -b:a {settings.AudioBitrate} -ar 44100 ");
            if (settings.ForceStereo) args.Append("-ac 2 ");
            args.Append("-movflags +faststart ");
            args.Append("-shortest ");
            args.Append($"\"{settings.OutputPath}\"");

            System.Diagnostics.Debug.WriteLine($"\n\n--- FFMPEG DYNAMIC PASS 2 COMMAND ---\n\nffmpeg {args}\n\n--- END FFMPEG COMMAND ---\n\n");
            await ExecuteFfmpegWithProgress(ffmpegPath, args.ToString(), progressWindow, settings.OutputPath, cancellationToken);
        }

        private async Task<string> BuildDynamicAudioFirstPassAsync(
    MediaAsset mainVideoAsset,
    double slowFactor,
    string ffmpegPath,
    IProgress<(int step, string msg)> progress)
        {
            if (mainVideoAsset == null ||
                string.IsNullOrWhiteSpace(mainVideoAsset.FilePath) ||
                !System.IO.File.Exists(mainVideoAsset.FilePath))
            {
                throw new System.IO.FileNotFoundException("Không tìm thấy video chính cho pass 1.");
            }
            double mainVideoVolumeDb = (mainVideoAsset as MediaAsset)?.VolumeDb ?? 0.0;
            string bgVol = DbToGain(mainVideoVolumeDb).ToString(CultureInfo.InvariantCulture);

            var (ttsClips, _) = ClassifyAudioClipsForExport();
            if (!ttsClips.Any())
            {
                progress?.Report((10, "xử lý audio nền..."));

                string simpleWavOut = TempFileManager.CreateTempFile(".wav");
                TempFileManager.RegisterForCleanup(simpleWavOut);

                double simpleSlow = Math.Clamp(slowFactor, 0.1, 4.0);
                string simpleSlowChain = BuildAtempoChain(simpleSlow);
                string simpleRestoreChain = BuildAtempoChain(1.0 / simpleSlow);

                var simpleArgs = new StringBuilder();
                simpleArgs.Append($"-y -i \"{mainVideoAsset.FilePath}\" ");
                simpleArgs.Append($"-filter:a \"volume={bgVol},{simpleSlowChain},{simpleRestoreChain},alimiter=limit=0.95\" ");
                simpleArgs.Append($"-ac 2 -ar 48000 \"{simpleWavOut}\"");

                int simpleExitCode = await RunFfmpegAsync(ffmpegPath, simpleArgs.ToString(), CancellationToken.None);
                if (simpleExitCode != 0)
                    throw new Exception($"FFmpeg trả về mã lỗi {simpleExitCode}.");

                progress?.Report((25, "Hoàn tất xử lý audio nền."));
                return simpleWavOut;
            }

            var smartCutMapItems = await BuildSmartCutTimelineForDynamicVideo();
            var smartCutTimelineForDynamic = smartCutMapItems
                .Select(it => (it.Line, it.FinalStart, it.FinalEnd))
                .ToList();
            var ttsPlacement = ResolveTtsPlacementsForDynamic(ttsClips, smartCutTimelineForDynamic);
            double clampedSlow = Math.Clamp(slowFactor, 0.1, 4.0);
            string slowChain = BuildAtempoChain(clampedSlow);
            string restoreChain = BuildAtempoChain(1.0 / clampedSlow);
            var fc = new StringBuilder();
            var mixInputs = new List<string>();
            fc.Append($"[0:a]volume={bgVol}");
            if (!string.IsNullOrEmpty(slowChain))
            {
                fc.Append($",{slowChain}");
            }
            fc.Append(",asetpts=PTS-STARTPTS[bg0];");
            mixInputs.Add("[bg0]");

            int ttsIndex = 0;
            foreach (var tts in ttsClips.OrderBy(c => c.StartTime))
            {
                if (!ttsPlacement.TryGetValue(tts, out var placement)) continue;

                string escapedPath = EscapePathForFilterScript(tts.FilePath);
                string volume = DbToGain(tts.VolumeDb).ToString(CultureInfo.InvariantCulture);

                long originalStartMs = (long)placement.Start.TotalMilliseconds;
                long preMixDelayMs = (long)Math.Round(originalStartMs / clampedSlow);

                string label = $"tts{ttsIndex++}";
                fc.Append($"amovie='{escapedPath}',volume={volume}");
                fc.Append(",aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo");
                fc.Append($",adelay={preMixDelayMs}|{preMixDelayMs}[{label}];");

                mixInputs.Add($"[{label}]");
            }

            fc.Append($"{string.Join("", mixInputs)}");
            fc.Append($"amix=inputs={mixInputs.Count}:duration=longest:normalize=0:dropout_transition=0,");
            fc.Append("alimiter=limit=0.95");
            if (!string.IsNullOrEmpty(restoreChain))
            {
                fc.Append($",{restoreChain}");
            }
            fc.Append("[aout]");

            string filterScriptPath = TempFileManager.CreateTempFile(".txt");
            await System.IO.File.WriteAllTextAsync(filterScriptPath, fc.ToString());
            TempFileManager.RegisterForCleanup(filterScriptPath);

            string wavOut = TempFileManager.CreateTempFile(".wav");
            TempFileManager.RegisterForCleanup(wavOut);

            string ffArgs =
                $"-i \"{mainVideoAsset.FilePath}\" " +
                $"-filter_complex_script \"{filterScriptPath}\" " +
                "-filter_threads 0 " +
                "-map \"[aout]\" -ac 2 -ar 48000 " +
                $"\"{wavOut}\"";

            progress?.Report((25, "Đang tạo data..."));

            int exitCode = await RunFfmpegAsync(ffmpegPath, "-y " + ffArgs, CancellationToken.None);
            if (exitCode != 0)
                throw new Exception($"FFmpeg trả về mã lỗi {exitCode}.");

            return wavOut;
        }

        private async Task<double> ProbeDurationSecondsAsync(string mediaPath)
        {
            try
            {
                var info = await FFProbe.AnalyseAsync(mediaPath);
                return info?.Duration.TotalSeconds ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static async Task<int> RunFfmpegAsync(string ffmpegExe, string args, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<int>();
            var errorLog = new StringBuilder();

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpegExe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            p.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (errorLog)
                    {
                        errorLog.AppendLine(e.Data);
                    }
                }
            };

            p.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                }
            };

            p.Exited += (s, e) =>
            {
                try { p.CancelErrorRead(); } catch { }
                try { p.CancelOutputRead(); } catch { }
                tcs.TrySetResult(p.ExitCode);
            };

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(true);
                    }
                }
                catch (InvalidOperationException) { }
                tcs.TrySetCanceled();
            }))
            {
                try
                {
                    if (!p.Start())
                    {
                        tcs.TrySetException(new InvalidOperationException("Không thể khởi chạy tiến trình FFmpeg."));
                        return await tcs.Task;
                    }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(new Exception($"Lỗi khi khởi chạy FFmpeg: {ex.Message}"));
                    return await tcs.Task;
                }

                var exitCode = await tcs.Task;

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (exitCode != 0)
                {
                    string log;
                    lock (errorLog)
                    {
                        log = errorLog.ToString();
                    }
                    throw new Exception($"FFmpeg thoát với mã lỗi {exitCode}.\n\nFFmpeg Log:\n{log}");
                }
                return exitCode;
            }
        }
        private async Task ExecuteVideoExportAsync(VideoExportSettings settings, GenerateVideoWindow progressWindow)
        {
            if (_selectedVoiceoverMode == VoiceoverExportMode.Ducking && _currentSmartCutMode != SmartCutMode.None)
            {
                CustomMessageBox.Show("Chế độ lồng tiếng Volume Ducking chỉ hoạt động với chế độ xuất video thông thường, không tương thích với Smart Cut. Vui lòng tắt Smart Cut hoặc chọn chế độ lồng tiếng 'Mặc định'.", "Chế độ không tương thích", MessageBoxButton.OK, MessageBoxImage.Warning);
                progressWindow.MarkAsComplete(false);
                progressWindow.Close();
                return;
            }
            string tempAssPath = null;
            string tempFilterScriptPath = null;
            Process ffmpegProcess = null;
            var cancellationTokenSource = new CancellationTokenSource();
            var ffmpegOutputLog = new StringBuilder();
            var logLock = new object();

            progressWindow.CancelButton.Click += (s, e) =>
            {
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    cancellationTokenSource.Cancel();
                }
            };

            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("Không tìm thấy file ffmpeg.exe!");

                progressWindow.UpdateProgress(0, "Đang tạo file phụ đề...");
                string assContent = GenerateAssFileContent(settings.ResolutionWidth, settings.ResolutionHeight);
                tempAssPath = TempFileManager.CreateTempFile(".ass");
                await File.WriteAllTextAsync(tempAssPath, assContent, Encoding.UTF8, cancellationTokenSource.Token);

                var argsBuilder = new StringBuilder();

                if (_currentSmartCutMode == SmartCutMode.StaticReview)
                {
                    // --- SMART CUT: REVIEW TRUYỆN (STATIC) ---
                    progressWindow.UpdateProgress(2, "Đang chuẩn bị Smart Cut.");
                    var (voicedSrtLines, timingSegments) = await ResolveSmartCutSourcesAsync();

                    if (voicedSrtLines == null || voicedSrtLines.Count == 0 || timingSegments == null || timingSegments.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Chế độ Smart Cut không tìm thấy dữ liệu voice từ bất kỳ nguồn nào (phụ đề, clip hoặc manifest.json).");
                    }
                    var mainVideoClip = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
                    if (mainVideoClip == null || mainVideoClip.SourceData is not MediaAsset mainVideoAsset
                        || string.IsNullOrWhiteSpace(mainVideoAsset.FilePath) || !File.Exists(mainVideoAsset.FilePath))
                    {
                        throw new Exception("Không tìm thấy video chính để xuất ở chế độ Static");
                    }
                    argsBuilder.Append("-y ");
                    argsBuilder.Append($"-i \"{mainVideoAsset.FilePath}\" ");
                    var (ttsClips, bgFxClips) = ClassifyAudioClipsForExport();
                    var inputMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { mainVideoAsset.FilePath, 0 } };
                    int inputIndexCounter = 1;
                    foreach (var bgClip in bgFxClips)
                    {
                        if (bgClip == null || string.IsNullOrEmpty(bgClip.FilePath) || !File.Exists(bgClip.FilePath))
                            continue;
                        if (!inputMap.ContainsKey(bgClip.FilePath))
                        {
                            argsBuilder.Append($"-i \"{bgClip.FilePath}\" ");
                            inputMap[bgClip.FilePath] = inputIndexCounter++;
                        }
                    }
                    var imageOverlayClips = TimelineClips
                        .Where(c => c.ClipType == TimelineClipType.Image && c.SourceData is MediaAsset)
                        .ToList();

                    foreach (var imgClip in imageOverlayClips)
                    {
                        var imgAsset = (MediaAsset)imgClip.SourceData;
                        bool isValid = imgAsset != null
                                       && !string.IsNullOrWhiteSpace(imgAsset.FilePath)
                                       && File.Exists(imgAsset.FilePath);
                        if (!isValid)
                        {
                            continue;
                        }

                        if (!inputMap.ContainsKey(imgAsset.FilePath))
                        {
                            argsBuilder.Append($"-stream_loop -1 -i \"{imgAsset.FilePath}\" ");
                            inputMap[imgAsset.FilePath] = inputIndexCounter++;
                        }
                    }
                    var filterComplex = new StringBuilder();
                    var videoSegments = new List<string>();
                    var targetDurations = new List<TimeSpan>();
                    var voiceAudioLabels = new List<string>();
                    var videoAudioLabels = new List<string>();
                    double sourceTotalSec = await ProbeDurationSecondsAsync(mainVideoAsset.FilePath);
                    double baseTrimStartSec = mainVideoAsset.TrimStartOffset.TotalSeconds;
                    double baseTrimEndSec = mainVideoAsset.TrimEndOffset.TotalSeconds;
                    double sourceEffectiveEndSec = Math.Max(0.0, sourceTotalSec - baseTrimEndSec);
                    double prevEndSrc = baseTrimStartSec;
                    int segIndex = 0;
                    const double EPS = 1e-6;
                    double slowedFactor = mainVideoClip.Speed > 0 ? mainVideoClip.Speed : 1.0;
                    slowedFactor = Math.Clamp(slowedFactor, 0.1, 4.0);
                    double mainVideoVolumeDb = (mainVideoClip.SourceData as MediaAsset)?.VolumeDb ?? 0.0;
                    double zoom = Math.Max(mainVideoAsset.Scale, 0.0001);
                    double posX = mainVideoAsset.PositionX;
                    double posY = mainVideoAsset.PositionY;
                    int W = settings.ResolutionWidth;
                    int H = settings.ResolutionHeight;
                    string cover = $"max({W}/iw\\,{H}/ih)";
                    string zoomExpr = $"max({zoom.ToString(CultureInfo.InvariantCulture)}\\,1)";
                    string eff = $"({cover})*({zoomExpr})";
                    string scaleFilter = $"scale=w=ceil(iw*{eff}):h=ceil(ih*{eff})";
                    string cx = $"(iw - {W})/2 - ({posX.ToString(CultureInfo.InvariantCulture)} - 0.5)*{W}";
                    string cy = $"(ih - {H})/2 - ({posY.ToString(CultureInfo.InvariantCulture)} - 0.5)*{H}";
                    string cropX = $"x='max(0\\,min(iw-{W}\\,({cx})))'";
                    string cropY = $"y='max(0\\,min(ih-{H}\\,({cy})))'";
                    string panZoomFilter = $"{scaleFilter},crop={W}:{H}:{cropX}:{cropY}";
                    for (int i = 0; i < timingSegments.Count; i++)
                    {
                        var segment = timingSegments[i];
                        var srtLine = voicedSrtLines[i];

                        // --- SỬA LỖI: Lấy thông tin clip thực tế từ project để có Speed và VolumeDb chính xác ---
                        // Tìm clip audio tương ứng trong project có chứa các chỉnh sửa của người dùng.
                        var actualClip = _currentProject.VoicedSubtitles.FirstOrDefault(c =>
                            c.SourceSubtitleIndexSnapshot == segment.SourceSubtitleIndexSnapshot &&
                            !string.IsNullOrEmpty(c.FilePath) &&
                            c.FilePath.Equals(segment.FilePath, StringComparison.OrdinalIgnoreCase))
                            ?? _currentProject.VoicedSubtitles.FirstOrDefault(c =>
                            !string.IsNullOrEmpty(c.FilePath) &&
                            c.FilePath.Equals(segment.FilePath, StringComparison.OrdinalIgnoreCase));

                        double startSrcNominal = srtLine.StartTime.TotalSeconds * slowedFactor + baseTrimStartSec;
                        double endSrcNominal = srtLine.EndTime.TotalSeconds * slowedFactor + baseTrimStartSec;
                        baseTrimEndSec = mainVideoAsset?.TrimEndOffset.TotalSeconds ?? 0.0;
                        sourceEffectiveEndSec = Math.Max(baseTrimStartSec, sourceTotalSec - baseTrimEndSec);
                        startSrcNominal = Math.Clamp(startSrcNominal, baseTrimStartSec, sourceEffectiveEndSec);
                        endSrcNominal = Math.Clamp(endSrcNominal, baseTrimStartSec, sourceEffectiveEndSec);
                        double startSrc = Math.Max(startSrcNominal, prevEndSrc);
                        double endSrc = Math.Max(endSrcNominal, startSrc + EPS);
                        double lenSrc = Math.Max(0.001, endSrc - startSrc);
                        double gapSec = Math.Max(0.0, startSrc - prevEndSrc);

                        if (gapSec > 0.0005)
                        {
                            filterComplex.Append($"[0:v]trim=start={prevEndSrc.ToString(CultureInfo.InvariantCulture)}:end={startSrc.ToString(CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS,{panZoomFilter}[v{segIndex}];");
                            videoSegments.Add($"[v{segIndex}]");
                            filterComplex.Append($"anullsrc=r=44100:cl=stereo,atrim=duration={gapSec.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[a_sil{segIndex}];");
                            voiceAudioLabels.Add($"[a_sil{segIndex}]");
                            filterComplex.Append($"[0:a]atrim=start={prevEndSrc.ToString(CultureInfo.InvariantCulture)}:end={startSrc.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS,volume={DbToGain(mainVideoVolumeDb).ToString(CultureInfo.InvariantCulture)},aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[va{segIndex}];");
                            videoAudioLabels.Add($"[va{segIndex}]");
                            segIndex++;
                        }

                        var targetDuration = actualClip?.EffectiveDuration ?? segment.EffectiveDuration;
                        if (targetDuration <= TimeSpan.Zero) targetDuration = (srtLine.EndTime - srtLine.StartTime);
                        if (targetDuration <= TimeSpan.Zero) targetDuration = TimeSpan.FromSeconds(lenSrc);
                        targetDurations.Add(targetDuration);

                        double targetSec = Math.Max(0.001, targetDuration.TotalSeconds);
                        double speed = Math.Clamp(lenSrc / targetSec, 0.0625, 100.0);
                        string atempoChain = BuildAtempoChain(speed);

                        filterComplex.Append($"[0:v]trim=start={startSrc.ToString(CultureInfo.InvariantCulture)}:end={endSrc.ToString(CultureInfo.InvariantCulture)},setpts=(PTS-STARTPTS)/{speed.ToString("0.################", CultureInfo.InvariantCulture)},{panZoomFilter}[v{segIndex}];");
                        videoSegments.Add($"[v{segIndex}]");
                        double audioSpeedForAtempo = actualClip?.Speed ?? 1.0;
                        string audioAtempoChain = BuildAtempoChain(audioSpeedForAtempo);
                        double ttsDb = ResolveDbOrDefault(actualClip?.VolumeDb ?? 0.0, +3.0);

                        filterComplex.Append($"amovie='{EscapePathForFilterScript(segment.FilePath)}'");
                        if (!string.IsNullOrEmpty(audioAtempoChain))
                        {
                            filterComplex.Append($",{audioAtempoChain}");
                        }
                        // Sử dụng atrim để đảm bảo độ dài chính xác sau khi co giãn tốc độ
                        filterComplex.Append($",atrim=duration={targetSec.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS,volume={DbToGain(ttsDb)},aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[av{segIndex}];");
                        voiceAudioLabels.Add($"[av{segIndex}]");
                        filterComplex.Append($"[0:a]atrim=start={startSrc.ToString(CultureInfo.InvariantCulture)}:end={endSrc.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS,volume={DbToGain(mainVideoVolumeDb).ToString(CultureInfo.InvariantCulture)}{(string.IsNullOrEmpty(atempoChain) ? "" : $",{atempoChain}")},aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[va{segIndex}];");
                        videoAudioLabels.Add($"[va{segIndex}]");

                        prevEndSrc = endSrc;
                        segIndex++;
                    }

                    if (prevEndSrc < sourceEffectiveEndSec - 0.0005)
                    {
                        double tailDuration = sourceEffectiveEndSec - prevEndSrc;
                        filterComplex.Append(
                            $"[0:v]trim=start={prevEndSrc.ToString(CultureInfo.InvariantCulture)}:" +
                            $"end={sourceEffectiveEndSec.ToString(CultureInfo.InvariantCulture)}," +
                            $"setpts=PTS-STARTPTS,{panZoomFilter}[v{segIndex}];");
                        videoSegments.Add($"[v{segIndex}]");
                        filterComplex.Append(
                            $"anullsrc=r=44100:cl=stereo," +
                            $"atrim=duration={tailDuration.ToString(CultureInfo.InvariantCulture)}," +
                            $"asetpts=PTS-STARTPTS[a_sil{segIndex}];");
                        voiceAudioLabels.Add($"[a_sil{segIndex}]");
                        filterComplex.Append(
                            $"[0:a]atrim=start={prevEndSrc.ToString(CultureInfo.InvariantCulture)}:" +
                            $"end={sourceEffectiveEndSec.ToString(CultureInfo.InvariantCulture)}," +
                            $"asetpts=PTS-STARTPTS," +
                            $"volume={DbToGain(mainVideoVolumeDb).ToString(CultureInfo.InvariantCulture)}," +
                            $"aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[va{segIndex}];");
                        videoAudioLabels.Add($"[va{segIndex}]");
                    }
                    filterComplex.Append($"{string.Join("", videoSegments)}concat=n={videoSegments.Count}:v=1:a=0[v_concat];");
                    filterComplex.Append($"{string.Join("", voiceAudioLabels)}concat=n={voiceAudioLabels.Count}:v=0:a=1[a_voice];");
                    filterComplex.Append($"{string.Join("", videoAudioLabels)}concat=n={videoAudioLabels.Count}:v=0:a=1[a_vid];");
                    string currentVideoTag = "v_concat";
                    int overlayIdx = 0;
                    double refW = CurrentProject?.ProjectReferenceVideoWidth > 0 ? CurrentProject.ProjectReferenceVideoWidth : 1280.0;
                    double refH = CurrentProject?.ProjectReferenceVideoHeight > 0 ? CurrentProject.ProjectReferenceVideoHeight : 720.0;
                    double scaleXFactor = settings.ResolutionWidth / refW;
                    double scaleYFactor = settings.ResolutionHeight / refH;
                    foreach (var imgClip in imageOverlayClips)
                    {
                        var imgAsset = (MediaAsset)imgClip.SourceData;
                        if (imgAsset == null || !inputMap.TryGetValue(imgAsset.FilePath, out int imgInputIndex))
                            continue;
                        TimeSpan mappedStart = MapSourceTimeToSmartCut(imgClip.StartTime);
                        TimeSpan mappedEnd = MapSourceTimeToSmartCut(imgClip.EndTime);
                        if (mappedEnd < mappedStart) mappedEnd = mappedStart;

                        double tStart = Math.Max(0, mappedStart.TotalSeconds);
                        double tEnd = Math.Max(tStart, mappedEnd.TotalSeconds);
                        if (tEnd <= tStart + 1e-6) continue;
                        double imgAR = imgAsset.Height > 0 ? (double)imgAsset.Width / imgAsset.Height : 1.0;
                        double initW_ref, initH_ref;
                        if ((refW / refH) > imgAR) { initH_ref = refH; initW_ref = initH_ref * imgAR; }
                        else { initW_ref = refW; initH_ref = initW_ref / imgAR; }

                        double finalW_out = initW_ref * Math.Max(imgAsset.ScaleX, 0.0001) * scaleXFactor;
                        double finalH_out = initH_ref * Math.Max(imgAsset.ScaleY, 0.0001) * scaleYFactor;
                        int outW = Math.Max(1, (int)Math.Round(finalW_out));
                        int outH = Math.Max(1, (int)Math.Round(finalH_out));
                        double centerX_out = imgAsset.PositionX * refW * scaleXFactor;
                        double centerY_out = imgAsset.PositionY * refH * scaleYFactor;

                        double radians = (imgAsset.Rotation) * Math.PI / 180.0;

                        string processedLabel = $"processed_img_{overlayIdx}";
                        string overLabel = $"v_img_{overlayIdx}";
                        if (Math.Abs(radians) < 1e-6)
                        {
                            filterComplex.Append(
                                $"[{imgInputIndex}:v]" +
                                $"scale={outW}:{outH}:flags=lanczos," +
                                $"format=rgba," +
                                $"fifo" +
                                $"[{processedLabel}];");
                        }
                        else
                        {
                            filterComplex.Append(
                                $"[{imgInputIndex}:v]" +
                                $"scale={outW}:{outH}:flags=lanczos," +
                                $"format=rgba," +
                                $"rotate={radians.ToString(CultureInfo.InvariantCulture)}:c=black@0:ow='hypot(iw,ih)':oh='hypot(iw,ih)'," +
                                $"fifo" +
                                $"[{processedLabel}];");
                        }
                        string ox = $"{centerX_out.ToString(CultureInfo.InvariantCulture)}-overlay_w/2";
                        string oy = $"{centerY_out.ToString(CultureInfo.InvariantCulture)}-overlay_h/2";

                        string enableExpr = $"between(t,{tStart.ToString("0.########", CultureInfo.InvariantCulture)},{tEnd.ToString("0.########", CultureInfo.InvariantCulture)})";

                        filterComplex.Append(
                            $"[{currentVideoTag}][{processedLabel}]" +
                            $"overlay=x='{ox}':y='{oy}':eval=init:shortest=1:format=auto:enable='{enableExpr}'" +
                            $"[{overLabel}];");

                        currentVideoTag = overLabel;
                        overlayIdx++;
                    }
                    _smartCutSubtitleTimeline = BuildSmartCutTimeline(voicedSrtLines, targetDurations, slowedFactor, TimeSpan.FromSeconds(sourceTotalSec));
                    string assSmartContent = GenerateAssFileContentForSmartCut(settings.ResolutionWidth, settings.ResolutionHeight, _smartCutSubtitleTimeline);
                    await File.WriteAllTextAsync(tempAssPath, assSmartContent, Encoding.UTF8, cancellationTokenSource.Token);
                    if (_smartCutSubtitleTimeline != null && _smartCutSubtitleTimeline.Count > 0)
                    {
                        _exportCachedBlurIntervals = _smartCutSubtitleTimeline
                            .Select(it => (it.FinalStart, it.FinalEnd))
                            .ToList();
                    }
                    else
                    {
                        _exportCachedBlurIntervals = null;
                    }
                    TimeSpan finalStaticDuration = TimeSpan.Zero;
                    if (targetDurations != null && targetDurations.Count > 0)
                    {
                        foreach (var td in targetDurations) finalStaticDuration += td;
                    }
                    bool burnSubsForStatic = CurrentProject.Subtitles != null && CurrentProject.Subtitles.Count > 0;
                    string blurChainStatic = BuildBlurBoxFilter(
                        currentVideoTag,
                        "video_blur_e",
                        CurrentProject,
                        burnSubsForStatic,
                        finalStaticDuration
                    );
                    filterComplex.Append(blurChainStatic + ";");
                    currentVideoTag = "video_blur_e";
                    filterComplex.Append($"[{currentVideoTag}]subtitles=filename='{EscapePathForFilterScript(tempAssPath)}':charenc=UTF-8[video_out];");
                    var finalMixInputs = new List<string> { "[a_voice]", "[a_vid]" };
                    foreach (var bg in bgFxClips)
                    {
                        if (bg == null || !inputMap.TryGetValue(bg.FilePath, out int bgInputIndex)) continue;
                        string processedLabel = $"bgp_{bgInputIndex}";
                        string finalBgLabel = $"bg_{bgInputIndex}";
                        var bgFilters = new List<string>
        {
            $"volume={DbToGain(bg.VolumeDb).ToString(CultureInfo.InvariantCulture)}",
            "aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo"
        };
                        filterComplex.Append($"[{bgInputIndex}:a]{string.Join(",", bgFilters)}[{processedLabel}];");

                        TimeSpan mappedBgStart = MapSourceTimeToSmartCut(bg.StartTime);
                        long delayMs = (long)Math.Max(0, mappedBgStart.TotalMilliseconds);
                        filterComplex.Append($"[{processedLabel}]adelay={delayMs}|{delayMs}[{finalBgLabel}];");
                        finalMixInputs.Add($"[{finalBgLabel}]");
                    }
                    filterComplex.Append($"{string.Join("", finalMixInputs)}amix=inputs={finalMixInputs.Count}:duration=first:normalize=0:dropout_transition=0,alimiter=limit=0.95[final_audio]");
                    var filterScriptPath2 = TempFileManager.CreateTempFile(".txt");
                    await WriteTextNoBomAsync(filterScriptPath2, filterComplex.ToString(), cancellationTokenSource.Token);
                    TempFileManager.RegisterForCleanup(filterScriptPath2);
                    argsBuilder.Append($" -filter_complex_script \"{filterScriptPath2}\" -filter_threads 0 ");
                    argsBuilder.Append("-map \"[video_out]\" -map \"[final_audio]\" ");
                }
                else if (_currentSmartCutMode == SmartCutMode.ParallelDynamic)
                {
                    // MODE 2 MỚI - Parallel Processing
                    await ExecuteParallelSmartCutExportAsync(
                        ffmpegPath,
                        settings,
                        progressWindow,
                        cancellationTokenSource.Token);
                    return;
                }
                else if (_currentSmartCutMode == SmartCutMode.DynamicVideo)
                {
                    string assContentForDynamic = GenerateAssFileContent(settings.ResolutionWidth, settings.ResolutionHeight);
                    await File.WriteAllTextAsync(tempAssPath, assContentForDynamic, Encoding.UTF8, cancellationTokenSource.Token);
                    progressWindow.UpdateProgress(1, "Chuẩn bị SmartCut...");
                    double slowFactor = _smartCutDynamicSpeed;
                    slowFactor = Math.Clamp(slowFactor, 0.1, 4.0);
                    var mainVideoClip = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
                    if (mainVideoClip == null || mainVideoClip.SourceData is not MediaAsset mainVideoAsset
                        || string.IsNullOrWhiteSpace(mainVideoAsset.FilePath) || !File.Exists(mainVideoAsset.FilePath))
                    {
                        throw new Exception("Không tìm thấy video chính để xuất ở chế độ Dynamic.");
                    }
                    string pass1AudioPath = await BuildDynamicAudioFirstPassAsync(
                        mainVideoAsset,
                        slowFactor,
                        ffmpegPath,
                        new Progress<(int step, string msg)>(p => progressWindow.UpdateProgress(p.step, p.msg))
                    );
                    TempFileManager.RegisterForCleanup(pass1AudioPath);
                    progressWindow.UpdateProgress(50, "Xuất video...");
                    await RunSmartCutDynamicPass2Async(ffmpegPath, mainVideoAsset, pass1AudioPath, tempAssPath, settings, progressWindow, cancellationTokenSource.Token);

                    progressWindow.UpdateProgress(100, "Hoàn tất.");
                    progressWindow.MarkAsComplete(true);
                    return;
                }

                else
                {
                    // --- LOGIC XUẤT VIDEO THÔNG THƯỜNG ---
                    var inputMap = new Dictionary<string, int>();
                    int inputIndexCounter = 0;
                    string finalGpuAccel = "none";
                    string finalVideoCodec = settings.VideoCodec;
                    if (settings.GpuAcceleration == "auto")
                    {
                        var allGpus = await FfmpegGpuDetector.GetAvailableGpusAsync();
                        var bestGpu = FfmpegGpuDetector.GetBestAvailableGpu(allGpus);
                        if (bestGpu != null && bestGpu.FfmpegValue != "none")
                        {
                            finalGpuAccel = bestGpu.FfmpegValue;
                            finalVideoCodec = bestGpu.RecommendedCodec;
                        }
                    }
                    else if (settings.GpuAcceleration != "none")
                    {
                        finalGpuAccel = settings.GpuAcceleration;
                    }

                    if (finalGpuAccel != "none")
                    {
                        argsBuilder.Append($"-hwaccel {finalGpuAccel} ");
                    }
                    var visualInputClips = TimelineClips
    .Where(c => (c.ClipType == TimelineClipType.Video || c.ClipType == TimelineClipType.Image) && !string.IsNullOrEmpty(c.FilePath))
    .ToList();

                    foreach (var clip in visualInputClips)
                    {
                        if (!inputMap.ContainsKey(clip.FilePath))
                        {
                            inputMap[clip.FilePath] = inputIndexCounter++;
                        }
                    }
                    foreach (var path in inputMap.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key))
                    {
                        argsBuilder.Append($"-i \"{path}\" ");
                    }
                    argsBuilder.Append("-y ");

                    var filterChains = new List<string>();
                    var visualConcatInputs = new List<string>();
                    var audioFromVisualsConcatInputs = new List<string>();
                    var finalAudioMixInputs = new List<string>();
                    var (ttsClipsForNormal, bgFxClipsForNormal) = ClassifyAudioClipsForExport();
                    var mainTrackVisualClips = TimelineClips.Where(c => (c.ClipType == TimelineClipType.Video || c.ClipType == TimelineClipType.Image) && c.TrackIndex == 0).OrderBy(c => c.StartTime).ToList();

                    for (int i = 0; i < mainTrackVisualClips.Count; i++)
                    {
                        var clip = mainTrackVisualClips[i];
                        int mapIndex = inputMap[clip.FilePath];
                        double duration = clip.Duration.TotalSeconds;
                        string v_out = $"[v_out_{i}]";
                        string a_out = $"[a_out_{i}]";
                        var mediaAsset = clip.SourceData as MediaAsset;
                        double trimStart = mediaAsset?.TrimStartOffset.TotalSeconds ?? 0;
                        double clipVolumeDb = mediaAsset?.VolumeDb ?? 0.0;
                        double speed = mediaAsset?.Speed ?? 1.0;

                        string videoProcessingChain;
                        if (clip.ClipType == TimelineClipType.Video)
                        {
                            string ptsFilter = (Math.Abs(speed - 1.0) > 0.01) ? $",setpts=PTS/{speed.ToString(CultureInfo.InvariantCulture)}" : "";
                            videoProcessingChain = $"[{mapIndex}:v]trim=start={trimStart.ToString(CultureInfo.InvariantCulture)}:duration={(duration * speed).ToString(CultureInfo.InvariantCulture)}{ptsFilter},setpts=PTS-STARTPTS";
                        }
                        else
                        {
                            videoProcessingChain = $"[{mapIndex}:v]loop=loop=-1:size=1:start=0,setpts=PTS-STARTPTS,trim=duration={duration.ToString(CultureInfo.InvariantCulture)}";
                        }

                        string panZoomFilter;
                        if (mediaAsset != null)
                        {
                            double zoom = mediaAsset.Scale;
                            double posX = mediaAsset.PositionX;
                            double posY = mediaAsset.PositionY;

                            if (Math.Abs(zoom - 1.0) > 0.001 || Math.Abs(posX - 0.5) > 0.001 || Math.Abs(posY - 0.5) > 0.001)
                            {
                                int W = settings.ResolutionWidth;
                                int H = settings.ResolutionHeight;

                                string cover = $"max({W}/iw\\,{H}/ih)";
                                string zoomExpr = $"max({zoom.ToString(CultureInfo.InvariantCulture)}\\,1)";
                                string eff = $"({cover})*({zoomExpr})";
                                string scaleFilter = $"scale=w=ceil(iw*{eff}):h=ceil(ih*{eff})";

                                string cx = $"(iw - {W})/2 - ({posX.ToString(CultureInfo.InvariantCulture)} - 0.5)*{W}";
                                string cy = $"(ih - {H})/2 - ({posY.ToString(CultureInfo.InvariantCulture)} - 0.5)*{H}";
                                string cropX = $"x='max(0\\,min(iw-{W}\\,({cx})))'";
                                string cropY = $"y='max(0\\,min(ih-{H}\\,({cy})))'";

                                string cropFilter = $"crop={W}:{H}:{cropX}:{cropY}";
                                panZoomFilter = $"{scaleFilter},{cropFilter}";
                            }
                            else
                            {
                                string cover = $"max({settings.ResolutionWidth}/iw\\,{settings.ResolutionHeight}/ih)";
                                string scaleFilter = $"scale=w=ceil(iw*({cover})):h=ceil(ih*({cover}))";
                                panZoomFilter = scaleFilter;
                            }
                        }
                        else
                        {
                            string cover = $"max({settings.ResolutionWidth}/iw\\,{settings.ResolutionHeight}/ih)";
                            string scaleFilter = $"scale=w=ceil(iw*({cover})):h=ceil(ih*({cover}))";
                            panZoomFilter = scaleFilter;
                        }


                        filterChains.Add($"{videoProcessingChain},{panZoomFilter},setsar=1{v_out}");

                        if (clip.ClipType == TimelineClipType.Video)
                        {
                            string atempoFilter = (Math.Abs(speed - 1.0) > 0.01) ? $",atempo={speed.ToString(CultureInfo.InvariantCulture)}" : "";
                            filterChains.Add(
                                $"[{mapIndex}:a]atrim=start={trimStart.ToString(CultureInfo.InvariantCulture)}:duration={(duration * speed).ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS{atempoFilter},volume={clipVolumeDb.ToString(CultureInfo.InvariantCulture)}dB,aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo{a_out}"
                            );
                        }
                        else
                        {
                            filterChains.Add($"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=duration={duration.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS{a_out}");
                        }

                        visualConcatInputs.Add(v_out);
                        audioFromVisualsConcatInputs.Add(a_out);
                    }

                    if (visualConcatInputs.Any())
                    {
                        filterChains.Add($"{string.Join("", visualConcatInputs)}concat=n={visualConcatInputs.Count}:v=1:a=0[v_concat]");
                        filterChains.Add($"{string.Join("", audioFromVisualsConcatInputs)}concat=n={audioFromVisualsConcatInputs.Count}:v=0:a=1[audio_vis_cat]");
                        filterChains.Add("[audio_vis_cat]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[audio_from_visuals]");

                        // === BẮT ĐẦU THAY ĐỔI: Logic Volume Ducking ===
                        if (_selectedVoiceoverMode == VoiceoverExportMode.Ducking)
                        {
                            var (duckingChain, finalDuckedLabel) = BuildAudioDuckingFilterchain("[audio_from_visuals]");
                            if (!string.IsNullOrEmpty(duckingChain))
                            {
                                filterChains.Add(duckingChain);
                                finalAudioMixInputs.Add($"[{finalDuckedLabel}]");
                            }
                            else
                            {
                                finalAudioMixInputs.Add("[audio_from_visuals]");
                            }
                        }
                        else
                        {
                            finalAudioMixInputs.Add("[audio_from_visuals]");
                        }
                        // === KẾT THÚC THAY ĐỔI ===

                    }
                    else
                    {
                        filterChains.Add($"nullsrc=size={settings.ResolutionWidth}x{settings.ResolutionHeight}:duration={_actualContentDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}[v_concat]");
                    }

                    string currentVideoTag = "v_concat";
                    var imageOverlayClips = TimelineClips.Where(c => c.ClipType == TimelineClipType.Image && c.TrackIndex != 0).ToList();
                    if (imageOverlayClips.Any())
                    {
                        for (int i = 0; i < imageOverlayClips.Count; i++)
                        {
                            var imageClip = imageOverlayClips[i];
                            if (imageClip.SourceData is not MediaAsset imageAsset) continue;

                            int imageInputIndex = inputMap[imageAsset.FilePath];
                            string nextVideoTag = $"v_with_img_{i}";
                            string processedTag = $"processed_img_{i}";

                            double refW = CurrentProject.ProjectReferenceVideoWidth > 0 ? CurrentProject.ProjectReferenceVideoWidth : DEFAULT_REFERENCE_WIDTH;
                            double refH = CurrentProject.ProjectReferenceVideoHeight > 0 ? CurrentProject.ProjectReferenceVideoHeight : DEFAULT_REFERENCE_HEIGHT;

                            double scaleXFactor = settings.ResolutionWidth / refW;
                            double scaleYFactor = settings.ResolutionHeight / refH;

                            double imgAR = imageAsset.Height > 0 ? (double)imageAsset.Width / imageAsset.Height : 1.0;

                            double initW_ref, initH_ref;
                            if ((refW / refH) > imgAR)
                            {
                                initH_ref = refH;
                                initW_ref = initH_ref * imgAR;
                            }
                            else
                            {
                                initW_ref = refW;
                                initH_ref = initW_ref / imgAR;
                            }

                            double finalW_out = initW_ref * Math.Max(imageAsset.ScaleX, 0.0001) * scaleXFactor;
                            double finalH_out = initH_ref * Math.Max(imageAsset.ScaleY, 0.0001) * scaleYFactor;
                            int origW = Math.Max(1, (int)Math.Round(finalW_out));
                            int origH = Math.Max(1, (int)Math.Round(finalH_out));

                            double centerX_out = imageAsset.PositionX * refW * scaleXFactor;
                            double centerY_out = imageAsset.PositionY * refH * scaleYFactor;

                            double radians = (imageAsset.Rotation) * Math.PI / 180.0;
                            double startTime = imageClip.StartTime.TotalSeconds;
                            double endTime = imageClip.EndTime.TotalSeconds;

                            string imgChain =
                                $"[{imageInputIndex}:v]" +
                                $"scale={origW}:{origH}:flags=lanczos," +
                                $"format=rgba," +
                                $"rotate='{radians.ToString(CultureInfo.InvariantCulture)}:c=none:ow=hypot(iw\\,ih):oh=hypot(iw\\,ih)'" +
                                $"[{processedTag}]";
                            filterChains.Add(imgChain);

                            string ox = $"{centerX_out.ToString(CultureInfo.InvariantCulture)} - overlay_w/2";
                            string oy = $"{centerY_out.ToString(CultureInfo.InvariantCulture)} - overlay_h/2";

                            string overlayChain =
                                $"[{currentVideoTag}][{processedTag}]" +
                                $"overlay=x='{ox}':y='{oy}':enable='between(t\\,{startTime.ToString(CultureInfo.InvariantCulture)}\\,{endTime.ToString(CultureInfo.InvariantCulture)})':format=auto" +
                                $"[{nextVideoTag}]";
                            filterChains.Add(overlayChain);

                            currentVideoTag = nextVideoTag;
                        }
                    }

                    var audioClipsOnTimeline = TimelineClips.Where(c => c.ClipType == TimelineClipType.Audio).ToList();
                    for (int j = 0; j < audioClipsOnTimeline.Count; j++)
                    {
                        var audioVM = audioClipsOnTimeline[j];
                        var audioClipSource = audioVM.SourceData as TimelineAudioClip;
                        if (audioClipSource == null || string.IsNullOrEmpty(audioClipSource.FilePath) || !File.Exists(audioClipSource.FilePath)) continue;

                        // KHÔNG còn dùng inputMap, thay vào đó gọi trực tiếp file bằng amovie
                        string escapedPath = EscapePathForFilterScript(audioClipSource.FilePath);

                        double finalDb = audioVM.VolumeDb;
                        if (ttsClipsForNormal.Contains(audioClipSource))
                        {
                            finalDb = ResolveDbOrDefault(audioVM.VolumeDb, +20.0);
                        }
                        var filters = new List<string>
    {
        $"atrim=start={audioClipSource.TrimStartOffset.TotalSeconds.ToString(CultureInfo.InvariantCulture)}:duration={audioClipSource.OriginalDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}",
        "asetpts=PTS-STARTPTS"
    };
                        if (Math.Abs(audioClipSource.Speed - 1.0) > 0.01)
                        {
                            string atempoChain = BuildAtempoChain(audioClipSource.Speed);
                            if (!string.IsNullOrEmpty(atempoChain)) filters.Add(atempoChain);
                        }
                        if (Math.Abs(finalDb) > 0.01) filters.Add($"volume={finalDb.ToString(CultureInfo.InvariantCulture)}dB");

                        // Bắt đầu filter bằng 'amovie' thay vì '[index:a]'
                        filterChains.Add(
                            $"amovie='{escapedPath}',{string.Join(",", filters)}," +
                            $"aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo" +
                            $"[a_processed_{j}]"
                        );

                        filterChains.Add($"[a_processed_{j}]adelay={(long)audioVM.StartTime.TotalMilliseconds}|{(long)audioVM.StartTime.TotalMilliseconds}[a_delayed_{j}]");
                        finalAudioMixInputs.Add($"[a_delayed_{j}]");
                    }

                    if (finalAudioMixInputs.Any())
                    {
                        filterChains.Add(
                            $"{string.Join("", finalAudioMixInputs)}" +
                            $"amix=inputs={finalAudioMixInputs.Count}:duration=longest:normalize=0," +
                            $"alimiter=limit=0.97" +
                            $"[final_audio]"
                        );
                    }
                    else
                    {
                        filterChains.Add("anullsrc=channel_layout=stereo:sample_rate=44100[final_audio]");
                    }

                    string escapedAss = tempAssPath.Replace(@"\", @"\\").Replace(":", @"\:");
                    string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
                    if (!Directory.Exists(fontsDir)) fontsDir = @"C:\Windows\Fonts";
                    string escapedFonts = fontsDir.Replace(@"\", @"\\").Replace(":", @"\:");
                    if (CurrentProject?.Subtitles != null && CurrentProject.Subtitles.Count > 0)
                    {
                        _exportCachedBlurIntervals = CurrentProject.Subtitles
                            .OrderBy(s => s.StartTime)
                            .Select(s => (s.StartTime, s.EndTime))
                            .ToList();
                    }
                    else
                    {
                        _exportCachedBlurIntervals = null;
                    }
                    bool burnSubsForNormal = CurrentProject?.Subtitles != null && CurrentProject.Subtitles.Count > 0;
                    string blurChainNormal = BuildBlurBoxFilter(
                        currentVideoTag,
                        "video_blur_e",
                        CurrentProject,
                        burnSubsForNormal,
                        null
                    );
                    filterChains.Add(blurChainNormal);
                    currentVideoTag = "video_blur_e";
                    string subtitleFilterLine = $"[{currentVideoTag}]subtitles=filename='{escapedAss}':fontsdir='{escapedFonts}':charenc=UTF-8[video_out]";
                    filterChains.Add(subtitleFilterLine);

                    string finalFilterComplex = string.Join(";", filterChains);

                    tempFilterScriptPath = TempFileManager.CreateTempFile(".txt");
                    await WriteTextNoBomAsync(
                        tempFilterScriptPath,
                        finalFilterComplex,
                        cancellationTokenSource.Token
                    );
                    argsBuilder.Append($"-filter_complex_script \"{tempFilterScriptPath}\" -filter_threads 0 ");
                    argsBuilder.Append("-map \"[video_out]\" -map \"[final_audio]\" ");
                }

                // CHẶN MỚI - BẮT ĐẦU
                // XÁC ĐỊNH encoder phần cứng (NVENC / QSV / AMF) hay encoder phần mềm (libx264/libx265/libvpx-vp9/prores_ks)
                bool isHardwareEncoder = false;
                if (!string.IsNullOrWhiteSpace(settings.VideoCodec))
                {
                    string lowerCodec = settings.VideoCodec.ToLowerInvariant();
                    // bất kỳ *_nvenc, *_qsv, *_amf xem là phần cứng
                    if (lowerCodec.Contains("_nvenc") ||
                        lowerCodec.Contains("_qsv") ||
                        lowerCodec.Contains("_amf"))
                    {
                        isHardwareEncoder = true;
                    }
                }

                // Nếu là encoder phần cứng (ví dụ CUDA/NVENC) VÀ chúng ta đang ở chế độ xuất bình thường
                // (SmartCutMode.None = không chạy chế độ review/dynamic smartcut đặc biệt):
                if (isHardwareEncoder && _currentSmartCutMode == SmartCutMode.None)
                {
                    // GPU path (CUDA / NVENC / QSV / AMF):
                    // -cq dùng constant quality cho NVENC thay vì -crf. :contentReference[oaicite:19]{index=19}
                    // -preset dùng preset kiểu p1..p7 (hoặc lossless...) từ UI đã cập nhật. :contentReference[oaicite:20]{index=20}
                    // -vsync 0 để tránh stall do đồng bộ khung hình khi encode phần cứng. :contentReference[oaicite:21]{index=21}
                    argsBuilder.Append($"-c:v {settings.VideoCodec} ");
                    argsBuilder.Append($"-cq {settings.Crf} ");
                    if (!string.IsNullOrEmpty(settings.Preset))
                    {
                        argsBuilder.Append($"-preset {settings.Preset} ");
                    }

                    // Nếu người dùng có chọn tune (hq / ll / ull / lossless), thêm nó ở dưới phần -tune chung
                    // (phần -tune sẽ được append sau đoạn này, chúng ta không cần lặp lại ở đây).

                    // Tránh block sync không cần thiết với encoder phần cứng:
                    argsBuilder.Append("-vsync 0 ");
                }
                else
                {
                    // CPU path (libx264 software):
                    // CRF cho chất lượng, preset ultrafast..veryslow.
                    argsBuilder.Append("-c:v libx264 ");
                    argsBuilder.Append($"-crf {settings.Crf} ");
                    if (!string.IsNullOrEmpty(settings.Preset))
                    {
                        argsBuilder.Append($"-preset {settings.Preset} ");
                    }
                }

                // CHỈ ép giới hạn thread khi là encoder PHẦN MỀM.
                // NVENC/QSV/AMF không cần -threads và thêm vào có thể phản tác dụng.
                if (settings.IsThreadLimitEnabled && !isHardwareEncoder)
                {
                    argsBuilder.Append($"-threads {settings.ThreadLimit} ");
                }
                // CHẶN MỚI - KẾT THÚC

                if (!string.IsNullOrEmpty(settings.Tune))
                {
                    argsBuilder.Append($"-tune {settings.Tune} ");
                }
                if (!string.IsNullOrEmpty(settings.PixelFormat))
                {
                    argsBuilder.Append($"-pix_fmt {settings.PixelFormat} ");
                }
                argsBuilder.Append($"-c:a aac -b:a {settings.AudioBitrate} -ar 44100 ");
                if (settings.ForceStereo)
                {
                    argsBuilder.Append("-ac 2 ");
                }
                argsBuilder.Append("-movflags +faststart ");
                argsBuilder.Append("-shortest ");
                argsBuilder.Append($"\"{settings.OutputPath}\"");
                string finalArguments = argsBuilder.ToString();
                System.Diagnostics.Debug.WriteLine($"\n\n--- FFMPEG SMART CUT COMMAND ---\n\nffmpeg {finalArguments}\n\n--- END FFMPEG COMMAND ---\n\n");
                progressWindow.UpdateProgress(5, "Đang mã hóa...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = finalArguments,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                double totalDurationSeconds = _actualContentDuration.TotalSeconds;
                if (_currentSmartCutMode == SmartCutMode.StaticReview || _currentSmartCutMode == SmartCutMode.DynamicVideo)
                {
                    if (_smartCutSubtitleTimeline != null && _smartCutSubtitleTimeline.Count > 0)
                    {
                        totalDurationSeconds = _smartCutSubtitleTimeline.Last().FinalEnd.TotalSeconds;
                    }
                }
                ffmpegProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null || totalDurationSeconds <= 0) return;
                    lock (logLock) { ffmpegOutputLog.AppendLine(e.Data); }

                    var match = timeRegex.Match(e.Data);
                    if (match.Success)
                    {
                        var processedTime = TimeSpan.FromHours(double.Parse(match.Groups[1].Value)) +
                                            TimeSpan.FromMinutes(double.Parse(match.Groups[2].Value)) +
                                            TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value)) +
                                            TimeSpan.FromMilliseconds(double.Parse(match.Groups[4].Value) * 10);
                        double progress = (processedTime.TotalSeconds / totalDurationSeconds) * 100;
                        progressWindow.UpdateProgress((int)progress, $"Đang mã hóa... {processedTime:hh\\:mm\\:ss}");
                    }
                };

                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                await ffmpegProcess.WaitForExitAsync(cancellationTokenSource.Token);

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    if (File.Exists(settings.OutputPath)) { try { File.Delete(settings.OutputPath); } catch { } }
                    progressWindow.MarkAsComplete(false);
                }
                else if (ffmpegProcess.ExitCode == 0)
                {
                    progressWindow.UpdateProgress(100, "Hoàn thành!");
                    progressWindow.MarkAsComplete(true);
                }
                else
                {
                    string errorLog;
                    lock (logLock) { errorLog = ffmpegOutputLog.ToString(); }
                    var lastLines = string.Join("\n", errorLog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).TakeLast(15));
                    throw new Exception($"FFmpeg đã thoát với mã lỗi: {ffmpegProcess.ExitCode}.\n\n--- Log từ FFmpeg ---\n{lastLines}");
                }
            }
            catch (OperationCanceledException)
            {
                progressWindow.UpdateProgress((int)progressWindow.MainProgressBar.Value, "Đã hủy bỏ!");
                progressWindow.MarkAsComplete(false);
            }
            catch (Exception ex)
            {
                progressWindow.UpdateProgress((int)progressWindow.MainProgressBar.Value, "Xuất video thất bại!");
                CustomMessageBox.Show($"Có lỗi xảy ra trong quá trình xuất video.\n\nChi tiết: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                progressWindow.MarkAsComplete(false);
            }
            finally
            {
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    try { ffmpegProcess.Kill(true); }
                    catch { }
                }
                if (tempAssPath != null && File.Exists(tempAssPath))
                {
                    try { File.Delete(tempAssPath); } catch { }
                }
                if (tempFilterScriptPath != null && File.Exists(tempFilterScriptPath))
                {
                    try { File.Delete(tempFilterScriptPath); } catch { }
                }
                cancellationTokenSource.Dispose();
            }
        }

        private (string filterChain, string finalDuckedLabel) BuildAudioDuckingFilterchain(string mainAudioInputLabel)
        {
            var ttsClips = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Audio && c.SourceData is TimelineAudioClip)
                .Select(c => c.SourceData as TimelineAudioClip)
                .Where(tac => tac != null)
                .OrderBy(tac => tac.StartTime)
                .ToList();

            if (!ttsClips.Any())
            {
                // Không có clip TTS, không cần ducking
                return (string.Empty, mainAudioInputLabel.Trim('[', ']'));
            }

            var filterBuilder = new StringBuilder();
            string currentLabel = mainAudioInputLabel.Trim('[', ']');
            int filterIndex = 0;
            const double DUCKED_VOLUME = 0.80;

            foreach (var clip in ttsClips)
            {
                string nextLabel = $"ducked_{filterIndex++}";
                var startTime = clip.StartTime;
                var endTime = clip.EndTime;

                // Bỏ qua các clip có thời lượng không hợp lệ
                if (endTime <= startTime)
                {
                    continue;
                }

                filterBuilder.Append(
                    $"[{currentLabel}]volume={DUCKED_VOLUME.ToString(CultureInfo.InvariantCulture)}:enable='between(t,{startTime.TotalSeconds.ToString(CultureInfo.InvariantCulture)},{endTime.TotalSeconds.ToString(CultureInfo.InvariantCulture)})'[{nextLabel}];"
                );
                currentLabel = nextLabel;
            }

            // Nếu không có filter nào được thêm (ví dụ tất cả clip TTS đều không hợp lệ), trả về như cũ
            if (filterBuilder.Length == 0)
            {
                return (string.Empty, mainAudioInputLabel.Trim('[', ']'));
            }

            // Sửa lỗi: Xóa dấu chấm phẩy thừa ở cuối chuỗi filter
            return (filterBuilder.ToString().TrimEnd(';'), currentLabel);
        }
        private static double ResolveDbOrDefault(double projectDb, double defaultDb)
        {
            if (Math.Abs(projectDb) < 0.01)
            {
                return defaultDb;
            }
            return projectDb;
        }
        private static async Task WriteTextNoBomAsync(string path, string content, CancellationToken ct)
        {
            string normalized = content.Replace("\r\n", "\n");
            var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, utf8NoBom))
            {
                await sw.WriteAsync(normalized.AsMemory(), ct);
                await sw.FlushAsync();
            }
        }
        private static string EscapePathForFilterScript(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path
                .Replace(@"\", @"\\")
                .Replace(":", @"\:")
                .Replace("'", @"\'");
        }
        private async Task ExecuteFfmpegWithProgress(string ffmpegPath, string arguments, GenerateVideoWindow progressWindow, string outputPath, CancellationToken cancellationToken)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
            double totalDurationSeconds = _actualContentDuration.TotalSeconds > 0 ? _actualContentDuration.TotalSeconds : 1;
            var tcs = new TaskCompletionSource<bool>();
            var errorLog = new StringBuilder();

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                errorLog.AppendLine(e.Data);
                var match = timeRegex.Match(e.Data);
                if (match.Success)
                {
                    var processedTime = TimeSpan.FromHours(double.Parse(match.Groups[1].Value)) +
                                        TimeSpan.FromMinutes(double.Parse(match.Groups[2].Value)) +
                                        TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value)) +
                                        TimeSpan.FromMilliseconds(double.Parse(match.Groups[4].Value) * 10);

                    double progressPercentage = (processedTime.TotalSeconds / totalDurationSeconds) * 100;
                    progressWindow.Dispatcher.Invoke(() =>
                    {
                        progressWindow.UpdateProgress((int)Math.Min(100, progressPercentage), $"Đang mã hóa... {processedTime:hh\\:mm\\:ss}");
                    });
                }
            };
            process.Exited += (sender, e) =>
            {
                if (System.IO.File.Exists(outputPath))
                {
                    try
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                    }
                }
                if (process.ExitCode == 0)
                {
                    tcs.TrySetResult(true);
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    tcs.TrySetException(new Exception($"FFmpeg exited with error code {process.ExitCode}.\n\n--- Full FFMpeg Log ---\n{errorLog.ToString()}"));
                }
                process.Dispose();
            };

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch (InvalidOperationException) { }
                tcs.TrySetCanceled();
            }))
            {
                process.Start();
                process.BeginErrorReadLine();
                await tcs.Task;
            }
        }

        private List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)> BuildSmartCutTimeline(List<SrtSubtitleLine> orderedVoicedLines, List<TimeSpan> targetDurations, double slowedFactor, TimeSpan sourceTotalDuration)
        {
            var result = new List<(SrtSubtitleLine, TimeSpan, TimeSpan)>();
            if (orderedVoicedLines == null || targetDurations == null) return result;
            if (orderedVoicedLines.Count != targetDurations.Count) return result;

            double cursorSec = 0.0;
            double prevEndSrc = 0.0;

            for (int i = 0; i < orderedVoicedLines.Count; i++)
            {
                var line = orderedVoicedLines[i];
                var target = targetDurations[i];
                double startSrc = line.StartTime.TotalSeconds * slowedFactor;
                double endSrc = line.EndTime.TotalSeconds * slowedFactor;
                double gapSec = Math.Max(0.0, startSrc - prevEndSrc);
                cursorSec += gapSec;
                var finalStart = TimeSpan.FromSeconds(cursorSec);
                cursorSec += Math.Max(0.0, target.TotalSeconds);
                var finalEnd = TimeSpan.FromSeconds(cursorSec);
                result.Add((line, finalStart, finalEnd));
                prevEndSrc = endSrc;
            }
            return result;
        }
        private string GenerateAssFileContentForSmartCut(int playResX, int playResY, List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)> smartCutTimeline)
        {
            const double REFERENCE_HEIGHT = 720.0;
            double scaleFactor = playResY / REFERENCE_HEIGHT;
            var sb = new StringBuilder();
            sb.AppendLine("[Script Info]");
            sb.AppendLine($"Title: Generated by AIOSubPhim - {DateTime.Now}");
            sb.AppendLine($"PlayResX: {playResX}");
            sb.AppendLine($"PlayResY: {playResY}");
            sb.AppendLine("WrapStyle: 2");
            sb.AppendLine("ScaledBorderAndShadow: yes");
            sb.AppendLine();
            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            string primary = HexToAssStyleColor(_currentProject.TemplateFontColor, _currentProject.TemplateOpacity);
            string secondary = primary;
            string outline = HexToAssStyleColor(_currentProject.TemplateOutlineColor, _currentProject.TemplateOpacity);
            string back = "&H00000000";
            int boldFlag = (_currentProject.TemplateFontWeight >= 600) ? -1 : 0;
            int italicFlag = _currentProject.TemplateIsItalic ? -1 : 0;
            int underlineFlag = _currentProject.TemplateIsUnderlined ? -1 : 0;
            double assDefaultFontSize = _currentProject.TemplateFontSize * scaleFactor / 0.985;

            sb.AppendLine(
                $"Style: Default,{_currentProject.TemplateFontFamily}," +
                $"{assDefaultFontSize.ToString("F2", CultureInfo.InvariantCulture)}," +
                            $"{primary},{secondary},{outline},{back}," +
                $"{boldFlag},{italicFlag},{underlineFlag},0," +
                $"100,100,{_currentProject.TemplateCharacterSpacing},0,1," +
                $"{_currentProject.TemplateOutlineThickness}," +
                $"{_currentProject.TemplateShadowDepth},5,10,10,10,1");

            sb.AppendLine(
        $"Style: ShapeBG,{_currentProject.TemplateFontFamily}," +
        $"{assDefaultFontSize.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"&H00FFFFFF,&H00000000,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,2,10,10,10,1");

            sb.AppendLine();
            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

            var lines = new List<SrtSubtitleLine>();
            if (_currentProject.Subtitles != null) lines.AddRange(_currentProject.Subtitles);
            if (_currentProject.TextClips != null) lines.AddRange(_currentProject.TextClips);
            lines = lines.OrderBy(x => x.StartTime).ToList();
            foreach (var item in smartCutTimeline)
            {
                var line = item.Line;
                string start = item.FinalStart.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);
                string end = item.FinalEnd.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);

                string rawText = _currentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated && !string.IsNullOrWhiteSpace(line.TranslatedText)
            ? line.TranslatedText
            : line.OriginalText;
                StyleState style;
                if (line.IsTextClip)
                {
                    style = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();
                }
                else
                {
                    style = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();
                }
                double effectiveWrapWidthPx = ComputeEffectiveWrapWidthPx(style, scaleFactor, rawText);


                bool isTranslated = (_currentProject?.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated);
                string manualOverride = isTranslated ? line.AssOverrideTranslated : line.AssOverrideOriginal;

                string textForAss;
                if (line.HasManualAssOverride && !string.IsNullOrEmpty(manualOverride))
                {
                    textForAss = manualOverride.Replace("\r\n", @"\N").Replace("\n", @"\N");
                }
                else
                {
                    string tempRaw = rawText ?? string.Empty;
                    if (style.AllowAutoWrap && effectiveWrapWidthPx < double.PositiveInfinity)
                        textForAss = WrapTextForAssSmart(tempRaw, style, effectiveWrapWidthPx, scaleFactor);
                    else
                        textForAss = tempRaw.Replace("\r\n", @"\N").Replace("\n", @"\N");
                }
                var styleForMeasure = style.Clone();
                styleForMeasure.ScaleX = 1.0;
                styleForMeasure.ScaleY = 1.0;

                var (measuredWLogical, measuredHLogical) = MeasureTextBoxPixels_UsingTextBlock(
                    textForAss.Replace("\\N", "\n"),
                    styleForMeasure,
                    scaleFactor,
                    double.PositiveInfinity
                );
                double contentWpx = measuredWLogical * style.ScaleX * scaleFactor;
                double contentHpx = measuredHLogical * style.ScaleY * scaleFactor;
                var (padXpx, padYpx) = ComputeFinalPaddingPx(style, scaleFactor);
                double rectW = contentWpx + (padXpx * 2.0);
                double rectH = contentHpx + (padYpx * 2.0);
                int px = (int)Math.Round(style.X * playResX);
                int py = (int)Math.Round(style.Y * playResY);
                if (style.IsBackgroundEnabled)
                {
                    int left = (int)Math.Floor(px - rectW / 2.0);
                    int top = (int)Math.Floor(py - rectH / 2.0);
                    double rx = style.BackgroundCornerRadius * style.ScaleX * scaleFactor;
                    double ry = style.BackgroundCornerRadius * style.ScaleY * scaleFactor;
                    string path = (style.BackgroundCornerRadius > 0)
                        ? BuildRoundedRectPath(rectW, rectH, rx, ry)
                        : BuildTopLeftRectPath(rectW, rectH);
                    var (assColorNoAlpha, assAlpha) = HexToAssColorAndAlpha(style.BackgroundColorHex, style.Opacity);

                    var bgTags = new StringBuilder();
                    bgTags.Append(@"\an7");
                    bgTags.Append($"\\pos({left},{top})");
                    if (Math.Abs(style.Rotation) > double.Epsilon)
                    {
                        int orgAbsX = left + (int)Math.Round(rectW / 2.0);
                        int orgAbsY = top + (int)Math.Round(rectH / 2.0);
                        bgTags.Append($"\\org({orgAbsX},{orgAbsY})");
                        bgTags.Append($"\\frz{(-style.Rotation).ToString("F2", CultureInfo.InvariantCulture)}");
                    }

                    bgTags.Append($"\\1c{assColorNoAlpha}\\1a{assAlpha}\\bord0\\shad0\\p1");

                    sb.AppendLine($"Dialogue: 0,{start},{end},ShapeBG,,0,0,0,,{{{bgTags}}}{path}{{\\p0}}");
                }
                string textTags = GenerateAssOverrideTags_TextOnly(style, playResX, playResY, rectW, padXpx, scaleFactor);
                sb.AppendLine($"Dialogue: 1,{start},{end},Default,,0,0,0,,{{{textTags}}}{textForAss}");
            }
            return sb.ToString();
        }
        private string GenerateAssFileContent(int playResX, int playResY)
        {
            const double REFERENCE_HEIGHT = 720.0;
            double scaleFactor = playResY / REFERENCE_HEIGHT;

            var sb = new StringBuilder();
            sb.AppendLine("[Script Info]");
            sb.AppendLine($"Title: Generated by AIOSubPhim - {DateTime.Now}");
            sb.AppendLine($"PlayResX: {playResX}");
            sb.AppendLine($"PlayResY: {playResY}");
            sb.AppendLine("WrapStyle: 2");
            sb.AppendLine("ScaledBorderAndShadow: yes");
            sb.AppendLine();
            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            string primary = HexToAssStyleColor(_currentProject.TemplateFontColor, _currentProject.TemplateOpacity);
            string secondary = primary;
            string outline = HexToAssStyleColor(_currentProject.TemplateOutlineColor, _currentProject.TemplateOpacity);
            string back = "&H00000000";
            int boldFlag = (_currentProject.TemplateFontWeight >= 600) ? -1 : 0;
            int italicFlag = _currentProject.TemplateIsItalic ? -1 : 0;
            int underlineFlag = _currentProject.TemplateIsUnderlined ? -1 : 0;
            double assDefaultFontSize = _currentProject.TemplateFontSize * scaleFactor / 0985;


            sb.AppendLine(
                $"Style: Default,{_currentProject.TemplateFontFamily}," +
                $"{assDefaultFontSize.ToString("F2", CultureInfo.InvariantCulture)}," +
                            $"{primary},{secondary},{outline},{back}," +
                $"{boldFlag},{italicFlag},{underlineFlag},0," +
                $"100,100,{_currentProject.TemplateCharacterSpacing},0,1," +
                $"{_currentProject.TemplateOutlineThickness}," +
                $"{_currentProject.TemplateShadowDepth},5,10,10,10,1");

            sb.AppendLine(
        $"Style: ShapeBG,{_currentProject.TemplateFontFamily}," +
        $"{assDefaultFontSize.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"&H00FFFFFF,&H00000000,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,2,10,10,10,1");

            sb.AppendLine();
            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

            var lines = new List<SrtSubtitleLine>();
            if (_currentProject.Subtitles != null) lines.AddRange(_currentProject.Subtitles);
            if (_currentProject.TextClips != null) lines.AddRange(_currentProject.TextClips);
            lines = lines.OrderBy(x => x.StartTime).ToList();
            foreach (var line in lines)
            {
                string start = line.StartTime.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);
                string end = line.EndTime.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);

                string rawText = _currentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated && !string.IsNullOrWhiteSpace(line.TranslatedText)
            ? line.TranslatedText
            : line.OriginalText;
                StyleState style;
                if (line.IsTextClip)
                {
                    style = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();
                }
                else
                {
                    style = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();
                }
                double effectiveWrapWidthPx = ComputeEffectiveWrapWidthPx(style, scaleFactor, rawText);

                bool isTranslated = (_currentProject?.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated);
                string manualOverride = isTranslated ? line.AssOverrideTranslated : line.AssOverrideOriginal;

                string textForAss;
                if (line.HasManualAssOverride && !string.IsNullOrEmpty(manualOverride))
                {
                    textForAss = manualOverride.Replace("\r\n", @"\N").Replace("\n", @"\N");
                }
                else
                {
                    string tempRaw = rawText ?? string.Empty;
                    if (style.AllowAutoWrap && effectiveWrapWidthPx < double.PositiveInfinity)
                        textForAss = WrapTextForAssSmart(tempRaw, style, effectiveWrapWidthPx, scaleFactor);
                    else
                        textForAss = tempRaw.Replace("\r\n", @"\N").Replace("\n", @"\N");
                }
                var styleForMeasure = style.Clone();
                styleForMeasure.ScaleX = 1.0;
                styleForMeasure.ScaleY = 1.0;

                var (measuredWLogical, measuredHLogical) = MeasureTextBoxPixels_UsingTextBlock(
                    textForAss.Replace("\\N", "\n"),
                    styleForMeasure,
                    scaleFactor,
                    double.PositiveInfinity
                );
                double contentWpx = measuredWLogical * style.ScaleX * scaleFactor;
                double contentHpx = measuredHLogical * style.ScaleY * scaleFactor;
                var (padXpx, padYpx) = ComputeFinalPaddingPx(style, scaleFactor);
                double rectW = contentWpx + (padXpx * 2.0);
                double rectH = contentHpx + (padYpx * 2.0);
                int px = (int)Math.Round(style.X * playResX);
                int py = (int)Math.Round(style.Y * playResY);
                if (style.IsBackgroundEnabled)
                {
                    int left = (int)Math.Floor(px - rectW / 2.0);
                    int top = (int)Math.Floor(py - rectH / 2.0);
                    double rx = style.BackgroundCornerRadius * style.ScaleX * scaleFactor;
                    double ry = style.BackgroundCornerRadius * style.ScaleY * scaleFactor;
                    string path = (style.BackgroundCornerRadius > 0)
                        ? BuildRoundedRectPath(rectW, rectH, rx, ry)
                        : BuildTopLeftRectPath(rectW, rectH);

                    var (assColorNoAlpha, assAlpha) = HexToAssColorAndAlpha(style.BackgroundColorHex, style.Opacity);
                    var bgTags = new StringBuilder();
                    bgTags.Append(@"\an7");
                    bgTags.Append($"\\pos({left},{top})");
                    if (Math.Abs(style.Rotation) > double.Epsilon)
                    {
                        int orgAbsX = left + (int)Math.Round(rectW / 2.0);
                        int orgAbsY = top + (int)Math.Round(rectH / 2.0);
                        bgTags.Append($"\\org({orgAbsX},{orgAbsY})");
                        bgTags.Append($"\\frz{(-style.Rotation).ToString("F2", CultureInfo.InvariantCulture)}");
                    }

                    bgTags.Append($"\\1c{assColorNoAlpha}\\1a{assAlpha}\\bord0\\shad0\\p1");

                    sb.AppendLine($"Dialogue: 0,{start},{end},ShapeBG,,0,0,0,,{{{bgTags}}}{path}{{\\p0}}");
                }
                string textTags = GenerateAssOverrideTags_TextOnly(style, playResX, playResY, rectW, padXpx, scaleFactor);
                sb.AppendLine($"Dialogue: 1,{start},{end},Default,,0,0,0,,{{{textTags}}}{textForAss}");
            }
            return sb.ToString();
        }
        private static (double padXpx, double padYpx) ComputeFinalPaddingPx(StyleState style, double scaleFactor)
        {
            double sx = (style?.ScaleX ?? 1.0);
            double sy = (style?.ScaleY ?? 1.0);
            double padXpx = (style?.BackgroundPaddingX ?? 0.0) * sx * scaleFactor;
            double padYpx = (style?.BackgroundPaddingY ?? 0.0) * sy * scaleFactor;
            return (padXpx, padYpx);
        }
        private (double measuredWpx, double measuredHpx) MeasureTextBoxPixels_UsingTextBlock(string text, StyleState style, double scaleFactor, double wrapWidthPx)
        {
            var tb = new TextBlock
            {
                Text = text ?? string.Empty,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            tb.FontFamily = new System.Windows.Media.FontFamily(style.FontFamilyName ?? "Arial");
            tb.FontWeight = FontWeight.FromOpenTypeWeight(style.FontWeightValue);
            tb.FontStyle = style.IsItalic ? FontStyles.Italic : FontStyles.Normal;
            double correctedFontSize = style.FontSize * WPF_FONT_SIZE_CORRECTION_FACTOR;
            tb.FontSize = correctedFontSize;
            tb.TextAlignment = TextAlignment.Center;
            tb.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            System.Windows.Size desired = tb.DesiredSize;
            return (desired.Width, desired.Height);
        }
        private double ComputeEffectiveWrapWidthPx(StyleState style, double pxPerLogicalUnit, string rawTextForMeasure)
        {
            if (style.FixedTextBoxWidth > 1.0)
                return Math.Max(1.0, style.FixedTextBoxWidth);
            if (style.Width.HasValue && style.Width.Value > 1.0)
            {
                double totalPreviewPx = style.Width.Value * pxPerLogicalUnit;
                double padLR = (style.IsBackgroundEnabled ? (2.0 * style.BackgroundPaddingX * pxPerLogicalUnit) : 0.0);
                double contentPreviewPx = Math.Max(1.0, totalPreviewPx - padLR);
                return contentPreviewPx;
            }
            var styleForMeasure = style.Clone();
            styleForMeasure.ScaleX = 1.0;
            styleForMeasure.ScaleY = 1.0;
            var (wLogical, _) = MeasureTextBoxPixels_UsingTextBlock(
                (rawTextForMeasure ?? string.Empty).Replace("\\N", "\n"),
                styleForMeasure,
                pxPerLogicalUnit,
                double.PositiveInfinity
            );
            double measuredPreviewPx = wLogical * style.ScaleX;
            var videoRect = GetReferenceVideoFrameRect();
            double videoWidthPx = Math.Max(1.0, videoRect.Width);
            double padXPreviewPx = (style.IsBackgroundEnabled
                ? (style.BackgroundPaddingX * style.ScaleX * pxPerLogicalUnit)
                : 0.0);
            double videoLimitForTextPx = Math.Max(1.0, videoWidthPx - 2.0 * padXPreviewPx);
            if (measuredPreviewPx <= videoLimitForTextPx)
                return double.PositiveInfinity;
            return videoLimitForTextPx;
        }

        private string WrapTextForAssSmart(string raw, StyleState style, double effectiveWrapWidthPx, double pxPerLogicalUnit)
        {
            if (double.IsPositiveInfinity(effectiveWrapWidthPx))
                return (raw ?? string.Empty).Replace("\r\n", @"\N").Replace("\n", @"\N");

            raw ??= string.Empty;
            var tb = new System.Windows.Controls.TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily(style.FontFamilyName),
                FontSize = style.FontSize * pxPerLogicalUnit * WPF_FONT_SIZE_CORRECTION_FACTOR,

                FontWeight = System.Windows.FontWeight.FromOpenTypeWeight(style.FontWeightValue),
                FontStyle = style.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                TextWrapping = TextWrapping.NoWrap
            };
            double Measure(string text)
            {
                tb.Text = text;
                tb.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                return tb.DesiredSize.Width;
            }
            var userBlocks = raw.Replace("\r\n", "\n").Split('\n');

            var ass = new System.Text.StringBuilder();
            for (int b = 0; b < userBlocks.Length; b++)
            {
                string block = userBlocks[b];
                if (string.IsNullOrEmpty(block))
                {
                    if (ass.Length > 0) ass.Append("\\N");
                    continue;
                }
                var tokens = System.Text.RegularExpressions.Regex.Matches(block, @"\S+|\s+|[^\w\s]")
                                .Select(m => m.Value).ToList();

                string current = string.Empty;

                foreach (var tok in tokens)
                {
                    string candidate = current + tok;
                    double w = Measure(candidate);

                    if (w <= effectiveWrapWidthPx || current.Length == 0)
                    {
                        current = candidate;
                    }
                    else
                    {
                        if (ass.Length > 0) ass.Append("\\N");
                        ass.Append(current.TrimEnd());
                        current = tok.TrimStart();
                    }
                }

                if (!string.IsNullOrEmpty(current))
                {
                    if (ass.Length > 0) ass.Append("\\N");
                    ass.Append(current.TrimEnd());
                }
            }

            return ass.ToString();
        }
        private void ComputeAndStoreManualAssOverrideForResize(SrtSubtitleLine anyLine, StyleState styleOfAnyLine, double committedActualWidthPx)
        {
            if (anyLine == null || styleOfAnyLine == null) return;
            double pxPerLogical = ComputePlayerScaleFactor();
            double padLRpx = styleOfAnyLine.IsBackgroundEnabled ? (2.0 * styleOfAnyLine.BackgroundPaddingX * styleOfAnyLine.ScaleX * pxPerLogical) : 0.0;
            double newFixedTextWidthPx = Math.Max(1.0, committedActualWidthPx - padLRpx);
            double newLogicalWidth = Math.Max(1.0, newFixedTextWidthPx / pxPerLogical);
            _currentProject.TemplateWidth = newLogicalWidth;
            IEnumerable<SrtSubtitleLine> all =
                (_currentProject.Subtitles ?? Enumerable.Empty<SrtSubtitleLine>())
                .Concat(_currentProject.TextClips ?? Enumerable.Empty<SrtSubtitleLine>());

            foreach (var line in all)
            {
                var st = (line.Style ?? _currentProject.GetTemplateAsStyleState()).Clone();

                st.Width = newLogicalWidth;
                st.FixedTextBoxWidth = newFixedTextWidthPx;
                line.Style = st;

                bool isTranslated = (_currentProject?.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated);
                string raw = isTranslated ? (line.TranslatedText ?? "") : (line.OriginalText ?? "");
                double wrapWidthPx = ComputeEffectiveWrapWidthPx(st, pxPerLogical, raw);
                string wrapped = WrapTextForAssSmart(raw, st, wrapWidthPx, pxPerLogical);
                if (isTranslated)
                    line.AssOverrideTranslated = wrapped;
                else
                    line.AssOverrideOriginal = wrapped;

                line.HasManualAssOverride = true;
            }

            SaveProjectCurrent();
        }
        private void CollectAndSendPaddingSample(SrtSubtitleLine line, FrameworkElement visual)
        {
            try
            {
                if (line == null || visual == null) return;
                if (!(visual is Border outer)) return;
                if (!(outer.Child is Border inner)) return;
                if (!(inner.Child is FrameworkElement textElem)) return;
                var style = line.Style ?? new Models.StyleState();

                outer.UpdateLayout();
                inner.UpdateLayout();
                textElem.UpdateLayout();
                double contentW = textElem.ActualWidth;
                double padLR = inner.Padding.Left + inner.Padding.Right;
                double previewNoScaleW = contentW + padLR;
                double previewScaledW = previewNoScaleW * style.ScaleX;
                double renderSizeW = outer.RenderSize.Width;
                var canvas = outer.Parent as Canvas;
                if (canvas == null) return;
                System.Windows.Rect bounds = new System.Windows.Rect(new System.Windows.Point(0, 0), outer.RenderSize);
                GeneralTransform toCanvas = outer.TransformToAncestor(canvas);
                System.Windows.Rect transformed = toCanvas.TransformBounds(bounds);
                double transformedW = transformed.Width;
                string text = (textElem is TextBlock tb) ? tb.Text : (textElem as TextBox)?.Text ?? "";
            }
            catch (Exception ex)
            {
            }
        }
        private string BuildRoundedRectPath(double widthPx, double heightPx, double rx, double ry)
        {
            double w = Math.Max(0, widthPx);
            double h = Math.Max(0, heightPx);
            double rX = Math.Min(Math.Max(0, rx), w / 2.0);
            double rY = Math.Min(Math.Max(0, ry), h / 2.0);

            if (rX <= 0.0001 && rY <= 0.0001)
                return BuildTopLeftRectPath(w, h);
            const double K = 0.5522847498307936;
            double cX = rX * K;
            double cY = rY * K;
            int W = (int)Math.Round(w);
            int H = (int)Math.Round(h);
            int RX = (int)Math.Round(rX);
            int RY = (int)Math.Round(rY);
            int CX = (int)Math.Round(cX);
            int CY = (int)Math.Round(cY);
            var sb = new StringBuilder();
            sb.Append($"m {RX} 0 ");
            sb.Append($"l {W - RX} 0 ");
            sb.Append($"b {W - RX + CX} 0 {W} {RY - CY} {W} {RY} ");
            sb.Append($"l {W} {H - RY} ");
            sb.Append($"b {W} {H - RY + CY} {W - RX + CX} {H} {W - RX} {H} ");
            sb.Append($"l {RX} {H} ");
            sb.Append($"b {RX - CX} {H} 0 {H - RY + CY} 0 {H - RY} ");
            sb.Append($"l 0 {RY} ");
            sb.Append($"b 0 {RY - CY} {RX - CX} 0 {RX} 0");
            return sb.ToString();
        }

        private string BuildTopLeftRectPath(double widthPx, double heightPx)
        {
            int w = (int)Math.Round(widthPx);
            int h = (int)Math.Round(heightPx);
            return $"m 0 0 l {w} 0 {w} {h} 0 {h}";
        }
        private static (string assColorNoAlpha, string assAlpha) HexToAssColorAndAlpha(string hex, double opacityFactor = 1.0)
        {
            try
            {
                var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
                double opHex = c.A / 255.0;
                double opFinal = Math.Max(0, Math.Min(1, opacityFactor)) * opHex;
                byte assAA = (byte)(255 - Math.Round(opFinal * 255.0));
                string color = $"&H{c.B:X2}{c.G:X2}{c.R:X2}&";
                string alpha = $"&H{assAA:X2}&";
                return (color, alpha);
            }
            catch
            {
                return ("&HFFFFFF&", "&H00&");
            }
        }
        private string GenerateAssOverrideTags_TextOnly(StyleState style, int playResX, int playResY, double rectW, double padXpx, double scaleFactor)
        {
            if (style == null) return string.Empty;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            int alignmentTag;
            int px = (int)Math.Round(style.X * playResX);
            int py = (int)Math.Round(style.Y * playResY);

            int textX = px;
            int textY = py;

            switch (style.Alignment)
            {
                case 1:
                    alignmentTag = 4;
                    int backgroundLeftEdge = px - (int)Math.Round(rectW / 2.0);
                    textX = backgroundLeftEdge + (int)Math.Round(padXpx);
                    break;
                case 3:
                    alignmentTag = 6;
                    int backgroundRightEdge = px + (int)Math.Round(rectW / 2.0);
                    textX = backgroundRightEdge - (int)Math.Round(padXpx);
                    break;
                case 4:
                case 2:
                default:
                    alignmentTag = 5;
                    break;
            }
            sb.Append($"\\an{alignmentTag}");
            sb.Append(@"\q2");
            sb.Append($"\\pos({textX},{textY})");
            if (Math.Abs(style.Rotation) > double.Epsilon)
            {
                sb.Append($"\\org({px},{py})");
                sb.Append($"\\frz{(-style.Rotation).ToString("F2", inv)}");

            }
            sb.Append($"\\fscx{(style.ScaleX * 100.0).ToString("F2", inv)}");
            sb.Append($"\\fscy{(style.ScaleY * 100.0).ToString("F2", inv)}");
            sb.Append($"\\fn{style.FontFamilyName ?? "Arial"}");
            double assOverrideFontSize = style.FontSize * scaleFactor / 0.985;
            sb.Append($"\\fs{assOverrideFontSize.ToString("F2", inv)}");
            sb.Append($"\\b{(style.FontWeightValue > 500 ? 1 : 0)}");
            sb.Append($"\\i{(style.IsItalic ? 1 : 0)}");
            sb.Append($"\\u{(style.IsUnderlined ? 1 : 0)}");
            var (ass1c, ass1a) = HexToAssColorAndAlpha(style.FontColorHex, style.Opacity);
            sb.Append($"\\1c{ass1c}\\1a{ass1a}");
            if (style.EdgeStyle == TextEdgeStyle.Outline && !style.IsBackgroundEnabled)
            {
                var (ass3c, ass3a) = HexToAssColorAndAlpha(style.OutlineColorHex, style.Opacity);
                sb.Append($"\\3c{ass3c}\\3a{ass3a}");
                sb.Append($"\\bord{style.OutlineThickness.ToString("F2", inv)}");
                sb.Append("\\shad0");
            }
            else if (style.EdgeStyle == TextEdgeStyle.Shadow && !style.IsBackgroundEnabled)
            {
                var (ass4c, ass4a) = HexToAssColorAndAlpha(style.ShadowColorHex, style.Opacity);
                sb.Append($"\\4c{ass4c}\\4a{ass4a}");
                sb.Append($"\\shad{style.ShadowDepth.ToString("F2", inv)}");
                sb.Append($"\\blur{style.ShadowBlur.ToString("F2", inv)}");
                sb.Append("\\bord0");
            }
            else
            {
                sb.Append("\\bord0\\shad0");
            }

            return sb.ToString();
        }
        private static string HexToAssStyleColor(string hex, double masterOpacity)
        {
            try
            {
                var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
                double opHex = c.A / 255.0;
                double opFinal = Math.Max(0, Math.Min(1, masterOpacity)) * opHex;
                byte assAA = (byte)(255 - Math.Round(opFinal * 255.0));
                return $"&H{assAA:X2}{c.B:X2}{c.G:X2}{c.R:X2}";
            }
            catch
            {
                return "&H00FFFFFF";
            }
        }
        #region Subtitle Clip Drag-Move Logic

        private void SubtitleClip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Border || e.OriginalSource is TextBlock || e.OriginalSource is Grid || e.OriginalSource is Canvas || e.OriginalSource is System.Windows.Controls.Image)
            {
                if (sender is FrameworkElement element && element.DataContext is TimelineClipViewModel vm)
                {
                    _isDraggingClip = true;
                    _draggedClipVM = vm;
                    _dragInitialMouseX = e.GetPosition(TracksContainerGrid).X;
                    _dragInitialStartTime = vm.StartTime;
                    _dragInitialEndTime = vm.EndTime;

                    element.CaptureMouse();
                    e.Handled = true;
                }
            }
        }
        private void SubtitleClip_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingTimelineClip || _draggedTimelineClip == null) return;

            System.Windows.Point currentMousePos = e.GetPosition(TracksContainerGrid);
            double deltaX = currentMousePos.X - _dragClipStartPoint.X;
            double deltaY = currentMousePos.Y - _dragClipStartPoint.Y;
            _draggedTimelineClip.X = _dragClipInitialX + deltaX;
            _draggedTimelineClip.Y = _dragClipInitialY + deltaY;
        }

        private void SubtitleClip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingClip)
            {

                if (sender is FrameworkElement element)
                {
                    element.ReleaseMouseCapture();
                }
                UpdateTimelineScaleAndRender();
                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
                if (_draggedClipVM != null)
                {

                }

                _isDraggingClip = false;
                _draggedClipVM = null;
                e.Handled = true;
            }
        }
        #endregion

        private string BuildBlurBoxFilter(string videoInLabel, string videoOutLabel, ProjectState project, bool burnSubtitles, TimeSpan? forceFullDuration = null, bool emitDebug = false)
        {
            static string Q(string expr)
            {
                if (string.IsNullOrEmpty(expr)) return "''";
                return "'" + expr.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
            }
            if (project == null ||
                project.BlurMode == ProjectState.BlurApplyMode.None ||
                project.BlurRectNormalized == null)
            {
                return $"[{videoInLabel}]null[{videoOutLabel}]";
            }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var rect = project.BlurRectNormalized.Value;
            double nx = Math.Max(0.0, Math.Min(1.0, rect.X));
            double ny = Math.Max(0.0, Math.Min(1.0, rect.Y));
            double nw = Math.Max(0.0, Math.Min(1.0, rect.Width));
            double nh = Math.Max(0.0, Math.Min(1.0, rect.Height));
            string wExprCrop = $"max(1,{nw.ToString(ci)}*iw)";
            string hExprCrop = $"max(1,{nh.ToString(ci)}*ih)";
            string xExprCrop = $"clip({nx.ToString(ci)}*iw,0,iw-({wExprCrop}))";
            string yExprCrop = $"clip({ny.ToString(ci)}*ih,0,ih-({hExprCrop}))";
            string wExprOv = wExprCrop.Replace("iw", "main_w").Replace("ih", "main_h");
            string hExprOv = hExprCrop.Replace("iw", "main_w").Replace("ih", "main_h");
            string xExprOv = $"clip({nx.ToString(ci)}*main_w,0,main_w-({wExprOv}))";
            string yExprOv = $"clip({ny.ToString(ci)}*main_h,0,main_h-({hExprOv}))";
            double sigma = Math.Max(0.1, project.BlurPreviewRadius * 0.75);
            string enableExpr = "1";
            var intervalsSeconds = new List<(double Start, double End)>();

            if (project.BlurMode == ProjectState.BlurApplyMode.AllTime)
            {
                if (forceFullDuration.HasValue && forceFullDuration.Value > TimeSpan.Zero)
                {
                    double T = forceFullDuration.Value.TotalSeconds;
                    enableExpr = $"between(t,0,{T.ToString(ci)})";
                    intervalsSeconds.Add((0.0, T));
                }
                else
                {
                    enableExpr = "1";
                }
            }
            else
            {
                if (_currentSmartCutMode == SmartCutMode.StaticReview &&
                    _smartCutSubtitleTimeline != null && _smartCutSubtitleTimeline.Count > 0)
                {
                    foreach (var item in _smartCutSubtitleTimeline)
                    {
                        double s = Math.Max(0.0, item.FinalStart.TotalSeconds);
                        double e = Math.Max(s + 1e-6, item.FinalEnd.TotalSeconds);
                        intervalsSeconds.Add((s, e));
                    }
                }
                else
                {
                    if (burnSubtitles)
                    {
                        foreach (var line in project.Subtitles.OrderBy(l => l.StartTime))
                        {
                            double s = Math.Max(0.0, line.StartTime.TotalSeconds);
                            double e = Math.Max(s + 1e-6, line.EndTime.TotalSeconds);
                            intervalsSeconds.Add((s, e));
                        }
                    }
                    else
                    {
                        if (_exportCachedBlurIntervals != null && _exportCachedBlurIntervals.Count > 0)
                        {
                            foreach (var (start, end) in _exportCachedBlurIntervals)
                            {
                                double s = Math.Max(0.0, start.TotalSeconds);
                                double e = Math.Max(s + 1e-6, end.TotalSeconds);
                                intervalsSeconds.Add((s, e));
                            }
                        }
                        else
                        {
                            foreach (var line in project.Subtitles.OrderBy(l => l.StartTime))
                            {
                                double s = Math.Max(0.0, line.StartTime.TotalSeconds);
                                double e = Math.Max(s + 1e-6, line.EndTime.TotalSeconds);
                                intervalsSeconds.Add((s, e));
                            }
                        }
                    }
                }
                _exportCachedBlurIntervals = intervalsSeconds
                    .Select(iv => (TimeSpan.FromSeconds(iv.Start), TimeSpan.FromSeconds(iv.End)))
                    .ToList();
                if (intervalsSeconds.Count == 0)
                {
                    enableExpr = "0";
                }
                else
                {
                    var parts = intervalsSeconds.Select(iv =>
                        $"between(t,{iv.Start.ToString(ci)},{iv.End.ToString(ci)})");
                    enableExpr = string.Join("+", parts);
                }
            }
            string inOrig = $"{videoInLabel}_orig";
            string inCrop = $"{videoInLabel}_crop";
            string blurred = $"{videoInLabel}_blurred";
            string afterOverlay = $"{videoOutLabel}_tmpov";
            var sb = new System.Text.StringBuilder();
            sb.Append($"[{videoInLabel}]split[{inOrig}][{inCrop}];");
            sb.Append($"[{inCrop}]crop=w={Q(wExprCrop)}:h={Q(hExprCrop)}:x={Q(xExprCrop)}:y={Q(yExprCrop)},gblur=sigma={sigma.ToString(ci)}[{blurred}];");
            sb.Append($"[{inOrig}][{blurred}]overlay=x={Q(xExprOv)}:y={Q(yExprOv)}:enable='{enableExpr}'[{afterOverlay}]");
            if (emitDebug)
            {
                sb.Append($";[{afterOverlay}]drawbox=x={Q(xExprOv)}:y={Q(yExprOv)}:w={Q(wExprOv)}:h={Q(hExprOv)}:t=4:color=white@0.6[{videoOutLabel}]");
                try
                {
                    int exportW = (int)(project.ProjectReferenceVideoWidth > 0 ? project.ProjectReferenceVideoWidth : 1920);
                    int exportH = (int)(project.ProjectReferenceVideoHeight > 0 ? project.ProjectReferenceVideoHeight : 1080);
                    double wPx = Math.Max(1, nw * exportW);
                    double hPx = Math.Max(1, nh * exportH);
                    double xPx = Math.Max(0, Math.Min(nx * exportW, exportW - wPx));
                    double yPx = Math.Max(0, Math.Min(ny * exportH, exportH - hPx));
                    var log = new System.Text.StringBuilder();
                    log.AppendLine("=== BLUR DEBUG (Top-Left based) ===");
                    log.AppendLine($"Preview Normalized: X={nx.ToString(ci)}, Y={ny.ToString(ci)}, W={nw.ToString(ci)}, H={nh.ToString(ci)}");
                    log.AppendLine($"Export Size (est.): {exportW}x{exportH}");
                    log.AppendLine($"Pixel (approx.): X={xPx:F2}, Y={yPx:F2}, W={wPx:F2}, H={hPx:F2}");
                    log.AppendLine($"CROP  w={wExprCrop}  h={hExprCrop}  x={xExprCrop}  y={yExprCrop}");
                    log.AppendLine($"OVER  w={wExprOv}    h={hExprOv}    x={xExprOv}    y={yExprOv}");
                    log.AppendLine($"enable={enableExpr}");
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIOSubPhim_blur_debug.txt");
                    System.IO.File.WriteAllText(path, log.ToString());
                }
                catch { /* tránh crash  */ }
            }
            else
            {
                sb.Append($";[{afterOverlay}]null[{videoOutLabel}]");
            }

            return sb.ToString();
        }


        private void DownloadTtsButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentTtsAudioPath) || !File.Exists(_currentTtsAudioPath))
            {
                CustomMessageBox.Show("Không có file âm thanh nào để tải về.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Title = "Lưu file âm thanh",
                Filter = "MP3 Audio (*.mp3)|*.mp3|WAV Audio (*.wav)|*.wav|All Files (*.*)|*.*",
                FileName = $"TTS_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(_currentTtsAudioPath)}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_currentTtsAudioPath, saveFileDialog.FileName, true);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Lỗi khi lưu file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private BitmapImage LoadBitmapImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        public static async Task<BitmapImage> GenerateVideoThumbnailAsync(string videoPath)
        {
            string thumbnailPath = TempFileManager.CreateTempFile(".jpg");
            try
            {
                var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
                var duration = mediaInfo.Duration;
                var captureTime = duration.TotalSeconds > 10 ? TimeSpan.FromSeconds(duration.TotalSeconds * 0.1) : TimeSpan.FromSeconds(1);
                var snapshotSuccess = await FFMpeg.SnapshotAsync(videoPath, thumbnailPath, new System.Drawing.Size(120, -1), captureTime);
                if (!snapshotSuccess || !File.Exists(thumbnailPath))
                {
                    return null;
                }
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(thumbnailPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {

                return null;
            }
            finally
            {
                if (File.Exists(thumbnailPath))
                {
                    try
                    {
                        File.Delete(thumbnailPath);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }
        private async Task GenerateAndDisplayFilmstripAsync(TimelineClipViewModel clipVM)
        {
            if (clipVM == null || clipVM.ClipType != TimelineClipType.Video || string.IsNullOrEmpty(clipVM.FilePath))
            {
                return;
            }
            if (clipVM.FilmstripThumbnails.Any())
            {
                return;
            }

            if (!_filmstripServiceInstances.TryGetValue(clipVM.FilePath, out var service))
            {
                return;
            }

            if (_filmstripTasks.TryRemove(clipVM, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var newCts = new CancellationTokenSource();
            if (!_filmstripTasks.TryAdd(clipVM, newCts))
            {
                newCts.Dispose();
                return;
            }

            try
            {
                const int initialThumbCount = 20;
                int targetHeight = 60;

                var mediaAsset = clipVM.SourceData as MediaAsset;
                TimeSpan trimStart = mediaAsset?.TrimStartOffset ?? TimeSpan.Zero;
                TimeSpan originalDuration = mediaAsset?.Duration ?? clipVM.Duration;
                TimeSpan trimEnd = mediaAsset?.TrimEndOffset ?? TimeSpan.Zero;
                TimeSpan effectiveDuration = originalDuration - trimStart - trimEnd;

                if (effectiveDuration <= TimeSpan.Zero)
                {
                    await Dispatcher.InvokeAsync(() => clipVM.FilmstripThumbnails.Clear());
                    return;
                }

                var images = await service.GetFilmstripAsync(
                    start: trimStart,
                    duration: effectiveDuration,
                    count: initialThumbCount,
                    targetHeight: targetHeight,
                    ct: newCts.Token
                );

                if (newCts.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (newCts.IsCancellationRequested) return;

                    clipVM.FilmstripThumbnails.Clear();
                    foreach (var img in images)
                    {
                        if (img != null)
                        {
                            clipVM.FilmstripThumbnails.Add(img);
                        }
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { }
            finally
            {
                if (_filmstripTasks.TryRemove(clipVM, out var completedCts))
                {
                    if (completedCts == newCts)
                    {
                        completedCts.Dispose();
                    }
                    else
                    {
                        _filmstripTasks.TryAdd(clipVM, completedCts);
                    }
                }
            }
        }
        private async void ImportMediaButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Chọn các tệp media",
                Filter = "Tất cả media|*.mp4;*.mkv;*.avi;*.mov;*.mp3;*.wav;*.aac;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|Video|*.mp4;*.mkv;*.avi;*.mov|Âm thanh|*.mp3;*.wav;*.aac|Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var newVideoImageAssets = new List<MediaAsset>();
                var newAudioAssets = new List<MediaAsset>();

                foreach (var filePath in openFileDialog.FileNames)
                {
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();
                    var asset = new MediaAsset { FilePath = filePath };

                    try
                    {
                        if (new[] { ".mp4", ".mkv", ".avi", ".mov" }.Contains(extension))
                        {
                            asset.Type = AssetType.Video;

                            if (!_filmstripServiceInstances.ContainsKey(filePath))
                            {
                                try
                                {
                                    _filmstripServiceInstances[filePath] = FilmstripService.Open(filePath);
                                }
                                catch (Exception ex)
                                {
                                    continue;
                                }
                            }

                            if (_filmstripServiceInstances.TryGetValue(filePath, out var filmstripService))
                            {
                                var mediaInfoForThumb = await FFProbe.AnalyseAsync(filePath);
                                var durationForThumb = mediaInfoForThumb.Duration;
                                var captureTime = durationForThumb.TotalSeconds > 10 ? TimeSpan.FromSeconds(durationForThumb.TotalSeconds * 0.1) : TimeSpan.FromSeconds(1);

                                BitmapImage thumbnailBitmap = await filmstripService.GetThumbnailAtAsync(captureTime, 120, CancellationToken.None);
                                if (thumbnailBitmap != null)
                                {
                                    asset.ThumbnailSource = thumbnailBitmap;
                                    asset.ThumbnailBase64 = Services.MediaProcessingService.ConvertBitmapImageToBase64(thumbnailBitmap);
                                }
                            }

                            var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                            asset.Duration = mediaInfo.Duration;
                            var videoStream = mediaInfo.PrimaryVideoStream;
                            if (videoStream != null)
                            {
                                asset.Width = videoStream.Width;
                                asset.Height = videoStream.Height;
                            }

                            newVideoImageAssets.Add(asset);
                        }
                        else if (new[] { ".mp3", ".wav", ".aac" }.Contains(extension))
                        {
                            asset.Type = AssetType.Audio;
                            var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                            asset.Duration = mediaInfo.Duration;
                            newAudioAssets.Add(asset);
                            QueueWaveformGeneration(asset);
                        }
                        else if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" }.Contains(extension))
                        {
                            asset.Type = AssetType.Image;
                            asset.ThumbnailSource = LoadBitmapImage(filePath);
                            if (asset.ThumbnailSource != null)
                            {
                                asset.Width = asset.ThumbnailSource.PixelWidth;
                                asset.Height = asset.ThumbnailSource.PixelHeight;
                                asset.ThumbnailBase64 = Services.MediaProcessingService.ConvertBitmapImageToBase64(asset.ThumbnailSource);
                            }
                            asset.Duration = TimeSpan.FromSeconds(5);
                            newVideoImageAssets.Add(asset);
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }

                foreach (var asset in newVideoImageAssets)
                {
                    VideoImageAssets.Add(asset);
                }
                foreach (var asset in newAudioAssets)
                {
                    AudioAssets.Add(asset);
                }

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private async void ImportSubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Subtitle files (*.srt;*.ass)|*.srt;*.ass|All files (*.*)|*.*",
                Title = "Chọn file phụ đề",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                try
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        var asset = new MediaAsset
                        {
                            FilePath = filePath,
                            Type = AssetType.Subtitle,
                        };

                        try
                        {
                            var lines = await Task.Run(() => SrtFileUtils.LoadFromFile(filePath));
                            if (lines.Any())
                            {
                                var startTime = lines.Min(l => l.StartTime);
                                var endTime = lines.Max(l => l.EndTime);
                                asset.Duration = endTime - startTime;
                            }
                            else
                            {
                                asset.Duration = TimeSpan.FromSeconds(5);
                            }
                        }
                        catch (Exception ex)
                        {
                            asset.Duration = TimeSpan.FromSeconds(5);
                        }
                        SubtitleAssets.Add(asset);
                    }
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
        private void AddTextClip_Click(object sender, RoutedEventArgs e)
        {
            int topMostTextTrack = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Subtitle || c.ClipType == TimelineClipType.Text)
                .Select(c => c.TrackIndex)
                .DefaultIfEmpty(-1)
                .Min();
            int newTrackIndex = topMostTextTrack - 1;
            var newClipStyle = new StyleState();
            newClipStyle.FontSize = _currentProject.TemplateFontSize;
            newClipStyle.X = 0.5;
            newClipStyle.Y = 0.5;
            newClipStyle.ScaleX = 1.2;
            newClipStyle.ScaleY = 1.2;
            newClipStyle.Rotation = 0;
            newClipStyle.Width = null;
            newClipStyle.FontColorHex = "#FFFFFFFF";
            newClipStyle.EdgeStyle = TextEdgeStyle.None;
            newClipStyle.IsBackgroundEnabled = false;
            var newTextClipLine = new SrtSubtitleLine
            {
                IsTextClip = true,
                Index = (_currentProject.Subtitles.Count + _currentProject.TextClips.Count + 1),
                OriginalText = "Văn bản mới",
                StartTime = _playhead,
                Duration = TimeSpan.FromSeconds(3),
                Style = newClipStyle,
                TrackIndex = newTrackIndex
            };
            newTextClipLine.SetTimeFromTimeCodeString(newTextClipLine.TimeCode);
            _currentProject.TextClips.Add(newTextClipLine);
            SrtSubtitleLinesView.Add(newTextClipLine);

            var newClipVM = new TimelineClipViewModel(newTextClipLine);
            TimelineClips.Add(newClipVM);

            RecalculateTrackAssignments();
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            UpdateSubtitleForCurrentTime(_playhead);
            SrtLinesDataGrid.SelectedItem = newTextClipLine;
            SrtLinesDataGrid.ScrollIntoView(newTextClipLine);
            DisplaySubtitleOnPlayer(newTextClipLine);
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }
        private void MediaAsset_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is MediaAsset asset)
            {
                _dragStartPoint = e.GetPosition(null);
                ActiveMediaAsset = asset;
            }
        }
        private async Task HandleCreateSubtitlesForVideoAsync(MediaAsset asset)
        {
            if (asset == null)
            {
                CustomMessageBox.Show("Không thể xác định được clip video.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string videoPath = asset.FilePath;
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                CustomMessageBox.Show("Đường dẫn của clip video không hợp lệ hoặc file không tồn tại.", "Lỗi File", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_isTimelinePlaying)
            {
                await PauseTimelinePlayback();
            }
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                SubtitleTab.IsChecked = true;
                ModeOcrRadio.IsChecked = true;
                if (IsBatchSubtitleMode) IsBatchSubtitleMode = false;
                TimeSpan? freezePosition = null;
                if (!string.IsNullOrEmpty(_currentVideoPath) &&
                    string.Equals(_currentVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (_activeVideoClip != null)
                    {
                        freezePosition = ComputePositionInClip(_playhead, _activeVideoClip);
                    }
                    else
                    {
                        freezePosition = FFMEPlayer.Position;
                    }
                }
                else
                {
                    if (_activeVideoClip != null &&
                        _activeVideoClip.SourceData is MediaAsset activeAsset &&
                        string.Equals(activeAsset.FilePath, videoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        freezePosition = ComputePositionInClip(_playhead, _activeVideoClip);
                    }
                    else
                    {
                        freezePosition = TimeSpan.Zero;
                    }
                }
                bool setupSuccess = await SetupMediaPlayerForDisplayAsync(videoPath, freezePosition);
                if (!setupSuccess)
                {
                    CustomMessageBox.Show("Không thể tải video để tạo phụ đề. Vui lòng kiểm tra lại file.", "Lỗi Tải Video", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi khi chuẩn bị tạo phụ đề:\n\n{ex.Message}", "Lỗi Không Mong Muốn", MessageBoxButton.OK, MessageBoxImage.Error);
                await ResetVideoPlayerState();
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private async void CreateSubtitlesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MediaAsset asset)
            {
                if (asset.Type == AssetType.Video)
                {
                    await HandleCreateSubtitlesForVideoAsync(asset);
                }
                else
                {
                    CustomMessageBox.Show("Chức năng này chỉ áp dụng cho các clip video.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private double _currentVideoFps = 30.0;
        private async Task<bool> SetupMediaPlayerForDisplayAsync(string videoPath, TimeSpan? freezePosition = null)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                return false;
            await PauseTimelinePlayback();
            bool localSwitching = false;
            try
            {
                _isSwitchingVideoSource = true;
                localSwitching = true;
                if (!string.IsNullOrEmpty(_currentVideoPath) &&
                    string.Equals(_currentVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
                {
                    await FFMEPlayer.Pause();
                    if (freezePosition.HasValue)
                    {
                        int token = NewTransportVersion();
                        await FFMEPlayer.Seek(freezePosition.Value);
                        await WaitForSeekSettleAsync(freezePosition.Value, token);
                    }
                    UpdatePlaybackUI(freezePosition ?? FFMEPlayer.Position);
                    return true;
                }
                IMediaAnalysis mediaInfo = await FFProbe.AnalyseAsync(videoPath);
                var videoStream = mediaInfo.PrimaryVideoStream;
                if (videoStream == null) throw new InvalidDataException("File không chứa luồng video hợp lệ.");
                try
                {
                    if (videoStream.FrameRate > 1.0)
                        _currentVideoFps = videoStream.FrameRate;
                    else
                        _currentVideoFps = 30.0; // fallback
                }
                catch
                {
                    _currentVideoFps = 30.0;
                }
                _currentVideoPath = videoPath;
                VideoEntry.Text = _currentVideoPath;

                _videoNativeWidth = videoStream.Width;
                _videoNativeHeight = videoStream.Height;
                ApplyReferenceSurfaceSize();
                UpdateCropThumbsLayout();
                await FFMEPlayer.Open(new Uri(_currentVideoPath));
                await FFMEPlayer.Pause();
                TimeSpan target = freezePosition ?? TimeSpan.Zero;
                int openToken = NewTransportVersion();
                await FFMEPlayer.Seek(target);
                await WaitForSeekSettleAsync(target, openToken);
                _totalTimelineDuration = mediaInfo.Duration;
                totalDurationTextBlock.Text = _totalTimelineDuration.ToString(@"hh\:mm\:ss\.fff");
                UpdatePlaybackUI(target);
                return true;
            }
            catch
            {
                await ResetVideoPlayerState();
                return false;
            }
            finally
            {
                if (localSwitching)
                    _isSwitchingVideoSource = false;
            }
        }

        public class TimelineAudioClipViewModel : INotifyPropertyChanged
        {
            public TimelineAudioClip Source { get; set; }

            private double _x;
            public double X
            {
                get => _x;
                set { if (_x != value) { _x = value; OnPropertyChanged(nameof(X)); } }
            }

            private double _y;
            public double Y
            {
                get => _y;
                set { if (_y != value) { _y = value; OnPropertyChanged(nameof(Y)); } }
            }

            private double _width;
            public double Width
            {
                get => _width;
                set { if (_width != value) { _width = value; OnPropertyChanged(nameof(Width)); } }
            }

            public double Height { get; set; }
            public int TrackIndex { get; set; }
            public string FileName => Path.GetFileName(Source.FilePath);
            public List<float> WaveformData => Source.WaveformData;

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        private void MediaAsset_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
            {
                System.Windows.Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (element.DataContext is MediaAsset asset)
                    {
                        DataObject data = new DataObject(typeof(MediaAsset), asset);
                        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
                    }
                }
            }
        }
        private void TimelineClip_Container_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ContentPresenter container && container.DataContext is TimelineClipViewModel vm)
            {
                if (vm.ClipType == TimelineClipType.Subtitle)
                {

                }
            }
        }
        private void UpdateProjectReferenceDimensions(MediaAsset newAsset)
        {
            bool isProjectUnsized = CurrentProject.ProjectReferenceVideoWidth <= 0 || CurrentProject.ProjectReferenceVideoHeight <= 0;
            bool noVideoExistsOnTimeline = !TimelineClips.Any(c => c.ClipType == TimelineClipType.Video && c.SourceData != newAsset);

            if (newAsset.Type == AssetType.Video && (isProjectUnsized || noVideoExistsOnTimeline))
            {
                if (newAsset.Width > 0 && newAsset.Height > 0)
                {
                    CurrentProject.ProjectReferenceVideoWidth = newAsset.Width;
                    CurrentProject.ProjectReferenceVideoHeight = newAsset.Height;
                }
            }
        }
        private async void Timeline_Drop(object sender, DragEventArgs e)
        {
            PositionMarkerThumb.IsHitTestVisible = true;
            if (_dropHighlightBorder != null) _dropHighlightBorder.Visibility = Visibility.Collapsed;
            if (_snapGuideline != null) _snapGuideline.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(typeof(MediaAsset)))
            {
                return;
            }

            if (_isTimelinePlaying)
            {
                await PauseTimelinePlayback();
            }
            MediaAsset droppedVideoAsset = null;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || !files.Any()) return;

                LoadingOverlay.Visibility = Visibility.Visible;
                try
                {
                    foreach (var filePath in files)
                    {
                        var asset = await AddFileToMediaBinAsync(filePath);
                        if (asset == null) continue;
                        System.Windows.Point dropPositionInGrid = e.GetPosition(TracksContainerGrid);
                        if (asset.Type == AssetType.Video)
                        {
                            var lastVideoClip = TimelineClips
                                .Where(c => c.ClipType == TimelineClipType.Video && c.TrackIndex == 0)
                                .OrderBy(c => c.StartTime)
                                .LastOrDefault();

                            TimeSpan startTime = lastVideoClip != null ? lastVideoClip.EndTime : TimeSpan.Zero;

                            var newClipInstance = asset.Clone();
                            _currentProject.TimelineMediaClips.Add(newClipInstance);
                            var newVM = new TimelineClipViewModel(newClipInstance);
                            newVM.StartTime = startTime;
                            newVM.TrackIndex = 0;
                            QueueWaveformGeneration(newClipInstance);
                            TimelineClips.Add(newVM);
                            droppedVideoAsset = newClipInstance;
                        }
                        else if (asset.Type == AssetType.Subtitle)
                        {
                            double mouseAbsoluteX = dropPositionInGrid.X;
                            TimeSpan finalDroppedTime = TimeSpan.FromSeconds(Math.Max(0, mouseAbsoluteX / this._pixelsPerSecond));
                            int targetTrackIndex = CalculateTrackIndexFromY(dropPositionInGrid.Y);
                            targetTrackIndex = EnforceTrackTypeRules(asset.Type.ToTimelineClipType(), targetTrackIndex);
                            await LoadSrtFromAssetAsync(asset, finalDroppedTime, targetTrackIndex);
                        }
                        else
                        {
                            double mouseAbsoluteX = dropPositionInGrid.X;
                            TimeSpan finalDroppedTime = TimeSpan.FromSeconds(Math.Max(0, mouseAbsoluteX / this._pixelsPerSecond));
                            int targetTrackIndex = CalculateTrackIndexFromY(dropPositionInGrid.Y);
                            targetTrackIndex = EnforceTrackTypeRules(asset.Type.ToTimelineClipType(), targetTrackIndex);

                            TimelineClipViewModel newVM;
                            if (asset.Type == AssetType.Audio)
                            {
                                var newAudioClip = new TimelineAudioClip
                                {
                                    FilePath = asset.FilePath,
                                    OriginalDuration = asset.Duration,
                                    VolumeDb = asset.VolumeDb,
                                    Speed = asset.Speed,
                                    WavePeaks = asset.WavePeaks
                                };
                                _currentProject.VoicedSubtitles.Add(newAudioClip);
                                newVM = new TimelineClipViewModel(newAudioClip);
                                QueueWaveformGeneration(newAudioClip);
                            }
                            else
                            {
                                var newClipInstance = asset.Clone();
                                _currentProject.TimelineMediaClips.Add(newClipInstance);
                                newVM = new TimelineClipViewModel(newClipInstance);
                                PopulateImageFilmstrip(newVM);
                            }
                            newVM.StartTime = finalDroppedTime;
                            newVM.TrackIndex = targetTrackIndex;
                            TimelineClips.Add(newVM);
                            ResolveCollisionsForClip(newVM);
                        }
                    }
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else if (e.Data.GetDataPresent(typeof(MediaAsset)))
            {
                var asset = e.Data.GetData(typeof(MediaAsset)) as MediaAsset;
                if (asset == null) return;
                System.Windows.Point dropPositionInGrid = e.GetPosition(TracksContainerGrid);
                if (asset.Type == AssetType.Video)
                {
                    var lastVideoClip = TimelineClips
                        .Where(c => c.ClipType == TimelineClipType.Video && c.TrackIndex == 0)
                        .OrderBy(c => c.StartTime)
                        .LastOrDefault();
                    TimeSpan startTime = lastVideoClip != null ? lastVideoClip.EndTime : TimeSpan.Zero;

                    var newClipInstance = asset.Clone();
                    _currentProject.TimelineMediaClips.Add(newClipInstance);
                    var newVM = new TimelineClipViewModel(newClipInstance);
                    newVM.StartTime = startTime;
                    newVM.TrackIndex = 0;
                    QueueWaveformGeneration(newClipInstance);
                    TimelineClips.Add(newVM);
                    droppedVideoAsset = newClipInstance;
                }
                else if (asset.Type == AssetType.Subtitle)
                {
                    double mouseAbsoluteX = dropPositionInGrid.X;
                    TimeSpan finalDroppedTime = TimeSpan.FromSeconds(Math.Max(0, mouseAbsoluteX / this._pixelsPerSecond));
                    int targetTrackIndex = CalculateTrackIndexFromY(dropPositionInGrid.Y);
                    targetTrackIndex = EnforceTrackTypeRules(asset.Type.ToTimelineClipType(), targetTrackIndex);
                    await LoadSrtFromAssetAsync(asset, finalDroppedTime, targetTrackIndex);
                }
                else
                {
                    double mouseAbsoluteX = dropPositionInGrid.X;
                    TimeSpan finalDroppedTime = TimeSpan.FromSeconds(Math.Max(0, mouseAbsoluteX / this._pixelsPerSecond));
                    int targetTrackIndex = CalculateTrackIndexFromY(dropPositionInGrid.Y);
                    targetTrackIndex = EnforceTrackTypeRules(asset.Type.ToTimelineClipType(), targetTrackIndex);

                    TimelineClipViewModel newVM;
                    if (asset.Type == AssetType.Audio)
                    {
                        var newAudioClip = new TimelineAudioClip
                        {
                            FilePath = asset.FilePath,
                            OriginalDuration = asset.Duration,
                            VolumeDb = asset.VolumeDb,
                            Speed = asset.Speed,
                            WavePeaks = asset.WavePeaks
                        };
                        _currentProject.VoicedSubtitles.Add(newAudioClip);
                        newVM = new TimelineClipViewModel(newAudioClip);
                        QueueWaveformGeneration(newAudioClip);
                    }
                    else
                    {
                        var newClipInstance = asset.Clone();
                        _currentProject.TimelineMediaClips.Add(newClipInstance);
                        newVM = new TimelineClipViewModel(newClipInstance);
                        PopulateImageFilmstrip(newVM);
                    }
                    newVM.StartTime = finalDroppedTime;
                    newVM.TrackIndex = targetTrackIndex;
                    TimelineClips.Add(newVM);
                    ResolveCollisionsForClip(newVM);
                }
            }
            bool isFirstVideoClip = droppedVideoAsset != null && TimelineClips.Count(c => c.ClipType == TimelineClipType.Video) == 1;

            if (isFirstVideoClip)
            {
                UpdateProjectReferenceDimensions(droppedVideoAsset);
                videoGrid.Width = CurrentProject.ProjectReferenceVideoWidth;
                videoGrid.Height = CurrentProject.ProjectReferenceVideoHeight;
                if (droppedVideoAsset.Duration.TotalSeconds > 0 && TimelineScrollViewer.ViewportWidth > 0)
                {
                    double desiredVisibleDurationSeconds = droppedVideoAsset.Duration.TotalSeconds / 0.5;
                    double newPixelsPerSecond = TimelineScrollViewer.ViewportWidth / desiredVisibleDurationSeconds;
                    this._pixelsPerSecond = Math.Clamp(newPixelsPerSecond, GetDynamicMinPixelsPerSecond(), MAX_PIXELS_PER_SECOND);
                }
            }


            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
            await SeekTimeline(_playhead);
            e.Handled = true;
        }
        private void PopulateImageFilmstrip(TimelineClipViewModel imageClipVM)
        {
            if (imageClipVM == null || imageClipVM.ClipType != TimelineClipType.Image || imageClipVM.Thumbnail == null)
            {
                return;
            }
            imageClipVM.FilmstripThumbnails.Clear();
            const double singleThumbnailWidth = 80.0;
            if (imageClipVM.Width <= 0 || singleThumbnailWidth <= 0)
            {
                return;
            }
            int numberOfThumbnails = (int)Math.Ceiling(imageClipVM.Width / singleThumbnailWidth);
            for (int i = 0; i < numberOfThumbnails; i++)
            {
                imageClipVM.FilmstripThumbnails.Add(imageClipVM.Thumbnail);
            }
        }
        private async Task LoadSrtFromAssetAsync(MediaAsset srtAsset, TimeSpan? dropTime, int initialTrackIndex)
        {
            if (srtAsset == null || string.IsNullOrEmpty(srtAsset.FilePath) || !File.Exists(srtAsset.FilePath))
            {
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                await Task.Delay(50); // Đợi một chút để UI kịp phản hồi

                var lines = SrtFileUtils.LoadFromFile(srtAsset.FilePath);
                if (lines == null || lines.Count == 0)
                {
                    return;
                }

                TimeSpan baseOffset;
                if (dropTime.HasValue)
                {
                    // Trường hợp KÉO-THẢ: Tính toán offset để dòng đầu tiên đáp xuống đúng vị trí chuột
                    TimeSpan firstStart = lines.Min(l => l.StartTime);
                    baseOffset = dropTime.Value - firstStart;
                }
                else
                {
                    // Trường hợp NHẤN NÚT "Add to timeline": Giữ nguyên timecode gốc, không có offset
                    baseOffset = TimeSpan.Zero;
                }


                var projectTemplate = GetTemplateAsStyleState();
                var newClipsVMs = new List<TimelineClipViewModel>();
                foreach (var line in lines)
                {
                    var newStartTime = line.StartTime + baseOffset;
                    line.StartTime = (newStartTime < TimeSpan.Zero) ? TimeSpan.Zero : newStartTime;

                    line.TrackIndex = initialTrackIndex;
                    line.Style = projectTemplate.Clone();
                    SrtSubtitleLinesView.Add(line);
                    _currentProject.Subtitles.Add(line);
                    var clipVM = new TimelineClipViewModel(line);
                    TimelineClips.Add(clipVM);
                    newClipsVMs.Add(clipVM);
                }

                // Sau khi thêm tất cả, mới giải quyết va chạm
                foreach (var vm in newClipsVMs)
                {
                    ResolveCollisionsForClip(vm);
                }

                EnsureInitialTemplateWidthFitsVideo();
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể tải hoặc xử lý file phụ đề.\nLỗi: {ex.Message}", "Lỗi Tải Phụ Đề", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private TimeSpan _resizeInitialTrimStartOffset;
        private TimeSpan _resizeInitialTrimEndOffset;

        private void TimelineClip_Container_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = sender as ContentPresenter;
            var vm = container?.DataContext as TimelineClipViewModel;
            if (vm == null)
            {
                return;
            }
            if (vm.ClipType == TimelineClipType.Audio)
            {
                if (vm.SourceData is TimelineAudioClip tac)
                { }
            }
            var source = e.OriginalSource as DependencyObject;
            Thumb thumb = FindVisualParent<Thumb>(source);
            bool isResizing = (thumb != null && (thumb.Name == "LeftHandle" || thumb.Name == "RightHandle"));
            if (thumb != null && thumb.Name == "VolumeThumb")
            {
                return;
            }

            if (_isTimelinePlaying)
            {
                PauseTimelinePlayback();
                _wasPlayingBeforeDrag = true;
            }
            else
            {
                _wasPlayingBeforeDrag = false;
            }

            bool isCtrlPressed = Keyboard.Modifiers == ModifierKeys.Control;
            bool isShiftPressed = Keyboard.Modifiers == ModifierKeys.Shift;

            if (isCtrlPressed)
            {
                vm.IsSelected = !vm.IsSelected;
            }
            else if (isShiftPressed && _selectedTimelineClip != null)
            {
                if (!vm.IsSelected)
                {
                    foreach (var clip in TimelineClips.Where(c => c.IsSelected))
                    {
                        clip.IsSelected = false;
                    }
                    vm.IsSelected = true;
                }
            }
            else
            {
                if (!vm.IsSelected)
                {
                    foreach (var clip in TimelineClips.Where(c => c.IsSelected))
                    {
                        clip.IsSelected = false;
                    }
                    vm.IsSelected = true;
                }
            }
            var selectedClips = TimelineClips.Where(c => c.IsSelected).ToList();
            var newPrimarySelectedClip = selectedClips.Contains(vm) ? vm : selectedClips.FirstOrDefault();

            if (_selectedTimelineClip != newPrimarySelectedClip)
            {
                if (_selectedTimelineClip?.SourceData is INotifyPropertyChanged oldSource)
                {
                    oldSource.PropertyChanged -= SelectedClip_PropertyChanged;
                }

                _selectedTimelineClip = newPrimarySelectedClip;

                if (_selectedTimelineClip?.SourceData is INotifyPropertyChanged newSource)
                {
                    newSource.PropertyChanged += SelectedClip_PropertyChanged;
                }
            }
            ClearAllAdorners();
            this.SelectedAudioClip = null;
            this.SelectedSubtitle = null;

            if (selectedClips.Count == 1 && _selectedTimelineClip != null)
            {
                if (_selectedTimelineClip.SourceData is TimelineAudioClip audioClip)
                {
                    this.SelectedAudioClip = audioClip;
                }
                else if (_selectedTimelineClip.SourceData is SrtSubtitleLine srtLine)
                {
                    this.SelectedSubtitle = srtLine;
                    if (SrtLinesDataGrid.SelectedItem != srtLine)
                    {
                        SrtLinesDataGrid.SelectedItem = srtLine;
                    }
                    SrtLinesDataGrid.ScrollIntoView(srtLine);

                    if (_activeVisuals.TryGetValue(srtLine, out var visual))
                    {
                        AddSubtitleAdorner(visual, srtLine.Style);
                    }
                }
                else if (_selectedTimelineClip.SourceData is MediaAsset mediaAsset)
                {
                    if (mediaAsset.Type == AssetType.Video) AddVideoAdorner(mediaAsset);
                    else if (mediaAsset.Type == AssetType.Image) AddImageAdorner(mediaAsset);
                }
            }
            UpdateEditorPanelVisibility();
            _dragStartPointInGrid = e.GetPosition(TracksContainerGrid);
            _dragClipInitialX = vm.X;
            _dragClipInitialY = vm.Y;

            if (thumb != null && vm.IsSelected && isResizing)
            {
                _isPreparingToDragClip = false;
                _suppressAudioDuringResize = true;
                _currentDragMode = DragMode.ResizeClip;
                _draggedResizeHandle = thumb;
                _resizeInitialStartTime = vm.StartTime;
                _resizeInitialEndTime = vm.EndTime;
                _isModifyingVideoClip = (vm.ClipType == TimelineClipType.Video);
                if (_isModifyingVideoClip)
                {
                    _preModificationEndTime = vm.EndTime;
                }
                if (vm.SourceData is TimelineAudioClip audioClipSource)
                {
                    _resizeInitialTrimStartOffset = audioClipSource.TrimStartOffset;
                    _resizeInitialTrimEndOffset = audioClipSource.TrimEndOffset;
                }
                else if (vm.SourceData is MediaAsset mediaAssetSource)
                {
                    _resizeInitialTrimStartOffset = mediaAssetSource.TrimStartOffset;
                    _resizeInitialTrimEndOffset = mediaAssetSource.TrimEndOffset;
                }
                else
                {
                    _resizeInitialTrimStartOffset = TimeSpan.Zero;
                    _resizeInitialTrimEndOffset = TimeSpan.Zero;
                }
                ResizeGuideline.Visibility = Visibility.Visible;
            }
            else
            {
                _isPreparingToDragClip = true;
                _currentDragMode = DragMode.None;
            }

            container.CaptureMouse();
            e.Handled = true;
        }
        private void TimelineClip_Container_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var container = sender as ContentPresenter;
            if (container == null || !(container.DataContext is TimelineClipViewModel activeClip))
            {
                e.Handled = true;
                return;
            }
            var selectedClips = TimelineClips.Where(c => c.IsSelected).ToList();
            foreach (var clip in selectedClips)
            {
            }
            var contextMenu = new ContextMenu { Style = (Style)FindResource("TimelineContextMenuStyle") };
            bool canCopy = selectedClips.Count == 1 && (activeClip.ClipType == TimelineClipType.Text || activeClip.ClipType == TimelineClipType.Subtitle);
            if (canCopy)
            {
                var copyAttrItem = new MenuItem { Header = "Sao chép thuộc tính", InputGestureText = "Ctrl+Shift+C", Style = (Style)FindResource("TimelineMenuItemStyle") };
                copyAttrItem.Click += CopyAttributes_Click;
                contextMenu.Items.Add(copyAttrItem);
            }
            bool canPaste = _copiedStyleState != null && selectedClips.Any(c => c.ClipType == TimelineClipType.Text || c.ClipType == TimelineClipType.Subtitle);
            if (canPaste)
            {
                var pasteAttrItem = new MenuItem { Header = "Dán thuộc tính", InputGestureText = "Ctrl+Shift+V", Style = (Style)FindResource("TimelineMenuItemStyle") };
                pasteAttrItem.Click += PasteAttributes_Click;
                contextMenu.Items.Add(pasteAttrItem);
            }
            bool canCreateSubtitlesSingle = selectedClips.Count == 1 && activeClip.ClipType == TimelineClipType.Video;
            bool canCreateSubtitlesBatch = selectedClips.Any(c => c.ClipType == TimelineClipType.Video);

            if (canCreateSubtitlesSingle || canCreateSubtitlesBatch)
            {
                if (contextMenu.Items.Count > 0) contextMenu.Items.Add(new Separator());
            }

            if (canCreateSubtitlesSingle)
            {
                var createSubItem = new MenuItem { Header = "Tạo phụ đề", Style = (Style)FindResource("TimelineMenuItemStyle") };
                createSubItem.Click += CreateSubtitles_Click;
                contextMenu.Items.Add(createSubItem);
            }

            if (canCreateSubtitlesBatch)
            {
                var createSubtitlesBatchItem = new MenuItem { Header = "Tạo phụ đề từ clip đã chọn", Style = (Style)FindResource("TimelineMenuItemStyle") };
                createSubtitlesBatchItem.Click += CreateSubtitlesFromClipsMenuItem_Click;
                contextMenu.Items.Add(createSubtitlesBatchItem);
            }
            bool canCreateVoice = selectedClips.Any(c => c.ClipType == TimelineClipType.Text || c.ClipType == TimelineClipType.Subtitle);
            if (canCreateVoice)
            {
                if (contextMenu.Items.Count > 0 && !(contextMenu.Items[contextMenu.Items.Count - 1] is Separator))
                {
                    contextMenu.Items.Add(new Separator());
                }
                var createVoiceItem = new MenuItem { Header = "Tạo Giọng nói", Style = (Style)FindResource("TimelineMenuItemStyle") };
                createVoiceItem.Click += CreateVoice_Click;
                contextMenu.Items.Add(createVoiceItem);
            }
            bool canGroup = selectedClips.All(c => c.ClipType == TimelineClipType.Video || c.ClipType == TimelineClipType.Image) && selectedClips.Count > 1;
            bool canUngroup = selectedClips.Count == 1 && activeClip.SourceData is CompoundClip;

            if (canGroup || canUngroup)
            {
                if (contextMenu.Items.Count > 0 && !(contextMenu.Items[contextMenu.Items.Count - 1] is Separator))
                {
                    contextMenu.Items.Add(new Separator());
                }
            }

            if (canGroup)
            {
                var groupItem = new MenuItem { Header = "Tạo clip ghép", InputGestureText = "Alt+G", Style = (Style)FindResource("TimelineMenuItemStyle") };
                groupItem.Click += CreateCompoundClip_Click;
                contextMenu.Items.Add(groupItem);
            }
            if (canUngroup)
            {
                var ungroupItem = new MenuItem { Header = "Hoàn tác clip ghép", InputGestureText = "Alt+G", Style = (Style)FindResource("TimelineMenuItemStyle") };
                ungroupItem.Click += UndoCompoundClip_Click;
                contextMenu.Items.Add(ungroupItem);
            }


            if (contextMenu.Items.Count > 0)
            {
                container.ContextMenu = contextMenu;
            }
            else
            {
                e.Handled = true;
            }
        }
        private int CalculateTrackIndexFromY(double yPos)
        {
            if (TrackBackgroundsControl.ItemsSource is not List<TrackBackgroundViewModel> trackBgs || !trackBgs.Any())
            {
                return 0;
            }

            double currentY = 0;
            var sortedBgs = trackBgs
                .OrderBy(bg => bg.TrackType == TimelineClipType.Image ? 1 :
                               bg.TrackType == TimelineClipType.Subtitle || bg.TrackType == TimelineClipType.Text ? 2 :
                               bg.TrackType == TimelineClipType.Video ? 3 : 4)
                .ThenBy(bg => bg.TrackIndex)
                .ToList();

            foreach (var trackBg in sortedBgs)
            {
                double trackTotalHeight = trackBg.Height + TIMELINE_TRACK_SPACING;
                if (yPos >= currentY && yPos < currentY + trackTotalHeight)
                {
                    return trackBg.TrackIndex;
                }
                currentY += trackTotalHeight;
            }
            int lastAudioTrack = sortedBgs.Where(bg => bg.TrackType == TimelineClipType.Audio).Select(bg => bg.TrackIndex).DefaultIfEmpty(0).Max();
            return lastAudioTrack + 1;
        }

        private int EnforceTrackTypeRules(TimelineClipType clipType, int targetTrackIndex)
        {
            switch (clipType)
            {
                case TimelineClipType.Video:
                    return 0;

                case TimelineClipType.Audio:
                    return targetTrackIndex > 0 ? targetTrackIndex : 1;

                case TimelineClipType.Image:
                    return targetTrackIndex < 0 ? targetTrackIndex : -1;

                case TimelineClipType.Subtitle:
                case TimelineClipType.Text:
                    return targetTrackIndex < 0 ? targetTrackIndex : -2;

                default:
                    return targetTrackIndex;
            }
        }
        private void ResolveCollisionsForClip(TimelineClipViewModel newClip)
        {
            ResolveCollisionsForClip(newClip, null);
        }
        private void ResolveCollisionsForClip(TimelineClipViewModel newClip, ISet<TimelineClipViewModel> ignoreSet)
        {
            var newClipType = newClip.ClipType;
            bool isTextOrSub = newClipType == TimelineClipType.Subtitle || newClipType == TimelineClipType.Text;
            List<TimelineClipViewModel> relevantClips = TimelineClips.Where(c =>
                c != newClip &&
                (ignoreSet == null || !ignoreSet.Contains(c)) &&
                (c.ClipType == newClipType || (isTextOrSub && (c.ClipType == TimelineClipType.Subtitle || c.ClipType == TimelineClipType.Text)))
            ).ToList();
            bool Overlap(TimelineClipViewModel a, TimelineClipViewModel b) =>
                (a.StartTime < b.EndTime) && (a.EndTime > b.StartTime);

            int loopGuard = 0;
            while (loopGuard++ < 200)
            {
                var collision = relevantClips.FirstOrDefault(c => c.TrackIndex == newClip.TrackIndex && Overlap(c, newClip));
                if (collision == null)
                {
                    return;
                }
                if (newClipType == TimelineClipType.Audio || newClipType == TimelineClipType.Video)
                {
                    int startIdx = Math.Max(1, newClip.TrackIndex);
                    var occupied = new HashSet<int>(
                        relevantClips.Where(c => Overlap(c, newClip) && c.TrackIndex > 0).Select(c => c.TrackIndex)
                    );

                    int candidate = startIdx;
                    if (newClipType == TimelineClipType.Video)
                    {
                        return;
                    }

                    while (occupied.Contains(candidate))
                    {
                        candidate++;
                        if (candidate > int.MaxValue - 1000) break;
                    }
                    newClip.TrackIndex = candidate;
                    continue;
                }
                else if (newClipType == TimelineClipType.Image || isTextOrSub)
                {
                    int startIdx = Math.Min(-1, newClip.TrackIndex);
                    var occupied = new HashSet<int>(
                        relevantClips.Where(c => Overlap(c, newClip) && c.TrackIndex < 0).Select(c => c.TrackIndex)
                    );
                    int candidate = startIdx;
                    while (occupied.Contains(candidate))
                    {
                        candidate--;
                        if (candidate < int.MinValue + 1000) break;
                    }
                    newClip.TrackIndex = candidate;
                    continue;
                }
                else
                {
                    return;
                }
            }
        }
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                {
                    return element;
                }
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
        private static DependencyObject GetParentObject(DependencyObject child)
        {
            if (child == null) return null;


            if (child is ContentElement ce)
            {
                var parent = ContentOperations.GetParent(ce);
                if (parent != null) return parent;

                if (ce is FrameworkContentElement fce)
                    return fce.Parent;

                return null;
            }
            if (child is Visual || child is Visual3D)
                return VisualTreeHelper.GetParent(child);
            return LogicalTreeHelper.GetParent(child);
        }
        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = GetParentObject(current);
            }
            return null;
        }
        private Border _dropHighlightBorder = null;
        private Rectangle _snapGuideline = null;
        private void Timeline_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(MediaAsset)))
            {
                e.Effects = DragDropEffects.Copy;
                PositionMarkerThumb.IsHitTestVisible = false;
                if (_dropHighlightBorder == null)
                {
                    _dropHighlightBorder = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(3) };
                    DropOverlayCanvas.Children.Add(_dropHighlightBorder);
                }
                if (_snapGuideline == null)
                {
                    _snapGuideline = new Rectangle
                    {
                        Width = 0.8,
                        Fill = new SolidColorBrush(Colors.Cyan) { Opacity = 0.9 },
                        Effect = new DropShadowEffect { Color = Colors.Cyan, ShadowDepth = 0, BlurRadius = 8 },
                        IsHitTestVisible = false
                    };
                    DropOverlayCanvas.Children.Add(_snapGuideline);
                }
                _dropHighlightBorder.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsAuxUiActive)
            {
                return;
            }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                switch (e.Key)
                {
                    case Key.C:
                        CopyAttributes_Click(null, null);
                        e.Handled = true;
                        return;
                    case Key.V:
                        PasteAttributes_Click(null, null);
                        e.Handled = true;
                        return;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.N:
                        NewProjectMenuItem_Click(null, null);
                        e.Handled = true;
                        return;
                    case Key.L:
                        LoadProjectMenuItem_Click(null, null);
                        e.Handled = true;
                        return;
                    case Key.P:
                        SaveCurrentPreset_Click(null, null);
                        e.Handled = true;
                        return;
                    case Key.Delete:
                        DeleteCurrentProject_Click();
                        e.Handled = true;
                        return;
                    case Key.Z:
                        UndoState();
                        e.Handled = true;
                        return;
                    case Key.Y:
                        RedoState();
                        e.Handled = true;
                        return;
                    case Key.F:
                        OpenFindReplaceWindow();
                        e.Handled = true;
                        return;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (e.OriginalSource is System.Windows.Controls.TextBox || e.OriginalSource is Xceed.Wpf.Toolkit.WatermarkTextBox)
                {
                    return;
                }
                if (await DeleteSelectedTimelineClips())
                {
                    e.Handled = true;
                    return;
                }
                if (DeleteSelectedSrtLine())
                {
                    e.Handled = true;
                    return;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                switch (e.Key)
                {
                    case Key.Right:
                    case Key.Left:
                        {
                            // Không chiếm phím khi đang gõ trong textbox
                            if (e.OriginalSource is System.Windows.Controls.TextBox ||
                                e.OriginalSource is Xceed.Wpf.Toolkit.WatermarkTextBox)
                            {
                                return;
                            }

                            // CHẶN SỚM để không rơi xuống control khác
                            e.Handled = true;

                            // Nếu đang phát thì dừng lại để step theo frame
                            if (_isTimelinePlaying)
                            {
                                await PauseTimelinePlayback();
                            }

                            // Lấy clip đang active tại playhead (fallback clip active hiện tại)
                            var activeClip = FindActiveVideoClipAt(_playhead) ?? _activeVideoClip;

                            // DÙNG FPS ĐÃ CACHE, KHÔNG probe lại mỗi lần bấm
                            // _currentVideoFps đã được set khi mở video (mặc định 30.0 nếu không có)
                            double fps = (_currentVideoFps > 0) ? _currentVideoFps : 30.0;

                            // Tốc độ của clip (nếu có), mặc định 1.0
                            double speed = (activeClip != null && activeClip.Speed > 0) ? activeClip.Speed : 1.0;

                            // 1 frame trên timeline = (1 / fps) / speed
                            var frameOnTimeline = TimeSpan.FromSeconds(1.0 / fps / (speed <= 0 ? 1.0 : speed));

                            // Tính thời điểm mới
                            var target = (e.Key == Key.Right) ? _playhead + frameOnTimeline
                                                              : _playhead - frameOnTimeline;

                            // Clamp về khoảng hợp lệ dựa trên actualContent (nếu có) hoặc totalTimeline
                            var maxT = _actualContentDuration > TimeSpan.Zero ? _actualContentDuration : _totalTimelineDuration;
                            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                            if (maxT > TimeSpan.Zero && target > maxT) target = maxT;

                            // Làm tròn về ms để tránh drift lẻ
                            _playhead = TimeSpan.FromMilliseconds(Math.Round(target.TotalMilliseconds));

                            // Cập nhật UI & phụ đề
                            UpdatePlaybackUI(_playhead);
                            UpdateSubtitleForCurrentTime(_playhead);

                            // SEEK NHẸ: không blackout, không reload FFME (mượt khi giữ phím)
                            PreviewPlayerAtTime(_playhead); // <<< THAY cho await SeekTimeline(_playhead)

                            // Giữ playhead luôn trong vùng nhìn thấy
                            if (_pixelsPerSecond > 0)
                            {
                                double absoluteX = _playhead.TotalSeconds * _pixelsPerSecond;
                                double left = TimelineScrollViewer.HorizontalOffset;
                                double right = left + TimelineScrollViewer.ViewportWidth;
                                const double padding = 40.0;
                                if (absoluteX > right - padding)
                                    TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, absoluteX - padding));
                                else if (absoluteX < left + padding)
                                    TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, absoluteX - padding));
                            }
                            return;
                        }

                }
            }
        }
        private void OpenFindReplaceWindow()
        {
            try
            {
                // Kiểm tra có subtitle nào không
                if (SrtSubtitleLinesView == null || SrtSubtitleLinesView.Count == 0)
                {
                    CustomMessageBox.Show(
                        "Không có phụ đề nào để tìm kiếm!\nVui lòng import file SRT trước.",
                        "Thông báo",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Tạo danh sách subtitles để truyền vào FindReplaceWindow
                var subtitlesList = SrtSubtitleLinesView.ToList();

                // Tạo callback để xử lý khi user click OK
                Action<List<SrtSubtitleLine>> onApplyChanges = (updatedSubtitles) =>
                {
                    // Callback này sẽ được gọi khi user click OK trong FindReplaceWindow
                    ApplyFindReplaceChanges(updatedSubtitles);
                };

                // Tạo và hiển thị FindReplaceWindow
                var findReplaceWindow = new FindReplaceWindow(subtitlesList, onApplyChanges)
                {
                    Owner = this // Set owner để window con luôn ở trên window cha
                };

                // Hiển thị window dạng Dialog (block main window cho đến khi đóng)
                bool? result = findReplaceWindow.ShowDialog();

                // result == true nghĩa là user click OK
                // result == false nghĩa là user click Cancel hoặc đóng window
                if (result == true){}
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"Lỗi khi mở Find and Replace:\n{ex.Message}",
                    "Lỗi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplyFindReplaceChanges(List<SrtSubtitleLine> updatedSubtitles)
        {
            try
            {
                // BƯỚC 1: Cập nhật ObservableCollection

                // BƯỚC 2: Cập nhật Timeline clips
                foreach (var clip in TimelineClips.Where(c => c.SourceData is SrtSubtitleLine).ToList())
                {
                    var subtitle = clip.SourceData as SrtSubtitleLine;
                    if (subtitle != null)
                    {
                        clip.DisplayName = subtitle.OriginalText;
                        clip.RefreshPropertiesFromSource();
                    }
                }

                // BƯỚC 3: Cập nhật _currentProject
                if (_currentProject != null)
                {
                    // Không cần làm gì vì cùng reference
                }

                // BƯỚC 4: Force render lại subtitle visuals (DÒNG ĐÃ SỬA)
                if (SubtitleRenderCanvas != null)
                {
                    SubtitleRenderCanvas.InvalidateVisual();
                }
                // HOẶC XOÁ LUÔN ĐOẠN TRÊN NẾU KHÔNG CÓ SubtitleRenderCanvas

                // BƯỚC 5: Refresh DataGrid
                SrtLinesDataGrid.Items.Refresh();

                // BƯỚC 6: Cập nhật Timeline render
                UpdateTimelineScaleAndRender();

                // BƯỚC 7: Lưu state vào Undo/Redo stack
                _undoRedoService.AddState(CaptureEditorSnapshot());

                // BƯỚC 8: Lưu project
                SaveProjectCurrent();

                // BƯỚC 9: Cập nhật status (optional)
                // StatusTextBlock.Text = "Đã cập nhật thành công từ Find and Replace";
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"Lỗi khi áp dụng thay đổi:\n{ex.Message}",
                    "Lỗi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    row.IsSelected = !row.IsSelected;
                    e.Handled = true;
                }
            }
        }
        private void SrtLinesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (DeleteSelectedSrtLine())
                {
                    e.Handled = true;
                }
            }
        }
        private bool DeleteSelectedSrtLine()
        {
            if (SrtLinesDataGrid.SelectedItems.Count == 0)
            {
                return false;
            }
            var linesToDelete = SrtLinesDataGrid.SelectedItems.Cast<SrtSubtitleLine>().ToList();
            _selectedTimelineClip = null;
            SelectedSubtitle = null;
            UpdateEditorPanelVisibility();
            RemoveVideoAdorner();
            RemoveSubtitleAdorner();

            foreach (var line in linesToDelete)
            {
                SrtSubtitleLinesView.Remove(line);

                if (line.IsTextClip)
                {
                    _currentProject.TextClips.Remove(line);
                }
                else
                {
                    _currentProject.Subtitles.Remove(line);
                }

                var clipToDeleteOnTimeline = TimelineClips.FirstOrDefault(c => c.SourceData == line);
                if (clipToDeleteOnTimeline != null)
                {
                    TimelineClips.Remove(clipToDeleteOnTimeline);
                }

                if (_activeVisuals.TryGetValue(line, out var visual))
                {
                    SubtitleRenderCanvas.Children.Remove(visual);
                    _activeVisuals.Remove(line);
                }
            }
            var subtitlesToReindex = SrtSubtitleLinesView.Where(l => !l.IsTextClip).OrderBy(l => l.StartTime).ToList();
            for (int i = 0; i < subtitlesToReindex.Count; i++)
            {
                subtitlesToReindex[i].Index = i + 1;
            }

            RecalculateTrackAssignments();
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
            SrtLinesDataGrid.UnselectAll();
            return true;
        }
        private async Task<bool> DeleteSelectedTimelineClips()
        {
            var clipsToDelete = TimelineClips.Where(c => c.IsSelected).ToList();
            if (!clipsToDelete.Any())
            {
                return false;
            }
            bool wasPlaying = _isTimelinePlaying;
            if (wasPlaying)
            {
                await PauseTimelinePlayback();
            }
            _selectedTimelineClip = null;
            SelectedSubtitle = null;
            SelectedAudioClip = null;
            ActiveMediaAsset = null;
            UpdateEditorPanelVisibility();
            RemoveVideoAdorner();
            RemoveSubtitleAdorner();
            foreach (var vm in clipsToDelete)
            {
                if (vm == null || vm.SourceData == null)
                {
                    continue;
                }
                if (vm.SourceData is SrtSubtitleLine line)
                {
                    SrtSubtitleLinesView.Remove(line);
                    if (line.IsTextClip)
                    {
                        _currentProject.TextClips.Remove(line);
                    }
                    else
                    {
                        _currentProject.Subtitles.Remove(line);
                    }
                    if (_activeVisuals != null && _activeVisuals.TryGetValue(line, out var visual))
                    {
                        SubtitleRenderCanvas.Children.Remove(visual);
                        _activeVisuals.Remove(line);
                    }
                }
                else if (vm.SourceData is TimelineAudioClip audioClip)
                {
                    if (TimelineAudioClips != null)
                    {
                        TimelineAudioClips.Remove(audioClip);
                    }
                    if (_currentProject?.VoicedSubtitles != null)
                    {
                        _currentProject.VoicedSubtitles.Remove(audioClip);
                    }
                    SrtSubtitleLine matchedSubtitleLine = null;
                    if (audioClip.SourceSubtitleIndexSnapshot.HasValue)
                    {
                        int idxSnapshot = audioClip.SourceSubtitleIndexSnapshot.Value;

                        matchedSubtitleLine = SrtSubtitleLinesView
                            .FirstOrDefault(l =>
                                l != null &&
                                !l.IsTextClip &&
                                l.Index == idxSnapshot);
                    }
                    if (matchedSubtitleLine == null)
                    {
                        string clipFullPath = SafeGetFullPath(audioClip.FilePath);

                        matchedSubtitleLine = SrtSubtitleLinesView.FirstOrDefault(l =>
                            l != null &&
                            !l.IsTextClip &&
                            !string.IsNullOrWhiteSpace(l.VoicedAudioPath) &&
                            string.Equals(
                                SafeGetFullPath(l.VoicedAudioPath),
                                clipFullPath,
                                StringComparison.OrdinalIgnoreCase));
                    }
                    if (matchedSubtitleLine != null)
                    {
                        bool sameFile =
                            string.Equals(
                                SafeGetFullPath(matchedSubtitleLine.VoicedAudioPath),
                                SafeGetFullPath(audioClip.FilePath),
                                StringComparison.OrdinalIgnoreCase);

                        if (sameFile)
                        {
                            matchedSubtitleLine.IsVoiced = false;
                            matchedSubtitleLine.VoicedAudioPath = null;
                        }
                    }
                }
                else if (vm.SourceData is MediaAsset mediaInstance)
                {
                    _currentProject?.TimelineMediaClips?.Remove(mediaInstance);
                    if (_activeVideoClip == vm)
                    {
                        await SwitchActiveVideoClip(null, resume: false);
                    }
                }
            }
            foreach (var vm in clipsToDelete)
            {
                TimelineClips.Remove(vm);
            }
            var subtitlesToReindex = SrtSubtitleLinesView
                .Where(l => !l.IsTextClip)
                .OrderBy(l => l.StartTime)
                .ToList();

            for (int i = 0; i < subtitlesToReindex.Count; i++)
            {
                subtitlesToReindex[i].Index = i + 1;
            }
            RecalculateTrackAssignments();
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
            if (wasPlaying)
            {
                await StartTimelinePlayback();
            }

            return true;
        }
        private static string SafeGetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return System.IO.Path.GetFullPath(path);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private void TimelineScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                UpdateTimelineScaleAndRender();
            }
        }
        private void SaveProjectCurrent()
        {
            try
            {
                UpdateProjectStateBeforeSave();
                var snapshot = CaptureEditorSnapshot(includeViewState: true);
                subphimv1.Services.ProjectManager.SaveSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể lưu project.\nLỗi: {ex.Message}", "Lỗi Lưu", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task SaveProjectCurrentAsync()
        {
            try
            {
                UpdateProjectStateBeforeSave();
                var snapshot = CaptureEditorSnapshot(includeViewState: true);
                await subphimv1.Services.ProjectManager.SaveSnapshotAsync(snapshot);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể lưu project.\nLỗi: {ex.Message}", "Lỗi Lưu", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private EditorSnapshot CaptureEditorSnapshot(bool includeViewState = false)
        {
            UpdateProjectStateBeforeSave();
            var snap = new subphimv1.Models.EditorSnapshot();
            snap.Project = _currentProject ?? new ProjectState();
            snap.MediaBin_SubtitleAssets = SubtitleAssets?.ToList() ?? new List<MediaAsset>();
            snap.MediaBin_AudioAssets = AudioAssets?.ToList() ?? new List<MediaAsset>();
            snap.TimelineAudioClips = TimelineAudioClips?.ToList() ?? new List<TimelineAudioClip>();

            // core-state luôn lưu
            snap.PlayheadSeconds = Math.Max(0.0, _playhead.TotalSeconds);
            snap.ReferenceWidth = (CurrentProject?.ProjectReferenceVideoWidth > 0) ? CurrentProject.ProjectReferenceVideoWidth : 1280.0;
            snap.ReferenceHeight = (CurrentProject?.ProjectReferenceVideoHeight > 0) ? CurrentProject.ProjectReferenceVideoHeight : 720.0;

            // view-state CHỈ lưu khi includeViewState = true (ví dụ lúc Save Project)
            if (includeViewState)
            {
                snap.PixelsPerSecond = _pixelsPerSecond > 0 ? _pixelsPerSecond : 50.0;
                snap.TimelineScrollOffsetX = TimelineScrollViewer?.HorizontalOffset;
                snap.TimelineScrollOffsetY = TimelineScrollViewer?.VerticalOffset;
            }
            return snap;
        }
        private void ApplySnapshot(EditorSnapshot snap)
        {
            if (snap == null)
            {
                return;
            }
            CurrentProject = snap.Project ?? new ProjectState();
            _currentProjectFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects", $"{CurrentProject.ProjectName}.json");
            VideoImageAssets.Clear();
            AudioAssets.Clear();
            SubtitleAssets.Clear();
            foreach (var a in snap.MediaBin_VideoImageAssets) VideoImageAssets.Add(a);
            foreach (var a in snap.MediaBin_AudioAssets) AudioAssets.Add(a);
            foreach (var a in snap.MediaBin_SubtitleAssets) SubtitleAssets.Add(a);
            SrtSubtitleLinesView.Clear();
            foreach (var s in CurrentProject.Subtitles.OrderBy(x => x.StartTime)) SrtSubtitleLinesView.Add(s);
            foreach (var t in CurrentProject.TextClips.OrderBy(x => x.StartTime)) SrtSubtitleLinesView.Add(t);
            TimelineAudioClips.Clear();
            foreach (var c in snap.TimelineAudioClips) TimelineAudioClips.Add(c);
            TimelineClips.Clear();
            foreach (var clipAsset in CurrentProject.TimelineMediaClips)
            {
                var vm = new TimelineClipViewModel(clipAsset);
                TimelineClips.Add(vm);
            }
            foreach (var s in CurrentProject.Subtitles) TimelineClips.Add(new TimelineClipViewModel(s));
            foreach (var t in CurrentProject.TextClips) TimelineClips.Add(new TimelineClipViewModel(t));
            foreach (var tac in snap.TimelineAudioClips)
            {
                var vm = new TimelineClipViewModel(tac);
                TimelineClips.Add(vm);
            }
            if (snap.PixelsPerSecond > 0)
            {
                _pixelsPerSecond = snap.PixelsPerSecond;
            }
            _playhead = TimeSpan.FromSeconds(Math.Max(0.0, snap.PlayheadSeconds));
            CurrentProject.ProjectReferenceVideoWidth = snap.ReferenceWidth > 0 ? snap.ReferenceWidth : 1280.0;
            CurrentProject.ProjectReferenceVideoHeight = snap.ReferenceHeight > 0 ? snap.ReferenceHeight : 720.0;
            UpdatePlayerLayout();
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            UpdateEditorPanelFromState();
            UpdateAllSubtitleDisplays();
            UpdatePlaybackUI(_playhead);
            if (snap.TimelineScrollOffsetX.HasValue)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(snap.TimelineScrollOffsetX.Value);
            }
        }
        private void UndoState()
        {
            double oldZoom = _pixelsPerSecond;
            double oldOffset = TimelineScrollViewer?.HorizontalOffset ?? 0;

            var snap = _undoRedoService?.Undo();
            if (snap != null)
            {
                ApplySnapshot(snap);

                // ✅ luôn bảo toàn view hiện tại cho thao tác undo
                _pixelsPerSecond = oldZoom;
                UpdateTimelineScaleAndRender();
                TimelineScrollViewer?.ScrollToHorizontalOffset(oldOffset);

                SaveProjectCurrent(); // vẫn lưu project nếu bạn muốn
            }
        }
        private void RedoState()
        {
            double oldZoom = _pixelsPerSecond;
            double oldOffset = TimelineScrollViewer?.HorizontalOffset ?? 0;

            var snap = _undoRedoService?.Redo();
            if (snap != null)
            {
                ApplySnapshot(snap);

                // ✅ luôn bảo toàn view hiện tại cho thao tác redo
                _pixelsPerSecond = oldZoom;
                UpdateTimelineScaleAndRender();
                TimelineScrollViewer?.ScrollToHorizontalOffset(oldOffset);

                SaveProjectCurrent();
            }
        }
        private void SubtitleDisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentProject == null || _isUpdatingUiFromCode || !(SubtitleDisplayModeComboBox.SelectedItem is ComboBoxItem selectedItem))
            {
                return;
            }

            if (Enum.TryParse<SubtitleDisplayMode>(selectedItem.Tag.ToString(), out var selectedMode))
            {
                if (CurrentProject.SelectedSubtitleDisplayMode != selectedMode)
                {
                    CurrentProject.SelectedSubtitleDisplayMode = selectedMode;
                    UpdateAllSubtitleDisplays();
                    ProjectManager.SaveProject(CurrentProject);
                }
            }
        }

        private void SubtitleDisplayModeComboBox_DropDownOpened(object sender, EventArgs e)
        {
            bool hasAnyTranslatedText = SrtSubtitleLinesView.Any(l => !string.IsNullOrWhiteSpace(l.TranslatedText));
            TranslatedSubtitleOption.IsEnabled = hasAnyTranslatedText;
            if (!hasAnyTranslatedText && SubtitleDisplayModeComboBox.SelectedIndex == 1)
            {
                SubtitleDisplayModeComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateAllSubtitleDisplays()
        {
            var subtitleClips = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle).ToList();
            foreach (var clipVM in subtitleClips)
            {
                if (clipVM.SourceData is SrtSubtitleLine srtLine)
                {
                    bool useTranslated = CurrentProject.SelectedSubtitleDisplayMode == SubtitleDisplayMode.Translated && !string.IsNullOrWhiteSpace(srtLine.TranslatedText);
                    string newDisplayName = useTranslated ? srtLine.TranslatedText : srtLine.OriginalText;
                    clipVM.DisplayName = newDisplayName.Replace("\r\n", " ").Replace("\n", " ");
                }
            }
            UpdateSubtitleForCurrentTime(_playhead);
        }
        private void Timeline_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MediaAsset)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Copy;

            if (_dropHighlightBorder == null || _snapGuideline == null || this._pixelsPerSecond <= 0) return;

            var asset = e.Data.GetData(typeof(MediaAsset)) as MediaAsset;
            if (asset == null) return;

            System.Windows.Point positionInViewport = e.GetPosition(TimelineScrollViewer);
            double mouseAbsoluteX = TimelineScrollViewer.HorizontalOffset + positionInViewport.X;

            double clipWidth = asset.Duration.TotalSeconds * this._pixelsPerSecond;
            double clipHeight;
            switch (asset.Type)
            {
                case AssetType.Video:
                    clipHeight = TIMELINE_TRACK_HEIGHT;
                    _dropHighlightBorder.Background = new SolidColorBrush(((SolidColorBrush)FindResource("CapCut.AccentColorBlue")).Color) { Opacity = 0.6 };
                    break;

                case AssetType.Image:
                    clipHeight = 50;
                    _dropHighlightBorder.Background = new SolidColorBrush(Color.FromArgb(0x80, 0x8A, 0x2B, 0xE2));
                    break;

                case AssetType.Audio:
                    clipHeight = TIMELINE_AUDIO_TRACK_HEIGHT;
                    _dropHighlightBorder.Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x80, 0x60));
                    break;

                case AssetType.Subtitle:
                    clipHeight = 20;
                    _dropHighlightBorder.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xA5, 0x00));
                    break;

                default:
                    clipHeight = TIMELINE_TRACK_HEIGHT;
                    _dropHighlightBorder.Background = new SolidColorBrush(((SolidColorBrush)FindResource("CapCut.AccentColorBlue")).Color) { Opacity = 0.6 };
                    break;
            }

            _dropHighlightBorder.Width = Math.Max(10, clipWidth);
            _dropHighlightBorder.Height = clipHeight;

            double finalAbsoluteX = mouseAbsoluteX;
            bool snapped = false;
            double snapLineAbsoluteX = 0;
            const double snapToleranceInPixels = 8.0;

            double potentialStartX = mouseAbsoluteX;
            double potentialEndX = mouseAbsoluteX + clipWidth;

            _snapGuideline.Visibility = Visibility.Collapsed;

            foreach (var existingClip in TimelineClips)
            {
                double existingClipStartX = existingClip.X;
                double existingClipEndX = existingClip.X + existingClip.Width;
                if (Math.Abs(potentialStartX - existingClipStartX) < snapToleranceInPixels)
                {
                    finalAbsoluteX = existingClipStartX;
                    snapLineAbsoluteX = existingClipStartX;
                    snapped = true;
                    break;
                }
                if (Math.Abs(potentialStartX - existingClipEndX) < snapToleranceInPixels)
                {
                    finalAbsoluteX = existingClipEndX;
                    snapLineAbsoluteX = existingClipEndX;
                    snapped = true;
                    break;
                }
                if (Math.Abs(potentialEndX - existingClipStartX) < snapToleranceInPixels)
                {
                    finalAbsoluteX = existingClipStartX - clipWidth;
                    snapLineAbsoluteX = existingClipStartX;
                    snapped = true;
                    break;
                }
                if (Math.Abs(potentialEndX - existingClipEndX) < snapToleranceInPixels)
                {
                    finalAbsoluteX = existingClipEndX - clipWidth;
                    snapLineAbsoluteX = existingClipEndX;
                    snapped = true;
                    break;
                }
            }

            double finalVisualX = finalAbsoluteX - TimelineScrollViewer.HorizontalOffset;
            double finalVisualY = positionInViewport.Y - (clipHeight / 2.0);

            Canvas.SetLeft(_dropHighlightBorder, finalVisualX);
            Canvas.SetTop(_dropHighlightBorder, finalVisualY);

            if (snapped)
            {
                double snapLineVisualX = snapLineAbsoluteX - TimelineScrollViewer.HorizontalOffset;
                Canvas.SetLeft(_snapGuideline, snapLineVisualX - (_snapGuideline.Width / 2));
                Canvas.SetTop(_snapGuideline, 0);
                _snapGuideline.Height = TracksContainerGrid.ActualHeight;
                _snapGuideline.Visibility = Visibility.Visible;
            }

            e.Handled = true;
        }

        private void Timeline_DragLeave(object sender, DragEventArgs e)
        {
            PositionMarkerThumb.IsHitTestVisible = true;
            if (_dropHighlightBorder != null) _dropHighlightBorder.Visibility = Visibility.Collapsed;
            if (_snapGuideline != null) _snapGuideline.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        private void MediaBinScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                double newOffset = scrollViewer.VerticalOffset - e.Delta;
                scrollViewer.ScrollToVerticalOffset(newOffset);


                e.Handled = true;
            }
        }
        private void RecalculateTotalDuration()
        {
            if (TimelineClips.Any())
            {
                _actualContentDuration = TimelineClips.Max(c => c.EndTime);
            }
            else
            {
                _actualContentDuration = TimeSpan.Zero;
            }
            if (_actualContentDuration > TimeSpan.Zero)
            {
                double paddingSeconds = 30.0;
                _totalTimelineDuration = _actualContentDuration + TimeSpan.FromSeconds(paddingSeconds);
            }
            else
            {
                _totalTimelineDuration = TimeSpan.FromMinutes(5);
            }
            totalDurationTextBlock.Text = _actualContentDuration > TimeSpan.Zero ? _actualContentDuration.ToString(@"hh\:mm\:ss\.fff") : "00:00:00.000";
            RenderTimeline(this._pixelsPerSecond);
            DrawRuler(this._pixelsPerSecond);
            UpdatePositionMarkerVisuals();
        }
        private TimelineClipViewModel FindActiveVideoClipAt(TimeSpan t)
        {
            var mainMediaClip = TimelineClips
                .Where(c => c.TrackIndex == 0 &&
                            (c.ClipType == TimelineClipType.Video) &&
                            c.StartTime <= t && c.EndTime > t)
                .FirstOrDefault();

            if (mainMediaClip != null)
            {
                return mainMediaClip;
            }

            var foundClip = TimelineClips
                .Where(c => (c.ClipType == TimelineClipType.Video) && c.StartTime <= t && c.EndTime > t)
                .OrderByDescending(c => c.TrackIndex)
                .FirstOrDefault();

            return foundClip;
        }
        #region New Playback Engine & Timeline Interaction
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (!_isTimelinePlaying || _isUserDraggingPlayhead || _isSwitchingVideoSource || _isExplicitSeekInProgress)
            {
                if (_renderStopwatch.IsRunning)
                {
                    _renderStopwatch.Stop();
                }
                return;
            }
            bool playheadAdvanced = false;
            if (_activeVideoClip != null && FFMEPlayer.IsInitialized && FFMEPlayer.IsPlaying)
            {
                var mediaAsset = _activeVideoClip.SourceData as MediaAsset;
                var trimStart = mediaAsset?.TrimStartOffset ?? TimeSpan.Zero;
                var positionInTimeline = _activeVideoClip.StartTime + FFMEPlayer.Position - trimStart;
                if (positionInTimeline < TimeSpan.Zero) positionInTimeline = TimeSpan.Zero;
                _playhead = positionInTimeline;
                playheadAdvanced = true;
                _renderStopwatch.Restart();
            }
            if (!playheadAdvanced)
            {
                if (!_renderStopwatch.IsRunning)
                {
                    _renderStopwatch.Start();
                }
                _playhead += _renderStopwatch.Elapsed;
                _renderStopwatch.Restart();
            }
            if (_actualContentDuration > TimeSpan.Zero && _playhead >= _actualContentDuration)
            {
                _playhead = _actualContentDuration;
                PauseTimelinePlayback();
                UpdatePlaybackUI(_playhead);
                UpdateSubtitleForCurrentTime(_playhead);
                UpdateOverlaysForCurrentTime(_playhead);
                _audioEngine.Update(_playhead, TimelineClips);
                return;
            }
            UpdatePlaybackUI(_playhead);
            UpdateSubtitleForCurrentTime(_playhead);
            UpdateOverlaysForCurrentTime(_playhead);
            _audioEngine.Update(_playhead, TimelineClips);
            var expectedClip = FindActiveVideoClipAt(_playhead);
            if (expectedClip != _activeVideoClip)
            {
                _ = SwitchActiveVideoClip(expectedClip, resume: true);
            }
            double absoluteX = _playhead.TotalSeconds * _pixelsPerSecond;
            double left = TimelineScrollViewer.HorizontalOffset;
            double right = left + TimelineScrollViewer.ViewportWidth;
            const double padding = 40.0;
            if (absoluteX > right - padding)
                TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, absoluteX - padding));
            else if (absoluteX < left + padding)
                TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, absoluteX - padding));
        }
        public async Task StartTimelinePlayback()
        {
            if (_actualContentDuration > TimeSpan.Zero && _playhead >= _actualContentDuration)
            {
                await SeekTimeline(TimeSpan.Zero);
            }

            if (_isTimelinePlaying) return;

            var token = NewTransportVersion();
            _isExplicitSeekInProgress = true;

            try
            {
                _audioEngine.SeekTo(_playhead, TimelineClips);

                var clipToPlay = FindActiveVideoClipAt(_playhead);
                if (!FFMEPlayer.IsInitialized || _activeVideoClip != clipToPlay)
                {
                    _pendingWasPlaying = true;
                    await SwitchActiveVideoClip(clipToPlay, resume: true);
                }
                else if (FFMEPlayer.IsInitialized && clipToPlay != null)
                {
                    var positionInClip = ComputePositionInClip(_playhead, clipToPlay);

                    BlackoutOn();
                    await FFMEPlayer.Seek(positionInClip);
                    await WaitForSeekSettleAsync(positionInClip, token);
                    BlackoutOff();

                    await FFMEPlayer.Play();
                    _audioEngine.Play();
                    _isTimelinePlaying = true;
                    playPauseButton.Content = "\uE769";
                }
                else
                {
                    _audioEngine.Play();
                    _isTimelinePlaying = true;
                    playPauseButton.Content = "\uE769";
                }
                _renderStopwatch.Reset();
                _renderStopwatch.Start();
            }
            finally
            {
                _isExplicitSeekInProgress = false;
            }
        }
        public async Task PauseTimelinePlayback()
        {
            if (!_isTimelinePlaying) return;

            _isTimelinePlaying = false;

            if (FFMEPlayer.CanPause)
            {
                await FFMEPlayer.Pause();
            }
            _audioEngine.Pause();

            if (_activeVideoClip != null && FFMEPlayer.IsInitialized)
            {
                var mediaAsset = _activeVideoClip.SourceData as MediaAsset;
                var trimStart = mediaAsset?.TrimStartOffset ?? TimeSpan.Zero;
                _playhead = _activeVideoClip.StartTime + FFMEPlayer.Position - trimStart;
                UpdatePlaybackUI(_playhead);
                UpdateSubtitleForCurrentTime(_playhead);
            }
            _pendingWasPlaying = false;
            playPauseButton.Content = "\uE768";
            _renderStopwatch.Reset();
        }
        private async Task SeekTimeline(TimeSpan targetTime)
        {
            if (_isExplicitSeekInProgress)
            {
                return;
            }

            _isExplicitSeekInProgress = true;
            var token = NewTransportVersion();
            try
            {
                bool wasPlaying = _isTimelinePlaying;
                if (wasPlaying)
                {
                    await PauseTimelinePlayback();
                }
                var clamped = TimeSpan.FromTicks(Math.Clamp(targetTime.Ticks, 0, _totalTimelineDuration.Ticks));
                _playhead = clamped;
                UpdatePlaybackUI(_playhead);
                UpdateSubtitleForCurrentTime(_playhead);
                UpdateOverlaysForCurrentTime(_playhead);
                _audioEngine.SeekTo(_playhead, TimelineClips);
                var clip = FindActiveVideoClipAt(_playhead);
                BlackoutOn();

                if (_activeVideoClip != clip)
                {
                    _pendingSeekForNewSource = _playhead;
                    await SwitchActiveVideoClip(clip, resume: false);
                }
                else if (FFMEPlayer.IsInitialized && clip != null)
                {
                    var posInClip = ComputePositionInClip(_playhead, clip);
                    await FFMEPlayer.Pause();
                    await FFMEPlayer.Seek(posInClip);
                    await WaitForSeekSettleAsync(posInClip, token);
                }
                if (wasPlaying)
                {
                    await StartTimelinePlayback();
                }
            }
            finally
            {
                BlackoutOff();
                _isExplicitSeekInProgress = false;
            }
        }
        private async Task SwitchActiveVideoClip(TimelineClipViewModel targetClip, bool? resume = null)
        {
            if (_activeVideoClip == targetClip) return;

            _isSwitchingVideoSource = true;
            _pendingWasPlaying = resume ?? _isTimelinePlaying;
            if (_isTimelinePlaying) _isTimelinePlaying = false;
            BlackoutOn();
            StaticImagePlayer.Visibility = Visibility.Collapsed;
            if (FFMEPlayer.IsInitialized && FFMEPlayer.CanPause) await FFMEPlayer.Pause();
            _activeVideoClip = targetClip;
            UpdateVideoTransform(_activeVideoClip?.SourceData as MediaAsset);
            if (targetClip == null)
            {
                if (FFMEPlayer.IsInitialized) await FFMEPlayer.Close();
                if (_pendingWasPlaying)
                {
                    _isTimelinePlaying = true;
                    _audioEngine.Play();
                }

                _isSwitchingVideoSource = false;
                BlackoutOff();
                return;
            }
            var ma = targetClip.SourceData as MediaAsset;
            if (ma == null || string.IsNullOrEmpty(ma.FilePath) || !File.Exists(ma.FilePath))
            {
                if (FFMEPlayer.IsInitialized) await FFMEPlayer.Close();
                _isSwitchingVideoSource = false;
                BlackoutOff();
                return;
            }
            if (ma.Type == AssetType.Image)
            {
                if (FFMEPlayer.IsInitialized) await FFMEPlayer.Close();
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(ma.FilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    StaticImagePlayer.Source = bitmap;
                    StaticImagePlayer.Visibility = Visibility.Visible;
                }
                catch (Exception ex) { StaticImagePlayer.Visibility = Visibility.Collapsed; }

                _isSwitchingVideoSource = false;
                BlackoutOff();
                if (_pendingWasPlaying)
                {
                    _audioEngine.Play();
                    _isTimelinePlaying = true;
                    playPauseButton.Content = "\uE769";
                }
            }
            else if (ma.Type == AssetType.Video)
            {
                try
                {
                    _pendingSeekForNewSource = _playhead;
                    await FFMEPlayer.Open(new Uri(ma.FilePath));
                }
                catch (Exception ex)
                {
                    await ResetVideoPlayerState();
                    _isSwitchingVideoSource = false;
                    BlackoutOff();
                }
            }
        }
        private void MediaPlayer_MediaFailed_Log(object sender, ExceptionRoutedEventArgs e)
        {
            if (_isSwitchingVideoSource)
            {
                _isSwitchingVideoSource = false;
            }
        }
        private void TimelineParentGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(originalSource) != null ||
                (FindVisualParent<ContentPresenter>(originalSource)?.DataContext is TimelineClipViewModel) ||
                originalSource == PositionMarkerThumb || FindVisualParent<Thumb>(originalSource) == PositionMarkerThumb)
            {
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                _isMarqueeSelecting = true;
                _marqueeStartPoint = e.GetPosition(TracksContainerGrid);
                foreach (var clip in TimelineClips.Where(c => c.IsSelected))
                {
                    clip.IsSelected = false;
                }
                UpdateEditorPanelVisibility();
                RemoveVideoAdorner();
                RemoveSubtitleAdorner();
                Canvas.SetLeft(SelectionRectangle, _marqueeStartPoint.X);
                Canvas.SetTop(SelectionRectangle, _marqueeStartPoint.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;

                TimelineParentGrid.CaptureMouse();
                e.Handled = true;
            }
        }
        private async void PreviewPlayerAtTime(TimeSpan previewTime)
        {
            if (_isExplicitSeekInProgress || _isSwitchingVideoSource) return;
            var now = DateTime.UtcNow;
            if (now - _lastScrubSeekAt < _minScrubInterval) return;
            _lastScrubSeekAt = now;

            currentTimeTextBlock.Text = previewTime.ToString(@"hh\:mm\:ss\.fff");
            var targetClip = FindActiveVideoClipAt(previewTime);

            if (_activeVideoClip == targetClip)
            {
                if (FFMEPlayer.IsInitialized && targetClip != null)
                {
                    var pos = ComputePositionInClip(previewTime, targetClip);
                    await FFMEPlayer.Seek(pos);
                }
            }
            else
            {
                await SwitchActiveVideoClip(targetClip, resume: false);
            }
        }
        private List<TimelineClipViewModel> _draggedClipGroup = new List<TimelineClipViewModel>();
        private Dictionary<TimelineClipViewModel, System.Windows.Point> _dragGroupInitialPositions = new Dictionary<TimelineClipViewModel, System.Windows.Point>();

        private async void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isMarqueeSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                SelectionRectangle.Visibility = Visibility.Visible;
                System.Windows.Point currentPoint = e.GetPosition(TracksContainerGrid);
                double left = Math.Min(_marqueeStartPoint.X, currentPoint.X);
                double top = Math.Min(_marqueeStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(_marqueeStartPoint.X - currentPoint.X);
                double height = Math.Abs(_marqueeStartPoint.Y - currentPoint.Y);
                Canvas.SetLeft(SelectionRectangle, left);
                Canvas.SetTop(SelectionRectangle, top);
                SelectionRectangle.Width = width;
                SelectionRectangle.Height = height;

                System.Windows.Rect selectionRect = new System.Windows.Rect(left, top, width, height);
                foreach (var clipVM in TimelineClips)
                {
                    System.Windows.Rect clipRect = new System.Windows.Rect(clipVM.X, clipVM.Y, clipVM.Width, clipVM.Height);
                    clipVM.IsSelected = selectionRect.IntersectsWith(clipRect);
                }

                e.Handled = true;
                return;
            }

            if (_isAdornerDragging) return;

            var original = e.OriginalSource as DependencyObject;
            if (original != null)
            {
                if (original is SubtitleAdorner || FindVisualParent<SubtitleAdorner>(original) != null) return;
            }
            if (_isPreparingToDragClip && e.LeftButton == MouseButtonState.Pressed && _selectedTimelineClip != null)
            {
                if (IsMouseOverElement(TracksContainerGrid, e))
                {
                    System.Windows.Point cur = e.GetPosition(TracksContainerGrid);
                    if (Math.Abs(cur.X - _dragStartPointInGrid.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(cur.Y - _dragStartPointInGrid.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        _currentDragMode = DragMode.MoveClip;
                        _isPreparingToDragClip = false;

                        _draggedClipGroup = TimelineClips.Where(c => c.IsSelected).ToList();
                        _dragGroupInitialPositions.Clear();
                        foreach (var clip in _draggedClipGroup)
                        {
                            _dragGroupInitialPositions[clip] = new System.Windows.Point(clip.X, clip.Y);
                        }
                    }
                }
                else
                {
                    _isPreparingToDragClip = false;
                }
            }

            if (_currentDragMode == DragMode.None || e.LeftButton != MouseButtonState.Pressed || _selectedTimelineClip == null) return;

            switch (_currentDragMode)
            {
                case DragMode.MoveClip:
                    if (!_draggedClipGroup.Any()) break;
                    System.Windows.Point currentMousePos = e.GetPosition(TracksContainerGrid);
                    double deltaX = currentMousePos.X - _dragStartPointInGrid.X;
                    double deltaY = currentMousePos.Y - _dragStartPointInGrid.Y;

                    foreach (var clip in _draggedClipGroup)
                    {
                        var initialPos = _dragGroupInitialPositions[clip];
                        clip.X = initialPos.X + deltaX;
                        clip.Y = initialPos.Y + deltaY;
                    }
                    break;

                case DragMode.AdjustVolume:
                    var container = MainTimelineControl.ItemContainerGenerator.ContainerFromItem(_selectedTimelineClip) as ContentPresenter;
                    var canvas = FindVisualChild<Canvas>(container, "AudioCanvas");
                    if (canvas == null || canvas.ActualHeight <= 0) return;
                    var mousePosInCanvas = e.GetPosition(canvas);
                    var newTop = Math.Clamp(mousePosInCanvas.Y, 0, canvas.ActualHeight);
                    var ratio = (canvas.ActualHeight - newTop) / canvas.ActualHeight;
                    var newDb = MIN_DB + (ratio * (MAX_DB - MIN_DB));
                    _selectedTimelineClip.VolumeDb = newDb;
                    _selectedTimelineClip.VolumeDb = newDb;
                    if (_selectedTimelineClip.ClipType == TimelineClipType.Video
                        && ReferenceEquals(_selectedTimelineClip, _activeVideoClip)
                        && _activeVideoClip?.SourceData is MediaAsset)
                    {
                        FFMEPlayer.Volume = Math.Pow(10, newDb / 20.0);
                    }

                    break;

                case DragMode.ResizeClip:
                    if (_draggedResizeHandle == null) return;
                    Mouse.SetCursor(Cursors.SizeWE);
                    if (this._pixelsPerSecond <= 0) return;
                    System.Windows.Point currentMousePosInGrid = e.GetPosition(TracksContainerGrid);
                    double deltaXResize = currentMousePosInGrid.X - _dragStartPointInGrid.X;
                    TimeSpan timeDeltaOnTimeline = TimeSpan.FromSeconds(deltaXResize / this._pixelsPerSecond);
                    var currentVM = _selectedTimelineClip;
                    TimeSpan guidelineTimeOnTimeline = TimeSpan.Zero;
                    var minDurationOnTimeline = TimeSpan.FromMilliseconds(100);
                    if (currentVM.SourceData is SrtSubtitleLine srtLine)
                    {
                        if (_draggedResizeHandle.Name == "LeftHandle")
                        {
                            TimeSpan newStartTime = _resizeInitialStartTime + timeDeltaOnTimeline;
                            if (newStartTime >= _resizeInitialEndTime - minDurationOnTimeline) newStartTime = _resizeInitialEndTime - minDurationOnTimeline;
                            if (newStartTime < TimeSpan.Zero) newStartTime = TimeSpan.Zero;
                            srtLine.StartTime = newStartTime;
                            srtLine.Duration = _resizeInitialEndTime - newStartTime;
                            guidelineTimeOnTimeline = srtLine.StartTime;
                        }
                        else
                        {
                            TimeSpan newEndTime = _resizeInitialEndTime + timeDeltaOnTimeline;
                            if (newEndTime <= srtLine.StartTime + minDurationOnTimeline) newEndTime = srtLine.StartTime + minDurationOnTimeline;
                            srtLine.EndTime = newEndTime;
                            guidelineTimeOnTimeline = srtLine.EndTime;
                        }
                    }
                    else if (currentVM.SourceData is MediaAsset imageAsset && imageAsset.Type == AssetType.Image)
                    {
                        if (_draggedResizeHandle.Name == "LeftHandle")
                        {
                            TimeSpan newStartTime = _resizeInitialStartTime + timeDeltaOnTimeline;
                            if (newStartTime >= _resizeInitialEndTime - minDurationOnTimeline) newStartTime = _resizeInitialEndTime - minDurationOnTimeline;
                            if (newStartTime < TimeSpan.Zero) newStartTime = TimeSpan.Zero;

                            currentVM.StartTime = newStartTime;
                            imageAsset.Duration = _resizeInitialEndTime - newStartTime;
                            guidelineTimeOnTimeline = currentVM.StartTime;
                        }
                        else
                        {
                            TimeSpan newEndTime = _resizeInitialEndTime + timeDeltaOnTimeline;
                            if (newEndTime <= currentVM.StartTime + minDurationOnTimeline) newEndTime = currentVM.StartTime + minDurationOnTimeline;

                            imageAsset.Duration = newEndTime - currentVM.StartTime;
                            guidelineTimeOnTimeline = currentVM.EndTime;
                        }
                        PopulateImageFilmstrip(currentVM);
                    }
                    else if (currentVM.SourceData is MediaAsset || currentVM.SourceData is TimelineAudioClip)
                    {
                        double speed = currentVM.Speed;
                        TimeSpan originalDuration = (currentVM.SourceData is MediaAsset ma_dur) ? ma_dur.Duration : ((TimelineAudioClip)currentVM.SourceData).OriginalDuration;
                        TimeSpan timeDeltaInSourceFile = TimeSpan.FromSeconds(timeDeltaOnTimeline.TotalSeconds * speed);

                        if (_draggedResizeHandle.Name == "LeftHandle")
                        {
                            var newTrimStartOffset = _resizeInitialTrimStartOffset + timeDeltaInSourceFile;
                            var minEffectiveDurationInSource = TimeSpan.FromSeconds(minDurationOnTimeline.TotalSeconds * speed);
                            var maxTrimStart = originalDuration - _resizeInitialTrimEndOffset - minEffectiveDurationInSource;
                            if (newTrimStartOffset > maxTrimStart) newTrimStartOffset = maxTrimStart;
                            if (newTrimStartOffset < TimeSpan.Zero) newTrimStartOffset = TimeSpan.Zero;
                            var actualTrimDeltaInSource = newTrimStartOffset - _resizeInitialTrimStartOffset;
                            var actualTimelineDelta = TimeSpan.FromSeconds(actualTrimDeltaInSource.TotalSeconds / speed);
                            currentVM.StartTime = _resizeInitialStartTime + actualTimelineDelta;
                            if (currentVM.SourceData is MediaAsset mas) mas.TrimStartOffset = newTrimStartOffset;
                            else if (currentVM.SourceData is TimelineAudioClip tac) tac.TrimStartOffset = newTrimStartOffset;

                            guidelineTimeOnTimeline = currentVM.StartTime;

                        }
                        else
                        {
                            var newTrimEndOffset = _resizeInitialTrimEndOffset - timeDeltaInSourceFile;
                            var minEffectiveDurationInSource = TimeSpan.FromSeconds(minDurationOnTimeline.TotalSeconds * speed);
                            var maxTrimEnd = originalDuration - _resizeInitialTrimStartOffset - minEffectiveDurationInSource;
                            if (newTrimEndOffset > maxTrimEnd) newTrimEndOffset = maxTrimEnd;
                            if (newTrimEndOffset < TimeSpan.Zero) newTrimEndOffset = TimeSpan.Zero;
                            if (currentVM.SourceData is MediaAsset mas) mas.TrimEndOffset = newTrimEndOffset;
                            else if (currentVM.SourceData is TimelineAudioClip tac) tac.TrimEndOffset = newTrimEndOffset;
                            guidelineTimeOnTimeline = currentVM.StartTime + currentVM.Duration;

                        }
                    }

                    currentVM.RefreshPropertiesFromSource();

                    bool staticSmartCutOn = _currentSmartCutMode == SmartCutMode.StaticReview &&
                                            _smartCutSegmentMaps != null &&
                                            _smartCutSegmentMaps.Count > 0;

                    TimeSpan drawStart, drawDuration;

                    if (staticSmartCutOn)
                    {
                        TimeSpan mappedStart = MapSourceTimeToSmartCut(currentVM.StartTime);
                        TimeSpan mappedEnd = MapSourceTimeToSmartCut(currentVM.EndTime);
                        if (mappedEnd < mappedStart) mappedEnd = mappedStart;
                        drawStart = mappedStart;
                        drawDuration = mappedEnd - mappedStart;
                    }
                    else
                    {
                        drawStart = currentVM.StartTime;
                        drawDuration = currentVM.Duration;
                    }

                    currentVM.X = drawStart.TotalSeconds * this._pixelsPerSecond;
                    currentVM.Width = Math.Max(1.0, drawDuration.TotalSeconds * this._pixelsPerSecond);
                    if (_currentSmartCutMode == SmartCutMode.StaticReview)
                    {
                        guidelineTimeOnTimeline = MapSourceTimeToSmartCut(guidelineTimeOnTimeline);
                    }
                    double guidelineAbsolutePosition = guidelineTimeOnTimeline.TotalSeconds * this._pixelsPerSecond;
                    ResizeGuideline.Margin = new Thickness(guidelineAbsolutePosition - TimelineScrollViewer.HorizontalOffset, 0, 0, 0);

                    if (currentVM.ClipType == TimelineClipType.Video)
                    {
                        _ = PreviewVideoFrameDuringResizeAsync(currentVM, guidelineTimeOnTimeline);
                    }
                    else if (currentVM.ClipType == TimelineClipType.Image || currentVM.ClipType == TimelineClipType.Subtitle || currentVM.ClipType == TimelineClipType.Text)
                    {
                        PreviewPlayerAtTime(guidelineTimeOnTimeline);
                    }
                    break;
            }
            e.Handled = true;
        }
        private async Task PreviewVideoFrameDuringResizeAsync(TimelineClipViewModel clip, TimeSpan previewTimeOnTimeline)
        {
            if (_activeVideoClip != clip || !FFMEPlayer.IsInitialized || !FFMEPlayer.HasVideo)
            {
                return;
            }

            try
            {
                var positionInSourceFile = ComputePositionInClip(previewTimeOnTimeline, clip);
                if (positionInSourceFile < TimeSpan.Zero || (FFMEPlayer.NaturalDuration.HasValue && positionInSourceFile > FFMEPlayer.NaturalDuration.Value))
                {
                    return;
                }
                await FFMEPlayer.Seek(positionInSourceFile);
            }
            catch (Exception ex) { }
        }
        private bool IsMouseOverElement(FrameworkElement element, MouseEventArgs e)
        {
            if (element == null) return false;
            var p = e.GetPosition(element);
            return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
        }
        private async void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMarqueeSelecting)
            {
                _isMarqueeSelecting = false;
                SelectionRectangle.Visibility = Visibility.Collapsed;
                if (TimelineParentGrid.IsMouseCaptured)
                {
                    TimelineParentGrid.ReleaseMouseCapture();
                }
                System.Windows.Point endPoint = e.GetPosition(TracksContainerGrid);
                Vector dragVector = _marqueeStartPoint - endPoint;
                if (dragVector.Length < SystemParameters.MinimumHorizontalDragDistance)
                {
                    if (_totalTimelineDuration.TotalSeconds > 0 && this._pixelsPerSecond > 0)
                    {
                        if (_isTimelinePlaying)
                        {
                            await PauseTimelinePlayback();
                        }
                        double absoluteClickX = _marqueeStartPoint.X;
                        TimeSpan clickedTime = TimeSpan.FromSeconds(absoluteClickX / this._pixelsPerSecond);
                        await SeekTimeline(clickedTime);
                    }
                }
                UpdateEditorPanelVisibility();
                e.Handled = true;
                return;
            }
        }
        private async void TimelineClip_Container_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var container = sender as ContentPresenter;
            if (container == null) return;

            MoveSnapGuideline.Visibility = Visibility.Collapsed;

            if (_currentDragMode == DragMode.AdjustVolume)
            {
                if (container.IsMouseCaptured)
                {
                    container.ReleaseMouseCapture();
                }
                return;
            }

            bool wasActionTaken = false;

            if (_currentDragMode != DragMode.None && _selectedTimelineClip != null)
            {
                wasActionTaken = true;

                if (_currentDragMode == DragMode.MoveClip)
                {
                    if (this._pixelsPerSecond > 0)
                    {
                        foreach (var clip in _draggedClipGroup)
                        {
                            TimeSpan newStartTime = TimeSpan.FromSeconds(clip.X / this._pixelsPerSecond);
                            clip.StartTime = (newStartTime < TimeSpan.Zero) ? TimeSpan.Zero : newStartTime;

                            int targetTrackIndex = CalculateTrackIndexFromY(clip.Y);
                            clip.TrackIndex = EnforceTrackTypeRules(clip.ClipType, targetTrackIndex);
                        }
                        var ignoreSet = new HashSet<TimelineClipViewModel>(_draggedClipGroup);
                        foreach (var clip in _draggedClipGroup)
                        {
                            ResolveCollisionsForClip(clip, ignoreSet);
                        }

                        RecalculateTotalDuration();
                        UpdateTimelineScaleAndRender();
                    }
                }
                else if (_currentDragMode == DragMode.ResizeClip)
                {
                    if (_draggedResizeHandle?.Name == "LeftHandle")
                    {
                        if (_selectedTimelineClip.SourceData is MediaAsset mediaAssetSource)
                        {
                            var trimDelta = mediaAssetSource.TrimStartOffset - _resizeInitialTrimStartOffset;
                            var trimDeltaInTimeline = TimeSpan.FromSeconds(trimDelta.TotalSeconds / mediaAssetSource.Speed);
                            _selectedTimelineClip.StartTime = _resizeInitialStartTime + trimDeltaInTimeline;
                        }
                        else if (_selectedTimelineClip.SourceData is TimelineAudioClip audioClipSource)
                        {
                            var trimDelta = audioClipSource.TrimStartOffset - _resizeInitialTrimStartOffset;
                            _selectedTimelineClip.StartTime = _resizeInitialStartTime + TimeSpan.FromSeconds(trimDelta.TotalSeconds / audioClipSource.Speed);
                        }
                    }

                    if (_isModifyingVideoClip)
                    {
                        HandleVideoClipModification(_selectedTimelineClip, _preModificationEndTime);
                        _isModifyingVideoClip = false;
                    }

                    if (_selectedTimelineClip.IsVideo)
                    {
                        var eps = _clipEpsilon;
                        var startThreshold = _selectedTimelineClip.StartTime + eps;
                        var endThreshold = _selectedTimelineClip.EndTime - eps;
                        if (endThreshold < startThreshold)
                        {
                            startThreshold = _selectedTimelineClip.StartTime;
                            endThreshold = _selectedTimelineClip.EndTime;
                        }

                        bool inside = _playhead >= startThreshold && _playhead <= endThreshold;
                        if (!inside)
                        {
                            _playhead = (_playhead < startThreshold) ? startThreshold : endThreshold;
                            UpdatePlaybackUI(_playhead);
                            UpdateSubtitleForCurrentTime(_playhead);
                            await SeekTimeline(_playhead);
                        }
                    }
                }

                if (_currentDragMode != DragMode.AdjustVolume)
                {
                }

                if (_selectedTimelineClip.ClipType != TimelineClipType.Video && _selectedTimelineClip.ClipType != TimelineClipType.Image)
                {
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                }

                _undoRedoService.AddState(CaptureEditorSnapshot());
                await SaveProjectCurrentAsync();
            }

            if (_currentDragMode == DragMode.ResizeClip)
                ResizeGuideline.Visibility = Visibility.Collapsed;

            _currentDragMode = DragMode.None;
            _draggedResizeHandle = null;
            _isPreparingToDragClip = false;

            if (wasActionTaken)
            {
                if (_wasPlayingBeforeDrag)
                {
                    await StartTimelinePlayback();
                }
            }
            else
            {
                _wasPlayingBeforeDrag = false;
            }

            if (container.IsMouseCaptured)
            {
                container.ReleaseMouseCapture();
            }
            Mouse.SetCursor(Cursors.Arrow);
            e.Handled = true;
        }

        private void UpdateTimelineScaleAndRender()
        {
            if (TimelineScrollViewer.ViewportWidth <= 0 || _totalTimelineDuration.TotalSeconds <= 0)
                return;

            double finalTotalWidth = _totalTimelineDuration.TotalSeconds * this._pixelsPerSecond;
            TracksContainerGrid.Width = Math.Max(finalTotalWidth, TimelineScrollViewer.ViewportWidth);
            double maxOffset = Math.Max(0, TracksContainerGrid.Width - TimelineScrollViewer.ViewportWidth);
            if (TimelineScrollViewer.HorizontalOffset > maxOffset)
                TimelineScrollViewer.ScrollToHorizontalOffset(maxOffset);
            RenderVisibleClipsOnly();
            DrawRuler(this._pixelsPerSecond);
            UpdatePositionMarkerVisuals();
            TracksContainerGrid.InvalidateMeasure();
            TracksContainerGrid.UpdateLayout();
            _zoomRenderDebounce.Stop();
            _zoomRenderDebounce.Start();
        }
        #endregion
        private void Aux_DropDownOpened(object sender, EventArgs e) => EnterAuxUi();
        private void ColorPicker_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Control ctrl) return;
            ctrl.ApplyTemplate();
            Popup? popup = null;
            if (ctrl.Template != null)
            {
                popup = ctrl.Template.FindName("PART_Popup", ctrl) as Popup
                     ?? ctrl.Template.FindName("PART_DropDownPopup", ctrl) as Popup
                     ?? ctrl.Template.FindName("Popup", ctrl) as Popup;
            }

            if (popup != null)
            {
                popup.Opened -= ColorPicker_Popup_Opened;
                popup.Closed -= ColorPicker_Popup_Closed;
                popup.Opened += ColorPicker_Popup_Opened;
                popup.Closed += ColorPicker_Popup_Closed;
            }
            else
            {
            }
        }
        private bool IsInsidePopup(DependencyObject? d)
        {
            if (d == null) return false;
            var cur = d;
            while (cur != null)
            {
                if (cur is Popup) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            if (d is Visual v)
            {
                var src = PresentationSource.FromVisual(v);
                var root = src?.RootVisual;
                if (root != null && root.GetType().Name.Contains("PopupRoot"))
                    return true;
            }
            return false;
        }
        private void ColorPicker_Popup_Opened(object? sender, EventArgs e) => EnterAuxUi();
        private void ColorPicker_Popup_Closed(object? sender, EventArgs e) => ExitAuxUi();
        private async Task<List<SmartCutMapItem>> BuildSmartCutMapItemsAsync()
        {
            var items = new List<SmartCutMapItem>();

            // Flow 1: Ưu tiên lấy từ các dòng phụ đề đã được voice trong project
            var allSubtitleLines = _currentProject?.Subtitles?.Where(l => !l.IsTextClip && l.IsVoiced).OrderBy(l => l.StartTime).ToList();
            if (allSubtitleLines != null && allSubtitleLines.Any())
            {
                foreach (var line in allSubtitleLines)
                {
                    items.Add(new SmartCutMapItem
                    {
                        Line = line,
                        FinalStart = line.StartTime,
                        FinalEnd = line.EndTime
                    });
                }
                return items;
            }

            // Flow 2: Nếu không có, thử xây dựng từ các clip audio có trong project
            if (_currentProject?.VoicedSubtitles != null && _currentProject.VoicedSubtitles.Any())
            {
                var orderedClips = _currentProject.VoicedSubtitles.OrderBy(c => c.StartTime).ToList();
                foreach (var clip in orderedClips)
                {
                    var start = clip.StartTime;
                    var end = clip.EndTime;
                    int idx = clip.SourceSubtitleIndexSnapshot ?? (items.Count + 1);

                    // Tạo một SrtSubtitleLine tạm thời để chứa thông tin
                    var line = new SrtSubtitleLine
                    {
                        Index = idx,
                        StartTime = start,
                        EndTime = end,
                        IsVoiced = true,
                        VoicedAudioPath = clip.FilePath,
                        OriginalText = clip.CaptionTextSnapshot
                    };

                    items.Add(new SmartCutMapItem
                    {
                        Line = line,
                        FinalStart = start,
                        FinalEnd = end
                    });
                }
                return items;
            }

            // SỬA LỖI: Xử lý đúng kiểu trả về List<TimelineAudioClip>
            // Flow 3 (Fallback): Tải từ manifest.json nếu 2 flow trên không có kết quả
            var jsonFallbackClips = await LoadVoicedLinesFromTtsManifestJsonAsync();
            if (jsonFallbackClips != null && jsonFallbackClips.Any())
            {
                foreach (var clip in jsonFallbackClips.OrderBy(l => l.StartTime))
                {
                    // Tạo một đối tượng SrtSubtitleLine tạm thời từ dữ liệu của TimelineAudioClip
                    var tempLine = new SrtSubtitleLine
                    {
                        Index = clip.SourceSubtitleIndexSnapshot ?? (items.Count + 1),
                        StartTime = clip.StartTime,
                        EndTime = clip.EndTime,
                        IsVoiced = true,
                        VoicedAudioPath = clip.FilePath,
                        OriginalText = clip.CaptionTextSnapshot ?? string.Empty,
                    };

                    // Sử dụng đối tượng SrtSubtitleLine vừa tạo
                    items.Add(new SmartCutMapItem
                    {
                        Line = tempLine,
                        FinalStart = tempLine.StartTime,
                        FinalEnd = tempLine.EndTime
                    });
                }
            }

            return items;
        }
        private async Task<(List<subphimv1.Subphim.SrtSubtitleLine> voicedLines, List<TimelineAudioClip> timingSegments)>
            ResolveSmartCutSourcesAsync()
        {
            var mapItems = await BuildSmartCutMapItemsAsync();
            var voicedLines = mapItems.Select(m => m.Line).OrderBy(l => l.StartTime).ToList();
            var timingSegments = new List<TimelineAudioClip>();
            foreach (var l in voicedLines)
            {
                if (string.IsNullOrWhiteSpace(l.VoicedAudioPath) || !File.Exists(l.VoicedAudioPath))
                    continue;

                var info = await FFProbe.AnalyseAsync(l.VoicedAudioPath);
                var dur = info?.Duration ?? TimeSpan.Zero;
                if (dur <= TimeSpan.Zero) continue;

                var clip = new TimelineAudioClip
                {
                    FilePath = l.VoicedAudioPath,
                    StartTime = l.StartTime,
                    OriginalDuration = dur,
                    TrimStartOffset = TimeSpan.Zero,
                    TrimEndOffset = TimeSpan.Zero,
                    SourceSubtitleIndexSnapshot = l.Index,
                    CaptionTextSnapshot = l.OriginalText,
                    IsTts = true
                };
                timingSegments.Add(clip);
            }

            return (voicedLines, timingSegments);
        }

        private async Task<List<TimelineAudioClip>> LoadVoicedLinesFromTtsManifestJsonAsync(string specificFolder = null)
        {
            var result = new List<TimelineAudioClip>();
            try
            {
                string manifestPath = null;

                if (!string.IsNullOrWhiteSpace(specificFolder) && Directory.Exists(specificFolder))
                {
                    // Logic mới: Nếu có thư mục được chỉ định, tìm manifest trực tiếp trong đó.
                    manifestPath = Path.Combine(specificFolder, "smartcut_manifest.json");
                }
                else
                {
                    // Logic cũ (fallback): Tìm trong thư mục TTS mặc định của project.
                    string ttsFolder = GetLatestTtsBatchFolderForProject(_currentProject?.ProjectName);
                    if (!string.IsNullOrWhiteSpace(ttsFolder))
                    {
                        manifestPath = Path.Combine(ttsFolder, "smartcut_manifest.json");
                        if (!File.Exists(manifestPath))
                        {
                            // Fallback sâu hơn: tìm trong tất cả các thư mục con
                            var allBatchDirs = Directory.GetDirectories(
                                Path.GetDirectoryName(ttsFolder),
                                "*",
                                SearchOption.TopDirectoryOnly);

                            foreach (var dir in allBatchDirs.OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
                            {
                                var candidate = Path.Combine(dir, "smartcut_manifest.json");
                                if (File.Exists(candidate))
                                {
                                    manifestPath = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
                {
                    return null; // Trả về null để báo hiệu không tìm thấy manifest.
                }

                var json = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
                var manifest = JsonConvert.DeserializeObject<subphimv1.Subphim.TtsSmartCutManifest>(json);
                if (manifest?.Entries == null || manifest.Entries.Count == 0)
                {
                    return result; // Trả về danh sách rỗng nếu manifest không có entry.
                }

                var ordered = manifest.Entries.OrderBy(e => e.Index).ThenBy(e => e.StartMs).ToList();

                foreach (var entry in ordered)
                {
                    if (string.IsNullOrWhiteSpace(entry.AudioPath) || !File.Exists(entry.AudioPath))
                    {
                        continue; // Bỏ qua nếu file audio không tồn tại
                    }
                    try
                    {
                        var mediaInfo = await FFProbe.AnalyseAsync(entry.AudioPath);
                        var duration = mediaInfo.Duration;
                        if (duration <= TimeSpan.Zero) continue;

                        var clip = new TimelineAudioClip
                        {
                            FilePath = entry.AudioPath,
                            StartTime = TimeSpan.FromMilliseconds(entry.StartMs),
                            OriginalDuration = duration,
                            SourceSubtitleIndexSnapshot = entry.Index,
                            IsTts = true // Đánh dấu đây là clip voice TTS
                        };
                        result.Add(clip);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LoadManifest] Lỗi khi probe file audio {entry.AudioPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadVoicedLinesFromTtsManifestJsonAsync] Lỗi nghiêm trọng: {ex.Message}");
                return null; // Trả về null nếu có lỗi nghiêm trọng khi đọc file.
            }
            return result;
        }
        #region CapCut Draft Models
        private class CapCutDraft
        {
            [JsonProperty("materials")]
            public CapCutMaterials Materials { get; set; }
        }

        private class CapCutMaterials
        {
            [JsonProperty("audios")]
            public List<CapCutAudio> Audios { get; set; }

            [JsonProperty("texts")]
            public List<CapCutText> Texts { get; set; }
        }

        private class CapCutAudio
        {
            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("text_id")]
            public string TextId { get; set; }
        }

        private class CapCutText
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("content")]
            public string Content { get; set; }
        }
        private class CapCutTextContent
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }
        #endregion
        private async void ImportVoicedSubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            // SỬA LỖI: Thay thế SrtSubtitleLinesView bằng _currentProject.Subtitles để kiểm tra chính xác
            if (!_currentProject.Subtitles.Any(l => !l.IsTextClip))
            {
                CustomMessageBox.Show("Vui lòng tải một file phụ đề (.srt) vào timeline hoặc tạo phụ đề tự động trước khi import voice.", "Chưa có phụ đề", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
                {
                    Description = "Chọn thư mục chứa audio giọng đọc (hoặc project CapCut)",
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog(this).GetValueOrDefault())
                {
                    string selectedFolder = dialog.SelectedPath;
                    if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
                    {
                        CustomMessageBox.Show("Thư mục không hợp lệ.", "Import giọng đọc", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    LoadingOverlay.Visibility = Visibility.Visible;

                    // BƯỚC 1: Ưu tiên đọc lại manifest.json nếu có
                    List<TimelineAudioClip> manifestClips = null;
                    try
                    {
                        // Sử dụng hàm đã có để đọc manifest từ thư mục người dùng chọn
                        manifestClips = await LoadVoicedLinesFromTtsManifestJsonAsync(selectedFolder);
                    }
                    catch
                    {
                        manifestClips = null;
                    }

                    if (manifestClips != null && manifestClips.Count > 0)
                    {
                        // Nếu đọc thành công, ta sẽ làm mới timeline thay vì tạo thêm

                        // 1.1: Xóa tất cả các clip ViewModel là TTS khỏi timeline
                        var ttsVmsToRemove = TimelineClips
                            .Where(vm => vm.SourceData is TimelineAudioClip t && t.IsTts)
                            .ToList();
                        foreach (var vm in ttsVmsToRemove)
                        {
                            TimelineClips.Remove(vm);
                        }

                        // 1.2: Xóa tất cả các đối tượng TimelineAudioClip là TTS khỏi ProjectState
                        _currentProject.VoicedSubtitles.RemoveAll(c => c != null && c.IsTts);

                        // 1.3: Nạp lại các clip từ manifest
                        var newClipViewModels = new List<TimelineClipViewModel>();
                        foreach (var clip in manifestClips)
                        {
                            if (clip == null || string.IsNullOrWhiteSpace(clip.FilePath) || !File.Exists(clip.FilePath)) continue;

                            clip.IsTts = true; // Đảm bảo cờ IsTts được bật
                            _currentProject.VoicedSubtitles.Add(clip); // Thêm vào project

                            var newVM = new TimelineClipViewModel(clip); // Tạo ViewModel cho timeline
                            TimelineClips.Add(newVM);
                            newClipViewModels.Add(newVM);
                            QueueWaveformGeneration(clip);
                        }

                        // 1.4: Cập nhật lại toàn bộ UI và lưu trạng thái
                        if (newClipViewModels.Any())
                        {
                            AssignAudioClipsToIntelligentTracks(newClipViewModels);
                        }
                        RecalculateTotalDuration();
                        UpdateTimelineScaleAndRender();
                        _undoRedoService.AddState(CaptureEditorSnapshot());
                        await SaveProjectCurrentAsync();

                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        return; // Dừng tại đây, không chạy flow import cũ
                    }

                    // BƯỚC 2: Nếu không có manifest, chạy logic import cũ
                    string capcutDraftJson = Path.Combine(selectedFolder, "draft_content.json");
                    if (File.Exists(capcutDraftJson))
                    {
                        await HandleCapCutProjectImport(selectedFolder, capcutDraftJson);
                    }
                    else
                    {
                        await HandleGenericAudioFolderImport(selectedFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Import giọng đọc thất bại.\nLỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task HandleCapCutProjectImport(string projectPath, string draftPath)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            try
            {
                string jsonContent = await File.ReadAllTextAsync(draftPath, Encoding.UTF8);
                var draft = JsonConvert.DeserializeObject<CapCutDraft>(jsonContent);

                if (draft?.Materials?.Audios == null || draft.Materials.Texts == null)
                {
                    throw new Exception("File draft_content.json không hợp lệ hoặc thiếu thông tin 'materials'.");
                }
                var idToTextMap = new Dictionary<string, string>();
                foreach (var textItem in draft.Materials.Texts)
                {
                    if (string.IsNullOrEmpty(textItem.Id) || string.IsNullOrEmpty(textItem.Content)) continue;
                    try
                    {
                        var textContent = JsonConvert.DeserializeObject<CapCutTextContent>(textItem.Content);
                        if (textContent != null && !string.IsNullOrWhiteSpace(textContent.Text))
                        {
                            idToTextMap[textItem.Id] = textContent.Text.Trim();
                        }
                    }
                    catch { /* Bỏ qua nếu content không phải JSON hợp lệ */ }
                }
                var textToAudioPathMap = new Dictionary<string, string>();
                foreach (var audioItem in draft.Materials.Audios)
                {
                    if (string.IsNullOrEmpty(audioItem.TextId) || string.IsNullOrEmpty(audioItem.Path)) continue;

                    if (idToTextMap.TryGetValue(audioItem.TextId, out string text))
                    {
                        string wavName = Path.GetFileName(audioItem.Path);
                        if (!string.IsNullOrEmpty(wavName))
                        {
                            string fullWavPath = Path.Combine(projectPath, "textReading", wavName);
                            textToAudioPathMap[text] = fullWavPath;
                        }
                    }
                }

                if (textToAudioPathMap.Count == 0)
                {
                    CustomMessageBox.Show("Không tìm thấy thông tin voice TTS nào trong dự án CapCut.", "Không có dữ liệu", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                // SỬA LỖI: Thay thế SrtSubtitleLinesView bằng _currentProject.Subtitles
                // Dòng cũ: var allSrtLines = SrtSubtitleLinesView.Where(l => !l.IsTextClip).ToList();
                var allSrtLines = _currentProject.Subtitles.Where(l => !l.IsTextClip).ToList();
                var audioPathByIndex = new Dictionary<int, string>();
                var newClipViewModels = new List<TimelineClipViewModel>();
                int importedCount = 0;

                foreach (var line in allSrtLines)
                {
                    if (line.IsVoiced) continue;

                    string searchText = line.OriginalText.Trim();
                    if (textToAudioPathMap.TryGetValue(searchText, out string audioPath) && File.Exists(audioPath))
                    {
                        try
                        {
                            var mediaInfo = await FFProbe.AnalyseAsync(audioPath);
                            if (mediaInfo.PrimaryAudioStream == null && mediaInfo.Duration == TimeSpan.Zero) continue;

                            line.IsVoiced = true;
                            line.VoicedAudioPath = audioPath;

                            var newAudioClip = new TimelineAudioClip
                            {
                                FilePath = audioPath,
                                StartTime = line.StartTime,
                                OriginalDuration = mediaInfo.Duration,
                                TrimStartOffset = TimeSpan.Zero,
                                TrimEndOffset = TimeSpan.Zero,
                                SourceSubtitleIndexSnapshot = line.Index,
                                CaptionTextSnapshot = line.OriginalText
                            };

                            _currentProject.VoicedSubtitles.Add(newAudioClip);
                            TimelineAudioClips.Add(newAudioClip);
                            QueueWaveformGeneration(newAudioClip);
                            var newClipVM = new TimelineClipViewModel(newAudioClip);
                            TimelineClips.Add(newClipVM);
                            newClipViewModels.Add(newClipVM);
                            audioPathByIndex[line.Index] = audioPath;
                            importedCount++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Lỗi khi xử lý file audio '{audioPath}': {ex.Message}", true);
                        }
                    }
                }
                await WriteTtsSmartCutManifestAsync(Path.Combine(projectPath, "textReading"), allSrtLines, audioPathByIndex, _currentSrtFilePath);
                if (importedCount > 0)
                {
                    AssignAudioClipsToIntelligentTracks(newClipViewModels);
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                    _undoRedoService.AddState(CaptureEditorSnapshot());
                    await SaveProjectCurrentAsync();
                }
                else
                {
                    CustomMessageBox.Show("Không tìm thấy voice nào khớp với nội dung các dòng phụ đề hiện tại.", "Không khớp", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Lỗi khi import từ dự án CapCut: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private async Task HandleGenericAudioFolderImport(string selectedFolder)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            try
            {
                var audioFiles = Directory.GetFiles(selectedFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, new NaturalStringComparer())
                    .ToList();

                if (!audioFiles.Any())
                {
                    CustomMessageBox.Show("Không tìm thấy file âm thanh nào trong thư mục đã chọn.", "Không có file", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // SỬA LỖI: Thay thế SrtSubtitleLinesView bằng _currentProject.Subtitles
                // Dòng cũ: var allSrtLines = SrtSubtitleLinesView.Where(l => !l.IsTextClip).ToList();
                var allSrtLines = _currentProject.Subtitles.Where(l => !l.IsTextClip).ToList();
                var srtLinesByIndex = allSrtLines.ToDictionary(line => line.Index, line => line);
                var audioPathByIndex = new Dictionary<int, string>();
                int importedCount = 0;
                var newClipViewModels = new List<TimelineClipViewModel>();
                const double timeToleranceMs = 300.0;

                foreach (var audioPath in audioFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(audioPath);
                    var parts = fileName.Split('_');
                    SrtSubtitleLine targetLine = null;
                    if (parts.Length > 0 && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int indexFromFile))
                    {
                        srtLinesByIndex.TryGetValue(indexFromFile, out targetLine);
                    }
                    if (targetLine == null && parts.Length >= 3)
                    {
                        if (long.TryParse(parts[parts.Length - 2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long startMs) &&
                            long.TryParse(parts[parts.Length - 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long endMs))
                        {
                            var startFromFile = TimeSpan.FromMilliseconds(startMs);
                            targetLine = allSrtLines.FirstOrDefault(l =>
                                !l.IsVoiced &&
                                Math.Abs((l.StartTime - startFromFile).TotalMilliseconds) <= timeToleranceMs);
                        }
                    }
                    if (targetLine == null)
                    {
                        // Fallback: Gán cho dòng chưa có voice tiếp theo theo thứ tự thời gian
                        targetLine = allSrtLines.Where(l => !l.IsVoiced).OrderBy(l => l.StartTime).FirstOrDefault();
                    }

                    if (targetLine != null && !targetLine.IsVoiced)
                    {
                        try
                        {
                            var mediaInfo = await FFProbe.AnalyseAsync(audioPath);
                            if (mediaInfo.PrimaryAudioStream == null && mediaInfo.Duration == TimeSpan.Zero) continue;

                            targetLine.IsVoiced = true;
                            targetLine.VoicedAudioPath = audioPath;

                            var newAudioClip = new TimelineAudioClip
                            {
                                FilePath = audioPath,
                                StartTime = targetLine.StartTime,
                                OriginalDuration = mediaInfo.Duration,
                                TrimStartOffset = TimeSpan.Zero,
                                TrimEndOffset = TimeSpan.Zero,
                                SourceSubtitleIndexSnapshot = targetLine.Index,
                                CaptionTextSnapshot = targetLine.OriginalText
                            };
                            _currentProject.VoicedSubtitles.Add(newAudioClip);
                            TimelineAudioClips.Add(newAudioClip);
                            QueueWaveformGeneration(newAudioClip);
                            var newClipVM = new TimelineClipViewModel(newAudioClip);
                            TimelineClips.Add(newClipVM);
                            newClipViewModels.Add(newClipVM);
                            audioPathByIndex[targetLine.Index] = audioPath;
                            importedCount++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Lỗi khi xử lý file audio '{audioPath}': {ex.Message}", true);
                        }
                    }
                }

                await WriteTtsSmartCutManifestAsync(selectedFolder, allSrtLines, audioPathByIndex, _currentSrtFilePath);

                if (importedCount > 0)
                {
                    AssignAudioClipsToIntelligentTracks(newClipViewModels);
                    RecalculateTotalDuration();
                    UpdateTimelineScaleAndRender();
                    _undoRedoService.AddState(CaptureEditorSnapshot());
                    await SaveProjectCurrentAsync();
                }
                else
                {
                    CustomMessageBox.Show("Không có file âm thanh nào trong thư mục khớp được với các dòng phụ đề.", "Không khớp", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Lỗi khi import từ thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private void AssignAudioClipsToIntelligentTracks(List<TimelineClipViewModel> newAudioClips)
        {
            var existingNonAudioClips = TimelineClips
                .Except(newAudioClips)
                .Where(c => c.ClipType != TimelineClipType.Audio);

            int audioTrackOffset = 0;
            if (existingNonAudioClips.Any())
            {
                audioTrackOffset = existingNonAudioClips.Max(c => c.TrackIndex) + 1;
            }
            var trackEndTimes = new Dictionary<int, TimeSpan>();
            var existingAudioClips = TimelineClips.Except(newAudioClips).Where(c => c.ClipType == TimelineClipType.Audio);
            foreach (var clip in existingAudioClips)
            {
                if (!trackEndTimes.ContainsKey(clip.TrackIndex) || clip.EndTime > trackEndTimes[clip.TrackIndex])
                {
                    trackEndTimes[clip.TrackIndex] = clip.EndTime;
                }
            }
            var sortedNewClips = newAudioClips.OrderBy(c => c.StartTime).ToList();
            var tolerance = TimeSpan.FromMilliseconds(10);
            foreach (var newClip in sortedNewClips)
            {
                bool placed = false;
                int potentialTrackIndex = audioTrackOffset;

                while (!placed)
                {
                    if (!trackEndTimes.ContainsKey(potentialTrackIndex))
                    {
                        trackEndTimes[potentialTrackIndex] = TimeSpan.Zero;
                    }

                    if (newClip.StartTime >= trackEndTimes[potentialTrackIndex] - tolerance)
                    {
                        newClip.TrackIndex = potentialTrackIndex;
                        trackEndTimes[potentialTrackIndex] = newClip.EndTime;
                        placed = true;
                    }
                    else
                    {
                        potentialTrackIndex++;
                    }
                }
            }
        }
        private void InitializeStylePresets()
        {
            _stylePresets = new List<StyleState>
    {
        new StyleState { EdgeStyle = TextEdgeStyle.None, IsBackgroundEnabled = false },
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FF000000", OutlineThickness = 2.5 },
         new StyleState { FontColorHex = "#FFFFEB3B", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FF000000", OutlineThickness = 2.5 },
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Shadow, ShadowColorHex = "#B3000000", ShadowBlur = 5, ShadowDepth = 3, ShadowDirection = 315 },
        new StyleState { FontColorHex = "#FFFFEB3B", EdgeStyle = TextEdgeStyle.None, IsBackgroundEnabled = true, BackgroundColorHex = "#FF000000", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { FontColorHex = "#FFFF5252", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FFFFFFFF", OutlineThickness = 2.5 },
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FFFF5252", OutlineThickness = 2.5 },
        new StyleState { FontColorHex = "#FF448AFF", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FFFFFFFF", OutlineThickness = 2.5 },
        new StyleState { FontColorHex = "#FF69F0AE", EdgeStyle = TextEdgeStyle.Outline, OutlineColorHex = "#FF000000", OutlineThickness = 2.5 },
        new StyleState { IsBackgroundEnabled = true, BackgroundColorHex = "#FFCCCCCC", FontColorHex = "#FF000000", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { IsBackgroundEnabled = true, BackgroundColorHex = "#FFFFEB3B", FontColorHex = "#FF000000", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { IsBackgroundEnabled = true, BackgroundColorHex = "#FF7C4DFF", FontColorHex = "#FFFFFFFF", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { IsBackgroundEnabled = true, BackgroundColorHex = "#FFFFFFFF", FontColorHex = "#FF000000", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { IsBackgroundEnabled = true, BackgroundColorHex = "#FF000000", FontColorHex = "#FFFFFFFF", BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4 },
        new StyleState { FontColorHex = "#FF00E676", IsBackgroundEnabled = true, BackgroundColorHex = "#FF000000", EdgeStyle = TextEdgeStyle.None, BackgroundPaddingX = 10, BackgroundPaddingY = 5, BackgroundCornerRadius = 4},
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Shadow, ShadowColorHex = "#B3FF1744", ShadowBlur = 8, ShadowDepth = 0 },
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Shadow, ShadowColorHex = "#B3FFD600", ShadowBlur = 8, ShadowDepth = 0 },
        new StyleState { FontColorHex = "#FFFFFFFF", EdgeStyle = TextEdgeStyle.Shadow, ShadowColorHex = "#B376FF03", ShadowBlur = 8, ShadowDepth = 0 },
    };

            PresetItemsControl.ItemsSource = _stylePresets;
        }

        private void ApplyStylePreset(StyleState preset)
        {
            if (preset == null) return;

            _isUpdatingUiFromCode = true;

            try
            {
                FontColorPicker.SelectedColor = (Color?)ColorConverter.ConvertFromString(preset.FontColorHex);
                BackgroundEnabledCheckBox.IsChecked = preset.IsBackgroundEnabled;
                OutlineEnabledCheckBox.IsChecked = !preset.IsBackgroundEnabled && preset.EdgeStyle == TextEdgeStyle.Outline;
                ShadowEnabledCheckBox.IsChecked = !preset.IsBackgroundEnabled && preset.EdgeStyle == TextEdgeStyle.Shadow;

                if (preset.IsBackgroundEnabled)
                {
                    var bgColor = (Color)ColorConverter.ConvertFromString(preset.BackgroundColorHex);
                    BackgroundColorPicker.SelectedColor = Color.FromRgb(bgColor.R, bgColor.G, bgColor.B);
                    if (ColorConverter.ConvertFromString(preset.BackgroundColorHex) is Color c)
                    {
                        BackgroundOpacitySlider.Value = c.A * 100.0 / 255.0;
                    }

                    BackgroundCornerRadiusSlider.Value = preset.BackgroundCornerRadius;
                    BackgroundPaddingXSlider.Value = preset.BackgroundPaddingX;
                    BackgroundPaddingYSlider.Value = preset.BackgroundPaddingY;
                }
                if (OutlineEnabledCheckBox.IsChecked == true)
                {
                    OutlineColorPicker.SelectedColor = (Color?)ColorConverter.ConvertFromString(preset.OutlineColorHex);
                    OutlineThicknessSlider.Value = preset.OutlineThickness;
                }
                if (ShadowEnabledCheckBox.IsChecked == true)
                {
                    var shadowColor = (Color)ColorConverter.ConvertFromString(preset.ShadowColorHex);
                    ShadowColorPicker.SelectedColor = Color.FromRgb(shadowColor.R, shadowColor.G, shadowColor.B);
                    ShadowOpacitySlider.Value = shadowColor.A * 100.0 / 255.0;
                    ShadowBlurSlider.Value = preset.ShadowBlur;
                    ShadowDepthSlider.Value = preset.ShadowDepth;
                    ShadowDirectionSlider.Value = preset.ShadowDirection;
                }
            }
            finally
            {
                _isUpdatingUiFromCode = false;
            }
            EditorControl_ValueChanged(null, null);
        }
        private TimelineClipViewModel _draggedVolumeClip = null;

        private void VolumeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is Thumb thumb && thumb.DataContext is TimelineClipViewModel vm)
            {
                _draggedVolumeClip = vm;
                _currentDragMode = DragMode.AdjustVolume;
                if (!vm.IsSelected)
                {
                    foreach (var clip in TimelineClips.Where(c => c.IsSelected))
                    {
                        clip.IsSelected = false;
                    }
                    vm.IsSelected = true;
                    _selectedTimelineClip = vm;
                    UpdateEditorPanelVisibility();
                }
            }
            e.Handled = true;
        }

        private void VolumeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_draggedVolumeClip == null || !(sender is Thumb thumb)) return;
            var container = MainTimelineControl.ItemContainerGenerator.ContainerFromItem(_draggedVolumeClip) as ContentPresenter;
            var canvas = FindVisualChild<Canvas>(container, "AudioCanvas");
            if (canvas == null || canvas.ActualHeight <= 0) return;
            double currentTop = Canvas.GetTop(thumb);
            double newTop = Math.Clamp(currentTop + e.VerticalChange, 0, canvas.ActualHeight);
            double ratio = (canvas.ActualHeight - newTop) / canvas.ActualHeight;
            var newDb = MIN_DB + (ratio * (MAX_DB - MIN_DB));
            _draggedVolumeClip.VolumeDb = newDb;
        }

        private async void VolumeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_draggedVolumeClip != null)
            {
                _undoRedoService.AddState(CaptureEditorSnapshot());
                _draggedVolumeClip = null;
            }
            _currentDragMode = DragMode.None;
            e.Handled = true;
        }
        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is StyleState preset)
            {
                ApplyStylePreset(preset);
            }
        }
        private void RewindTimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CustomTimeAdjustTextBox.Text, out int msValue) || msValue <= 0)
            {
                CustomMessageBox.Show("Vui lòng nhập một số dương hợp lệ cho mili giây.", "Giá trị không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AdjustSubtitleTimings(TimeSpan.FromMilliseconds(-msValue), TimeSpan.Zero);
        }

        private void AdvanceTimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CustomTimeAdjustTextBox.Text, out int msValue) || msValue <= 0)
            {
                CustomMessageBox.Show("Vui lòng nhập một số dương hợp lệ cho mili giây.", "Giá trị không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AdjustSubtitleTimings(TimeSpan.Zero, TimeSpan.FromMilliseconds(msValue));
        }

        private void AdjustSubtitleTimings(TimeSpan startTimeOffset, TimeSpan durationOffset)
        {
            var subtitlesToAdjust = _currentProject.Subtitles
                                      .Where(s => !s.IsTextClip)
                                      .OrderBy(s => s.StartTime)
                                      .ToList();


            if (!subtitlesToAdjust.Any())
            {
                CustomMessageBox.Show("Không có phụ đề nào trong dự án để hiệu chỉnh.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int adjustedCount = 0;
            for (int i = 0; i < subtitlesToAdjust.Count; i++)
            {
                var currentLine = subtitlesToAdjust[i];
                if (startTimeOffset.TotalMilliseconds < 0)
                {
                    var newStartTime = currentLine.StartTime + startTimeOffset;
                    if (i > 0)
                    {
                        var prevLine = subtitlesToAdjust[i - 1];
                        if (newStartTime <= prevLine.EndTime)
                        {
                            continue;
                        }
                    }
                    currentLine.StartTime = (newStartTime < TimeSpan.Zero) ? TimeSpan.Zero : newStartTime;
                    adjustedCount++;
                }
                if (durationOffset.TotalMilliseconds > 0)
                {
                    var newEndTime = currentLine.EndTime + durationOffset;
                    if (i + 1 < subtitlesToAdjust.Count)
                    {
                        var nextLine = subtitlesToAdjust[i + 1];
                        if (newEndTime >= nextLine.StartTime)
                        {
                            continue;
                        }
                    }
                    var newDuration = currentLine.Duration + durationOffset;
                    currentLine.Duration = (newDuration < TimeSpan.Zero) ? TimeSpan.Zero : newDuration;
                    adjustedCount++;
                }
            }
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
            string action = startTimeOffset != TimeSpan.Zero ? "lùi thời gian bắt đầu" : "tăng thời gian kết thúc";
            CustomMessageBox.Show($"Đã {action} thành công cho {adjustedCount} / {subtitlesToAdjust.Count} dòng phụ đề.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private double DetectMainVideoSlowFactorOrDefault(double defaultValue)
        {
            double? pick()
            {
                if (_activeVideoClip?.SourceData is MediaAsset act) return act.Speed;
                var firstVideo = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
                if (firstVideo?.SourceData is MediaAsset mv) return mv.Speed;
                return null;
            }

            var v = pick();
            if (!v.HasValue || v.Value <= 0) return defaultValue;
            return v.Value;
        }
        private void ApplySmartCutScaleToSubtitlesAndVoices(double slowedFactor)
        {
            if (slowedFactor <= 0) slowedFactor = 0.6;
            double scale = 1.0 / slowedFactor;

            foreach (var line in SrtSubtitleLinesView.ToList())
            {
                var newStart = TimeSpan.FromMilliseconds(Math.Round(line.StartTime.TotalMilliseconds * scale));
                var newDur = TimeSpan.FromMilliseconds(Math.Round(line.Duration.TotalMilliseconds * scale));
                line.StartTime = newStart;
                line.Duration = newDur;
            }
            if (_currentProject?.VoicedSubtitles != null)
            {
                foreach (var ac in _currentProject.VoicedSubtitles)
                {
                    var newStart = TimeSpan.FromMilliseconds(Math.Round(ac.StartTime.TotalMilliseconds * scale));
                    ac.StartTime = newStart;
                }
            }
            foreach (var vm in TimelineClips)
            {
                if (vm.SourceData is SrtSubtitleLine s)
                {
                    vm.RefreshPropertiesFromSource();
                    vm.X = s.StartTime.TotalSeconds * _pixelsPerSecond;
                    vm.Width = Math.Max(1.0, s.Duration.TotalSeconds * _pixelsPerSecond);
                }
                else if (vm.SourceData is TimelineAudioClip tac)
                {
                    vm.RefreshPropertiesFromSource();
                    vm.X = tac.StartTime.TotalSeconds * _pixelsPerSecond;
                    vm.Width = Math.Max(1.0, tac.EffectiveDuration.TotalSeconds * _pixelsPerSecond);
                }
            }
        }

        private async void SmartCutButton_Click(object sender, RoutedEventArgs e)
        {
            var (hasAccess, message) = await ApiService.CheckApiAccessAsync("SmartCut");
            if (!hasAccess)
            {
                CustomMessageBox.Show(message, "Không có quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            IsSmartCutPopupOpen = true;
            var selectedMode = await _smartCutService.GetSmartCutModeAsync();
            IsSmartCutPopupOpen = false;

            if (!selectedMode.HasValue)
            {
                return;
            }

            var picked = selectedMode.Value;
            _currentSmartCutMode = picked;
            _smartCutSubtitleTimeline = null;

            if (picked == SmartCutMode.None)
            {
                _smartCutSegmentMaps = null;
                if (TimelineClips != null)
                {
                    foreach (var vm in TimelineClips)
                        vm.SetRenderTimes(TimeSpan.Zero, TimeSpan.Zero);
                }
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                SmartCutButton.Content = "SmartCut:OFF";
                SmartCutButton.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (picked == SmartCutMode.DynamicVideo)
            {
                double slowed = DetectMainVideoSlowFactorOrDefault(0.6);
                slowed = Math.Clamp(slowed, 0.1, 4.0);
                ApplySmartCutScaleToSubtitlesAndVoices(slowed);
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                SmartCutButton.Content = "ON:Dynamic";
                SmartCutButton.Background = new SolidColorBrush(Color.FromRgb(120, 160, 220));
            }
            else if (picked == SmartCutMode.StaticReview || picked == SmartCutMode.ParallelDynamic)
            {
                double slowed = DetectMainVideoSlowFactorOrDefault(0.6);
                slowed = Math.Clamp(slowed, 0.1, 4.0);
                ApplySmartCutScaleToSubtitlesAndVoices(slowed);
                bool restored = TryRestoreStaticReviewFromCache(slowed);
                if (!restored)
                {
                    await RecomputeStaticReviewVirtualTimelineAsync();
                    SaveStaticReviewCacheJson(slowed, _totalTimelineDuration);
                }
                UpdateTimelineScaleAndRender();
                SmartCutButton.Content = "ON:Static";
                SmartCutButton.Background = new SolidColorBrush(Color.FromRgb(120, 200, 120));
            }
        }
        private void SmartCutMode1Button_Click(object sender, RoutedEventArgs e)
        {
            // Mode 1 sẽ kích hoạt logic StaticReview đã có
            _currentSmartCutMode = SmartCutMode.StaticReview;
            _smartCutService.SetResult(SmartCutMode.StaticReview);
        }
        private void SmartCutMode2Button_Click(object sender, RoutedEventArgs e)
        {
            // Mode 2 sẽ kích hoạt logic ParallelDynamic để xuất nhanh hơn
            _currentSmartCutMode = SmartCutMode.ParallelDynamic;
            _smartCutService.SetResult(SmartCutMode.ParallelDynamic);
        }
        private void SmartCutDynamicSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string content)
            {
                string speedValueString = content.Replace("x", "").Trim();
                if (double.TryParse(speedValueString, NumberStyles.Any, CultureInfo.InvariantCulture, out double speedValue))
                {
                    _smartCutDynamicSpeed = speedValue;
                    _smartCutService.SetResult(SmartCutMode.DynamicVideo);
                }
                else
                {
                    _smartCutDynamicSpeed = 0.6;
                    _smartCutService.SetResult(SmartCutMode.DynamicVideo);
                }
            }
        }

        private void SmartCutPopup_Disable_Click(object sender, RoutedEventArgs e)
        {
            _smartCutService.SetResult(SmartCutMode.None);
        }
        private void SmartCutPopup_Closed(object sender, EventArgs e)
        {
            _smartCutService.Cancel();
        }
        private static string GetLatestTtsBatchFolderForProject(string projectName)
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects", "TTS", projectName ?? "Untitled");
            if (!Directory.Exists(root)) return null;
            var subDirs = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            if (subDirs == null || subDirs.Length == 0) return null;
            var latest = subDirs.OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).FirstOrDefault();
            return latest;
        }

        private async Task WriteTtsSmartCutManifestAsync(
            string ttsBatchFolder,
            List<subphimv1.Subphim.SrtSubtitleLine> voicedLines,
            Dictionary<int, string> audioPathByIndex,
            string sourceSrtPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ttsBatchFolder)) return;
                Directory.CreateDirectory(ttsBatchFolder);

                var manifest = new subphimv1.Subphim.TtsSmartCutManifest
                {
                    ProjectName = _currentProject?.ProjectName ?? "Untitled Project",
                    TtsBatchFolder = ttsBatchFolder,
                    SourceSrtPath = sourceSrtPath,
                    CreatedAtUtc = DateTime.UtcNow,
                    Entries = new List<subphimv1.Subphim.TtsSmartCutEntry>()
                };

                foreach (var line in voicedLines.OrderBy(l => l.StartTime))
                {
                    if (!audioPathByIndex.TryGetValue(line.Index, out var audioPath)) continue;

                    var entry = new subphimv1.Subphim.TtsSmartCutEntry
                    {
                        Index = line.Index,
                        AudioPath = audioPath,
                        StartMs = (long)Math.Round(line.StartTime.TotalMilliseconds),
                        EndMs = (long)Math.Round(line.EndTime.TotalMilliseconds)
                    };
                    manifest.Entries.Add(entry);
                }

                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                var jsonPath = Path.Combine(ttsBatchFolder, "smartcut_manifest.json");
                await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8);
            }
            catch (Exception ex) { }
        }

        private void SwitchToClip(TimelineClipViewModel clip)
        {
            ClearAllAdorners();
            SelectedSubtitle = null;
            SelectedAudioClip = null;
            if (_selectedTimelineClip != null && _selectedTimelineClip != clip)
            {
                if (_selectedTimelineClip.SourceData is INotifyPropertyChanged oldSource)
                {
                    oldSource.PropertyChanged -= SelectedClip_PropertyChanged;
                }
                _selectedTimelineClip.IsSelected = false;
            }
            _selectedTimelineClip = clip;
            if (_selectedTimelineClip != null)
            {
                _selectedTimelineClip.IsSelected = true;
                if (_selectedTimelineClip.SourceData is INotifyPropertyChanged newSource)
                {
                    newSource.PropertyChanged += SelectedClip_PropertyChanged;
                }
                switch (_selectedTimelineClip.ClipType)
                {
                    case TimelineClipType.Subtitle:
                    case TimelineClipType.Text:
                        if (_selectedTimelineClip.SourceData is SrtSubtitleLine srt)
                        {
                            SelectedSubtitle = srt;
                            if (_activeVisuals.TryGetValue(srt, out var visual))
                            {
                                AddSubtitleAdorner(visual, srt.Style);
                            }
                        }
                        break;

                    case TimelineClipType.Video:
                        if (_selectedTimelineClip.SourceData is MediaAsset videoAsset)
                        {
                            if (!_suppressAdornerForHardSub)
                            {
                                AddVideoAdorner(videoAsset);
                            }
                        }
                        break;

                    case TimelineClipType.Image:
                        if (_selectedTimelineClip.SourceData is MediaAsset imageAsset)
                        {
                            AddImageAdorner(imageAsset);
                        }
                        break;

                    case TimelineClipType.Audio:
                        if (_selectedTimelineClip.SourceData is TimelineAudioClip audioClip)
                        {
                            SelectedAudioClip = audioClip;
                        }
                        break;
                }
            }
            UpdateEditorPanelVisibility();
        }
        private System.Windows.Rect GetReferenceVideoFrameRect()
        {
            double cw = PlayerAdornerDecorator.ActualWidth;
            double ch = PlayerAdornerDecorator.ActualHeight;
            if (cw <= 0 || ch <= 0 || CurrentProject == null
                || CurrentProject.ProjectReferenceVideoWidth <= 0
                || CurrentProject.ProjectReferenceVideoHeight <= 0) return System.Windows.Rect.Empty;

            double videoAspect = (double)CurrentProject.ProjectReferenceVideoWidth / CurrentProject.ProjectReferenceVideoHeight;
            double containerAspect = cw / ch;

            double w, h, x, y;
            if (containerAspect > videoAspect) { h = ch; w = h * videoAspect; x = (cw - w) / 2; y = 0; }
            else { w = cw; h = w / videoAspect; x = 0; y = (ch - h) / 2; }
            return new System.Windows.Rect(x, y, w, h);
        }

        private bool IsPointInsideAnyImageOverlay(System.Windows.Point pInOverlayCanvas)
        {
            foreach (var kv in _activeImageOverlays)
            {
                if (kv.Value is FrameworkElement fe)
                {
                    var hit = SubtitleRenderCanvas.InputHitTest(pInOverlayCanvas) as DependencyObject;
                    if (hit != null && IsDescendantOf(hit, fe)) return true;
                }
            }
            return false;
        }

        #region Context Menu Logic
        private void CreateSubtitlesFromClipsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _clipsForBatchSubtitle = TimelineClips
                .Where(c => c.IsSelected && c.ClipType == TimelineClipType.Video)
                .OrderBy(c => c.StartTime)
                .ToList();

            if (!_clipsForBatchSubtitle.Any())
            {
                CustomMessageBox.Show("Vui lòng chọn ít nhất một clip video trên timeline.", "Chưa chọn clip", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SubtitleTab.IsChecked = true;
            ModeOcrRadio.IsChecked = true;
            IsBatchSubtitleMode = true;
        }

        private async void StartBatchSubtitleCreationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckGoogleAccountsAndShowGuideAsync()) return;
            var clipsToProcess = _clipsForBatchSubtitle;

            if (clipsToProcess == null || !clipsToProcess.Any())
            {
                CustomMessageBox.Show("Lỗi: Không tìm thấy danh sách video cần xử lý.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBatchSubtitleMode = false;
                return;
            }
            var (usageSuccess, usageStatus, usageMessage) = await ApiService.GetUsageStatusAsync();
            if (!usageSuccess)
            {
                CustomMessageBox.Show($"Không thể kiểm tra giới hạn sử dụng video của bạn. Lỗi: {usageMessage}", "Lỗi Kết Nối", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBatchSubtitleMode = false;
                return;
            }
            if (clipsToProcess.Count > usageStatus.RemainingVideosToday)
            {
                CustomMessageBox.Show($"Bạn muốn xử lý {clipsToProcess.Count} video, nhưng tài khoản của bạn chỉ còn lại {usageStatus.RemainingVideosToday} lượt trong ngày.", "Không Đủ Lượt Xử Lý", MessageBoxButton.OK, MessageBoxImage.Warning);
                IsBatchSubtitleMode = false;
                return;
            }
            if (!await EnsureVsfIsAvailableAsync()) return;
            TranslateTab.IsChecked = true;
            _masterCts = new CancellationTokenSource();
            var token = _masterCts.Token;
            UpdateUiForProcessing(true);
            var allNewSrtLines = new List<SrtSubtitleLine>();
            int lastSrtIndex = 0;
            var clipsToRemove = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle || c.ClipType == TimelineClipType.Text).ToList();
            foreach (var clip in clipsToRemove) { TimelineClips.Remove(clip); }
            _currentProject.Subtitles.Clear();
            _currentProject.TextClips.Clear();
            SrtSubtitleLinesView.Clear();

            if (string.IsNullOrWhiteSpace(_currentProjectFilePath))
            {
                SaveProjectCurrent();
                _currentProjectFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects", $"{_currentProject.ProjectName}.json");
            }

            string projectDirectory = Path.GetDirectoryName(_currentProjectFilePath);
            string vsfBaseOutputForProject = Path.Combine(projectDirectory, "Output");
            Directory.CreateDirectory(vsfBaseOutputForProject);

            try
            {
                foreach (var clipVM in clipsToProcess)
                {
                    if (token.IsCancellationRequested) break;
                    var (canProcessThisVideo, processMessage) = await ApiService.TryStartProcessingAsync();
                    if (!canProcessThisVideo)
                    {
                        throw new Exception($"Đã hết lượt xử lý video giữa chừng. Server báo: {processMessage}");
                    }
                    var mediaAsset = clipVM.SourceData as MediaAsset;
                    if (mediaAsset == null) continue;

                    string videoFileName = Path.GetFileNameWithoutExtension(mediaAsset.FilePath);
                    string uniqueVsfOutputDirForVideo = Path.Combine(vsfBaseOutputForProject, videoFileName);
                    if (Directory.Exists(uniqueVsfOutputDirForVideo))
                    {
                        try
                        {
                            DirectoryInfo di = new DirectoryInfo(uniqueVsfOutputDirForVideo);
                            foreach (FileInfo file in di.GetFiles())
                            {
                                file.Delete();
                            }
                            foreach (DirectoryInfo dir in di.GetDirectories())
                            {
                                dir.Delete(true);
                            }
                        }
                        catch (Exception ex) { }
                    }
                    else
                    {
                        Directory.CreateDirectory(uniqueVsfOutputDirForVideo);
                    }
                    var vsfParams = GetVsfParametersFromUi();
                    vsfParams.VideoPath = mediaAsset.FilePath;
                    vsfParams.SpecificOutputDirectory = uniqueVsfOutputDirForVideo;
                    vsfParams.ClearDirectories = true;

                    if (mediaAsset.TrimStartOffset > TimeSpan.Zero || mediaAsset.TrimEndOffset > TimeSpan.Zero)
                    {
                        vsfParams.StartTime = mediaAsset.TrimStartOffset.ToString(@"hh\:mm\:ss\:fff");
                        var effectiveDurationInSource = mediaAsset.Duration - mediaAsset.TrimStartOffset - mediaAsset.TrimEndOffset;
                        vsfParams.EndTime = (mediaAsset.TrimStartOffset + effectiveDurationInSource).ToString(@"hh\:mm\:ss\:fff");
                    }
                    else
                    {
                        vsfParams.StartTime = null;
                        vsfParams.EndTime = null;
                    }
                    var (vsfSuccess, imagesDirPath) = await _vsfService.RunVSFProcessAsync(vsfParams, token);
                    if (!vsfSuccess) throw new Exception($"Tìm Sub cho video '{videoFileName}' thất bại. Dừng quá trình.");
                    if (Directory.Exists(imagesDirPath))
                    {
                        var imageFiles = Directory.GetFiles(imagesDirPath, "*.*", SearchOption.TopDirectoryOnly)
                                                  .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(f => f, new NaturalStringComparer()).ToList();
                        if (imageFiles.Any())
                        {
                            var finalOffset = clipVM.StartTime;
                            var newLines = await PrepareSrtViewFromImages(imageFiles, "", token, -2, finalOffset, lastSrtIndex + 1);
                            allNewSrtLines.AddRange(newLines);
                            lastSrtIndex = allNewSrtLines.Any() ? allNewSrtLines.Max(l => l.Index) : 0;
                        }
                    }
                }

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                if (!allNewSrtLines.Any())
                {
                    UpdateUiForProcessing(false);
                    return;
                }
                foreach (var line in allNewSrtLines)
                {
                    SrtSubtitleLinesView.Add(line);
                }

                Action<int, string> ocrProgressAction = (percent, message) => { };
                bool ocrSuccess = await StartOcrProcessAsync(token, allNewSrtLines, ocrProgressAction);
                if (!ocrSuccess && !token.IsCancellationRequested) throw new Exception("Quá trình OCR gặp lỗi.");
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                var validLinesFromOcr = allNewSrtLines
                    .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) &&
                                !l.OriginalText.StartsWith("[Lỗi") &&
                                l.OriginalText != "[Đang chờ OCR...]")
                    .ToList();

                var removedAfterMerge = SrtFileUtils.MergeDuplicates(new System.Collections.ObjectModel.ObservableCollection<SrtSubtitleLine>(validLinesFromOcr));
                var finalLines = validLinesFromOcr.Except(removedAfterMerge).ToList();
                SrtFileUtils.ReIndex(new System.Collections.ObjectModel.ObservableCollection<SrtSubtitleLine>(finalLines));
                SrtSubtitleLinesView.Clear();
                _currentProject.Subtitles.Clear();

                foreach (var line in finalLines)
                {
                    SrtSubtitleLinesView.Add(line);
                    _currentProject.Subtitles.Add(line);
                    TimelineClips.Add(new TimelineClipViewModel(line));
                }

                RecalculateTrackAssignments();
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                SaveProjectCurrent();
                IsSrtTranslating = true;
                var linesToTranslate = SrtSubtitleLinesView
                    .Where(l => !string.IsNullOrWhiteSpace(l.OriginalText) && !l.OriginalText.StartsWith("[Lỗi"))
                    .ToList();

                if (linesToTranslate.Any())
                {
                    await TranslateSrtLogic(token, linesToTranslate);
                }
                UpdateUiForProcessing(false);
                CustomMessageBox.Show("Hoàn tất xử lý phụ đề hàng loạt!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UpdateUiForProcessing(false);
                CustomMessageBox.Show($"Đã xảy ra lỗi trong quá trình xử lý hàng loạt:\n\n{ex.Message}", "Lỗi nghiêm trọng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSrtTranslating = false;
                IsBatchSubtitleMode = false;
                _clipsForBatchSubtitle = null;
                UpdateUiForProcessing(false);
                _masterCts?.Dispose();
                _masterCts = null;
                SaveProjectCurrent();
            }
        }
        private void CopyAttributes_Click(object sender, RoutedEventArgs e)
        {
            var selectedClip = TimelineClips.FirstOrDefault(c => c.IsSelected);
            if (selectedClip?.SourceData is SrtSubtitleLine srtLine)
            {
                _copiedStyleState = srtLine.Style.Clone();
            }
        }

        private void PasteAttributes_Click(object sender, RoutedEventArgs e)
        {
            if (_copiedStyleState == null) return;

            var selectedClips = TimelineClips.Where(c => c.IsSelected && (c.ClipType == TimelineClipType.Text || c.ClipType == TimelineClipType.Subtitle)).ToList();
            foreach (var clip in selectedClips)
            {
                if (clip.SourceData is SrtSubtitleLine srtLine)
                {
                    var originalX = srtLine.Style.X;
                    var originalY = srtLine.Style.Y;
                    var originalScaleX = srtLine.Style.ScaleX;
                    var originalScaleY = srtLine.Style.ScaleY;
                    var originalWidth = srtLine.Style.Width;

                    srtLine.Style = _copiedStyleState.Clone();
                    srtLine.Style.X = _copiedStyleState.X;
                    srtLine.Style.Y = _copiedStyleState.Y;
                    srtLine.Style.ScaleX = _copiedStyleState.ScaleX;
                    srtLine.Style.ScaleY = _copiedStyleState.ScaleY;
                    srtLine.Style.Width = _copiedStyleState.Width;
                    if (_activeVisuals.TryGetValue(srtLine, out var visual))
                    {
                        ApplyStyleToVisual(visual, srtLine.Style);
                    }
                }
            }
            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }
        private void BlurRegionButton_Click(object sender, RoutedEventArgs e)
        {
            IsBlurPopupOpen = true;
            BlurOptionsPopup.IsOpen = true;
        }

        private void BlurOptionsCancelButton_Click(object sender, RoutedEventArgs e)
        {
            DisableBlurFeature();
            IsBlurPopupOpen = false;
            BlurOptionsPopup.IsOpen = false;
        }
        private void EnsureBlurPreviewCreated()
        {
            if (_currentProject == null) return;
            if (_blurPreview != null && _blurPreview.Parent == SubtitleRenderCanvas)
            {
                ApplyZOrderForLayers();
                UpdateBlurVisualBrushViewbox();
                return;
            }
            if (_blurPreview == null)
            {
                var vb = new VisualBrush(FFMEPlayer)
                {
                    Stretch = Stretch.None,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new System.Windows.Rect(0, 0, 1, 1)
                };
                _blurPreview = new Border
                {
                    Name = "BlurPreview",
                    Uid = "BLUR_PREVIEW",
                    Background = vb,
                    Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = Math.Max(0.0, Math.Min(100.0, _currentProject.BlurPreviewRadius))
                    },
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Width = Math.Max(200, SubtitleRenderCanvas.ActualWidth * 0.6),
                    Height = Math.Max(80, SubtitleRenderCanvas.ActualHeight * 0.18),
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    IsHitTestVisible = true,
                    Focusable = true
                };
                _blurPreview.LayoutUpdated -= BlurPreview_LayoutUpdated;
                _blurPreview.LayoutUpdated += BlurPreview_LayoutUpdated;
                _blurPreview.MouseLeftButtonDown += BlurPreview_MouseLeftButtonDown;
                _blurPreview.DataContext = new MediaAsset
                {
                    Type = AssetType.Blur
                };
                Canvas.SetLeft(_blurPreview, (SubtitleRenderCanvas.ActualWidth - _blurPreview.Width) / 2);
                Canvas.SetTop(_blurPreview, (SubtitleRenderCanvas.ActualHeight - _blurPreview.Height) / 2);
                var layer = AdornerLayer.GetAdornerLayer(PlayerAdornerDecorator);
                _blurAdorner = new SubtitleAdorner(_blurPreview, this, rotationEnabled: false);
                layer.Add(_blurAdorner);
                _blurAdorner.DragDelta += (dx, dy) =>
                {
                    double left = Canvas.GetLeft(_blurPreview);
                    double top = Canvas.GetTop(_blurPreview);
                    double newLeft = left + dx;
                    double newTop = top + dy;
                    newLeft = Math.Clamp(newLeft, 0, Math.Max(0, SubtitleRenderCanvas.ActualWidth - _blurPreview.ActualWidth));
                    newTop = Math.Clamp(newTop, 0, Math.Max(0, SubtitleRenderCanvas.ActualHeight - _blurPreview.ActualHeight));
                    Canvas.SetLeft(_blurPreview, newLeft);
                    Canvas.SetTop(_blurPreview, newTop);
                    UpdateBlurVisualBrushViewbox();
                };
                _blurAdorner.Resized += (s, e) =>
                {
                    double left = Canvas.GetLeft(_blurPreview);
                    double width = _blurPreview.Width;

                    if (e.Direction == ResizeDirection.Left)
                    {
                        double newLeft = left + e.HorizontalChange;
                        double deltaW = left - newLeft;
                        double newW = width + deltaW;
                        if (newLeft < 0)
                        {
                            deltaW += newLeft;
                            newLeft = 0;
                            newW = width + deltaW;
                        }
                        newW = Math.Max(20, Math.Min(newW, SubtitleRenderCanvas.ActualWidth - newLeft));

                        _blurPreview.Width = newW;
                        Canvas.SetLeft(_blurPreview, newLeft);
                    }
                    else
                    {
                        double newW = width + e.HorizontalChange;
                        double maxW = Math.Max(20, SubtitleRenderCanvas.ActualWidth - left);
                        _blurPreview.Width = Math.Max(20, Math.Min(newW, maxW));
                    }

                    UpdateBlurVisualBrushViewbox();
                };
                _blurAdorner.UniformScaleDelta += scaleDelta =>
                {
                    double cx = Canvas.GetLeft(_blurPreview) + _blurPreview.Width / 2.0;
                    double cy = Canvas.GetTop(_blurPreview) + _blurPreview.Height / 2.0;

                    double newW = _blurPreview.Width * (1.0 + scaleDelta);
                    double newH = _blurPreview.Height * (1.0 + scaleDelta);
                    newW = Math.Clamp(newW, 20, SubtitleRenderCanvas.ActualWidth);
                    newH = Math.Clamp(newH, 20, SubtitleRenderCanvas.ActualHeight);
                    double newLeft = cx - newW / 2.0;
                    double newTop = cy - newH / 2.0;
                    newLeft = Math.Clamp(newLeft, 0, Math.Max(0, SubtitleRenderCanvas.ActualWidth - newW));
                    newTop = Math.Clamp(newTop, 0, Math.Max(0, SubtitleRenderCanvas.ActualHeight - newH));
                    _blurPreview.Width = newW;
                    _blurPreview.Height = newH;
                    Canvas.SetLeft(_blurPreview, newLeft);
                    Canvas.SetTop(_blurPreview, newTop);

                    UpdateBlurVisualBrushViewbox();
                };
                _blurAdorner.RotationChanged += angle =>
                {
                    if (_blurPreview.RenderTransform is RotateTransform rt)
                    {
                        rt.Angle = angle;
                        UpdateBlurVisualBrushViewbox();
                    }
                };
                _blurAdorner.DragCompleted += () =>
                {
                    SaveBlurPreviewToProjectState();
                    SaveProjectCurrent();
                };
                _blurPreview.SizeChanged += (_, __) => UpdateBlurVisualBrushViewbox();
                _blurPreview.LayoutUpdated += (_, __) => UpdateBlurVisualBrushViewbox();
                FFMEPlayer.SizeChanged += (_, __) => UpdateBlurVisualBrushViewbox();
                SubtitleRenderCanvas.SizeChanged += (_, __) =>
                {
                    UpdateBlurVisualBrushViewbox();
                    RestoreBlurPreviewFromProjectState();
                };
            }
            if (_blurPreview.Parent != SubtitleRenderCanvas)
            {
                SubtitleRenderCanvas.Children.Add(_blurPreview);
            }
            Panel.SetZIndex(_blurPreview, Z_ORDER_BLUR);
            ApplyZOrderForLayers();
            UpdateBlurVisualBrushViewbox();
            RestoreBlurPreviewFromProjectState();

        }
        private void BlurPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                EnsureBlurPreviewCreated();

                RemoveVideoAdorner();
                RemoveImageAdorner();
                RemoveSubtitleAdorner();

                if (_adornerLayer == null)
                    _adornerLayer = AdornerLayer.GetAdornerLayer(SubtitleRenderCanvas);

                if (_adornerLayer != null && _blurAdorner != null)
                {
                    var existing = _adornerLayer.GetAdorners(_blurPreview);
                    if (existing == null || !existing.Contains(_blurAdorner))
                        _adornerLayer.Add(_blurAdorner);

                    _blurAdorner.Visibility = Visibility.Visible;
                    _blurAdorner.InvalidateArrange();
                }
                e.Handled = true;
            }
            catch { /*  an toàn để không crash UI */ }
        }


        private void BlurOptionsApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject == null) return;

            _currentProject.BlurMode = BlurBySubtitleRadio.IsChecked == true
                ? ProjectState.BlurApplyMode.PerSubtitle
                : ProjectState.BlurApplyMode.AllTime;

            EnsureBlurPreviewCreated();
            SaveBlurPreviewToProjectState();
            ApplyZOrderForLayers();

            SaveProjectCurrent();
            IsBlurPopupOpen = false;
            BlurOptionsPopup.IsOpen = false;
        }
        private void UpdateBlurVisualBrushViewbox()
        {
            if (_blurPreview == null) return;
            if (!(_blurPreview.Background is VisualBrush vb)) return;
            if (FFMEPlayer.ActualWidth <= 0 || FFMEPlayer.ActualHeight <= 0) return;
            GeneralTransform t = _blurPreview.TransformToVisual(FFMEPlayer);
            System.Windows.Rect r = t.TransformBounds(new System.Windows.Rect(0, 0, _blurPreview.ActualWidth, _blurPreview.ActualHeight));
            double nx = r.X / FFMEPlayer.ActualWidth;
            double ny = r.Y / FFMEPlayer.ActualHeight;
            double nw = Math.Max(0.0001, r.Width / FFMEPlayer.ActualWidth);
            double nh = Math.Max(0.0001, r.Height / FFMEPlayer.ActualHeight);
            vb.Viewbox = new System.Windows.Rect(nx, ny, nw, nh);
            double sx = r.Width / Math.Max(0.0001, _blurPreview.ActualWidth);
            double sy = r.Height / Math.Max(0.0001, _blurPreview.ActualHeight);
            double scale = Math.Max(0.001, Math.Min(Math.Abs(sx), Math.Abs(sy)));
            if (_blurPreview.Effect is System.Windows.Media.Effects.BlurEffect blur)
            {
                double baseRadius = Math.Max(0.0, Math.Min(100.0, _currentProject?.BlurPreviewRadius ?? 50.0));
                double compensated = baseRadius / scale;
                if (compensated > 100.0) compensated = 100.0;
                if (compensated < 0.0) compensated = 0.0;
                blur.Radius = compensated;
            }
        }
        private void BlurPreview_LayoutUpdated(object sender, EventArgs e)
        {
            try
            {
                UpdateBlurVisualBrushViewbox();
            }
            catch { /* an toàn UI */ }
        }

        private void SaveBlurPreviewToProjectState()
        {
            if (_currentProject == null || _blurPreview == null || FFMEPlayer.ActualWidth <= 0 || FFMEPlayer.ActualHeight <= 0)
                return;

            GeneralTransform t = _blurPreview.TransformToVisual(FFMEPlayer);
            System.Windows.Rect r = t.TransformBounds(new System.Windows.Rect(0, 0, _blurPreview.ActualWidth, _blurPreview.ActualHeight));

            _currentProject.BlurRectNormalized = new System.Windows.Rect(
                r.X / FFMEPlayer.ActualWidth,
                r.Y / FFMEPlayer.ActualHeight,
                Math.Max(0.0001, r.Width / FFMEPlayer.ActualWidth),
                Math.Max(0.0001, r.Height / FFMEPlayer.ActualHeight)
            );

        }
        private void RemoveBlurAdorner()
        {
            if (_blurAdorner != null && _adornerLayer != null && _blurPreview != null)
            {
                try { _blurAdorner.ReleaseMouseCapture(); } catch { }
                _adornerLayer.Remove(_blurAdorner);
                _blurAdorner.Visibility = Visibility.Collapsed;
                _blurAdorner.InvalidateArrange();
            }
        }
        private const int Z_ORDER_BLUR = -1000;
        private const int Z_ORDER_IMAGE = 100;
        private const int Z_ORDER_SUBTITLE = 200;

        private void ApplyZOrderForLayers()
        {
            if (SubtitleRenderCanvas == null) return;

            foreach (UIElement child in SubtitleRenderCanvas.Children)
            {
                if (child == null) continue;
                if (ReferenceEquals(child, _blurPreview))
                {
                    Panel.SetZIndex(child, Z_ORDER_BLUR);
                    continue;
                }
                if (child is FrameworkElement fe)
                {
                    var dc = fe.DataContext;
                    if (dc is SrtSubtitleLine)
                    {
                        Panel.SetZIndex(fe, Z_ORDER_SUBTITLE);
                        continue;
                    }
                    if (dc is MediaAsset asset && asset.Type == AssetType.Image)
                    {
                        Panel.SetZIndex(fe, Z_ORDER_IMAGE);
                        continue;
                    }
                }
                Panel.SetZIndex(child, 0);
            }
        }
        private void RestoreBlurPreviewFromProjectState()
        {
            if (_currentProject?.BlurRectNormalized is not System.Windows.Rect nr) return;
            if (_blurPreview == null) return;
            double px = nr.X * SubtitleRenderCanvas.ActualWidth;
            double py = nr.Y * SubtitleRenderCanvas.ActualHeight;
            double pw = nr.Width * SubtitleRenderCanvas.ActualWidth;
            double ph = nr.Height * SubtitleRenderCanvas.ActualHeight;
            _blurPreview.Width = Math.Max(1, pw);
            _blurPreview.Height = Math.Max(1, ph);
            Canvas.SetLeft(_blurPreview, px);
            Canvas.SetTop(_blurPreview, py);
            if (_blurPreview.Tag is double savedAngle && _blurPreview.RenderTransform is RotateTransform rt)
            {
                rt.Angle = savedAngle;
            }

            UpdateBlurVisualBrushViewbox();
        }

        private async void CreateSubtitles_Click(object sender, RoutedEventArgs e)
        {
            var selectedClips = TimelineClips.Where(c => c.IsSelected).ToList();
            var videoClip = selectedClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);

            if (videoClip != null && videoClip.SourceData is MediaAsset asset)
            {
                // === SAFETY GUARD: đảm bảo player đã sẵn sàng trước khi chuyển sang SubtitleTab ===
                if (!(FFMEPlayer != null && FFMEPlayer.IsInitialized) || _activeVideoClip == null)
                {
                    await EnsurePlayerReadyOnFirstVideoClipAsync(videoClip);
                }

                // Vào flow tạo phụ đề như trước (hàm này sẽ set SubtitleTab.IsChecked = true và chuẩn bị player)
                await HandleCreateSubtitlesForVideoAsync(asset);
            }
            else
            {
                CustomMessageBox.Show(
                    "Vui lòng chọn một clip video trên timeline để tạo phụ đề.",
                    "Thông báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CreateCompoundClip_Click(object sender, RoutedEventArgs e)
        {
            CustomMessageBox.Show("Chức năng 'Tạo clip ghép' đang được phát triển.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UndoCompoundClip_Click(object sender, RoutedEventArgs e)
        {
            CustomMessageBox.Show("Chức năng 'Hoàn tác clip ghép' đang được phát triển.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        void PositionMarkerThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isUserDraggingPlayhead = true;
            _playheadDragStartAbsX = Mouse.GetPosition(TracksContainerGrid).X;
            _playheadDragStartTime = _playhead;

            if (_isTimelinePlaying)
            {
                PauseTimelinePlayback();
            }
        }

        void PositionMarkerThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_isUserDraggingPlayhead || _pixelsPerSecond <= 0) return;
            double currentMouseAbsX = Mouse.GetPosition(TracksContainerGrid).X;
            double deltaX = currentMouseAbsX - _playheadDragStartAbsX;
            double startAbsX = _playheadDragStartTime.TotalSeconds * _pixelsPerSecond;
            double targetAbsX = startAbsX + deltaX;
            double maxAbsX = _totalTimelineDuration.TotalSeconds * _pixelsPerSecond;
            if (targetAbsX < 0) targetAbsX = 0;
            if (targetAbsX > maxAbsX) targetAbsX = maxAbsX;

            TimeSpan newTime = TimeSpan.FromSeconds(targetAbsX / _pixelsPerSecond);

            _playhead = newTime;
            UpdatePlaybackUI(_playhead);
            UpdateSubtitleForCurrentTime(_playhead);
            PreviewPlayerAtTime(_playhead);
        }

        void PositionMarkerThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!_isUserDraggingPlayhead) return;

            _isUserDraggingPlayhead = false;
            (sender as Thumb)?.ReleaseMouseCapture();

            _playheadDragStartAbsX = 0;
            _playheadDragStartTime = TimeSpan.Zero;

            _ = SeekTimeline(_playhead);
        }
        private void HandleVideoClipModification(TimelineClipViewModel modifiedClip, TimeSpan oldEndTime)
        {
            if (modifiedClip == null) return;
            modifiedClip.RefreshPropertiesFromSource();
            var newEndTime = modifiedClip.EndTime;
            var delta = newEndTime - oldEndTime;
            AdjustSubsequentClipTimings(modifiedClip, delta, oldEndTime);
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
        }
        private void AdjustSubsequentClipTimings(TimelineClipViewModel modifiedClip, TimeSpan timeDelta, TimeSpan modificationPoint)
        {
            if (Math.Abs(timeDelta.TotalMilliseconds) < 1)
            {
                return;
            }
            var subsequentClips = TimelineClips
                .Where(c => c != modifiedClip && c.StartTime >= modificationPoint)
                .ToList();

            if (!subsequentClips.Any())
            {
                return;
            }
            foreach (var clip in subsequentClips)
            {
                var oldStartTime = clip.StartTime;
                var newStartTime = oldStartTime + timeDelta;
                if (newStartTime < TimeSpan.Zero)
                {
                    newStartTime = TimeSpan.Zero;
                }
                clip.StartTime = newStartTime;
            }
        }
        private double ComputePlayerScaleFactor()
        {
            const double REF_H = DEFAULT_REFERENCE_HEIGHT;
            double playerH = SubtitleRenderCanvas?.ActualHeight > 1 ? SubtitleRenderCanvas.ActualHeight : videoGrid?.ActualHeight ?? REF_H;
            if (playerH <= 1) playerH = REF_H;
            return playerH / REF_H;
        }
        private void EnsureInitialTemplateWidthFitsVideo()
        {
        }
        private void UpdateOverlaysForCurrentTime(TimeSpan timelineTime)
        {
            var imageClipsToShow = TimelineClips
                .Where(c => c.ClipType == TimelineClipType.Image && timelineTime >= c.StartTime && timelineTime < c.EndTime)
                .Select(c => c.SourceData as MediaAsset)
                .Where(asset => asset != null)
                .ToList();

            var visualsToRemove = _activeImageOverlays.Keys
                .Where(asset => !imageClipsToShow.Contains(asset))
                .ToList();

            foreach (var asset in visualsToRemove)
            {
                var visual = _activeImageOverlays[asset];
                SubtitleRenderCanvas.Children.Remove(visual);
                _activeImageOverlays.Remove(asset);
            }

            foreach (var asset in imageClipsToShow)
            {
                if (!_activeImageOverlays.ContainsKey(asset))
                {
                    DisplayImageOnPlayer(asset);
                }
            }
        }

        private void DisplayImageOnPlayer(MediaAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.FilePath) || !File.Exists(asset.FilePath))
            {
                return;
            }

            if (_activeImageOverlays.ContainsKey(asset))
            {
                return;
            }

            try
            {
                var imageControl = new System.Windows.Controls.Image
                {
                    Stretch = Stretch.Fill,
                    DataContext = asset
                };
                imageControl.MouseLeftButtonDown += ImageOverlay_MouseLeftButtonDown;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(asset.FilePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                imageControl.Source = bitmap;

                SubtitleRenderCanvas.Children.Add(imageControl);
                Panel.SetZIndex(imageControl, Z_ORDER_IMAGE);
                ApplyZOrderForLayers();
                _activeImageOverlays[asset] = imageControl;

                ApplyTransformToImageVisual(imageControl, asset);
            }
            catch (Exception ex) { }
        }
        private void ImageOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MediaAsset asset)
            {
                e.Handled = true;
                var clip = TimelineClips
                    .FirstOrDefault(c => c.SourceData == asset
                                         && _playhead >= c.StartTime && _playhead < c.EndTime);
                if (clip != null)
                {
                    SwitchToClip(clip);
                }
                else
                {
                    ClearAllAdorners();
                    AddImageAdorner(asset);
                    UpdateEditorPanelVisibility();
                }
            }
        }
        private void ApplyTransformToImageVisual(FrameworkElement overlayVisual, MediaAsset asset)
        {
            if (overlayVisual == null || asset == null) return;
            double refW = Math.Max(1, this.CurrentProject.ProjectReferenceVideoWidth);
            double refH = Math.Max(1, this.CurrentProject.ProjectReferenceVideoHeight);
            double refAspect = refW / refH;
            double canvasW = Math.Max(1, SubtitleRenderCanvas.ActualWidth);
            double canvasH = Math.Max(1, SubtitleRenderCanvas.ActualHeight);
            double toCanvas = Math.Min(canvasW / refW, canvasH / refH);
            double frameW = refW * toCanvas;
            double frameH = refH * toCanvas;
            double frameLeft = (canvasW - frameW) / 2;
            double frameTop = (canvasH - frameH) / 2;
            double imgW = asset.Width > 0 ? asset.Width : refW * 0.5;
            double imgH = asset.Height > 0 ? asset.Height : refH * 0.5;
            double imgAspect = imgW / Math.Max(1, imgH);

            double initW_ref, initH_ref;
            if (imgAspect >= refAspect)
            {
                initW_ref = refW;
                initH_ref = refW / imgAspect;
            }
            else
            {
                initH_ref = refH;
                initW_ref = refH * imgAspect;
            }
            double scaledInitW = initW_ref * toCanvas;
            double scaledInitH = initH_ref * toCanvas;
            double sx = Math.Max(asset.ScaleX, 0.0001);
            double sy = Math.Max(asset.ScaleY, 0.0001);
            double finalW = scaledInitW * sx;
            double finalH = scaledInitH * sy;
            overlayVisual.Width = finalW;
            overlayVisual.Height = finalH;
            double centerX = frameLeft + asset.PositionX * frameW;
            double centerY = frameTop + asset.PositionY * frameH;
            Canvas.SetLeft(overlayVisual, centerX - finalW / 2.0);
            Canvas.SetTop(overlayVisual, centerY - finalH / 2.0);
            overlayVisual.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            overlayVisual.RenderTransform = new System.Windows.Media.RotateTransform(asset.Rotation);
        }

        private void VideoContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            e.Handled = true;

            double oldScale = _playerPanelScale;
            double newScale;

            if (e.Delta > 0)
            {
                newScale = oldScale * PLAYER_ZOOM_SPEED;
            }
            else
            {
                newScale = oldScale / PLAYER_ZOOM_SPEED;
            }
            _playerPanelScale = Math.Clamp(newScale, PLAYER_ZOOM_MIN, 1.0);
            videoGridScaleTransform.ScaleX = _playerPanelScale;
            videoGridScaleTransform.ScaleY = _playerPanelScale;
            videoGridTranslateTransform.X = 0;
            videoGridTranslateTransform.Y = 0;
            _videoAdorner?.InvalidateArrange();
        }

        private void AddVideoAdorner(MediaAsset asset)
        {
            if (_suppressAdornerForHardSub) return;
            RemoveVideoAdorner();
            RemoveImageAdorner();
            RemoveSubtitleAdorner();
            if (asset == null) return;
            var adornerLayer = AdornerLayer.GetAdornerLayer(PlayerAdornerDecorator);
            if (adornerLayer != null)
            {
                _videoAdorner = new VideoAdorner(PlayerAdornerDecorator, videoGrid, this);

                _activeTransformingVideoAsset = asset;
                _videoAdorner.DataContext = asset;
                _videoAdorner.DragDelta += VideoAdorner_DragDelta;
                _videoAdorner.ScaleChanged += VideoAdorner_ScaleChanged;
                _videoAdorner.DragCompleted += VideoAdorner_DragCompleted;

                adornerLayer.Add(_videoAdorner);
                UpdateVideoTransform(asset);
            }
        }
        private void ActivateVideoAdornerIfNeeded()
        {
            if (_suppressAdornerForHardSub) return;

            if (_videoAdorner == null
                && _selectedTimelineClip?.SourceData is MediaAsset asset
                && asset.Type == AssetType.Video)
            {
                AddVideoAdorner(asset);
            }
        }
        private void ActivateImageAdornerIfNeeded()
        {
            if (_imageAdorner == null
                && _selectedTimelineClip?.SourceData is MediaAsset asset
                && asset.Type == AssetType.Image)
            {
                AddImageAdorner(asset);
            }
        }
        private void TextBox_LostFocus_TriggerSmartLogic(object sender, RoutedEventArgs e)
        {
            if (_isModifyingVideoClip && _selectedTimelineClip != null)
            {
                HandleVideoClipModification(_selectedTimelineClip, _preModificationEndTime);
            }
            _isModifyingVideoClip = false;
        }
        private void TransformControl_InteractionStarted(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip?.SourceData is MediaAsset asset)
            {
                if (asset.Type == AssetType.Video)
                {
                    RemoveImageAdorner();
                    ActivateVideoAdornerIfNeeded();
                }
                else if (asset.Type == AssetType.Image)
                {
                    RemoveVideoAdorner();
                    ActivateImageAdornerIfNeeded();
                }

                _isModifyingVideoClip = true;
                _preModificationEndTime = _selectedTimelineClip.EndTime;
            }
        }
        private void TransformControl_InteractionCompleted(object sender, MouseButtonEventArgs e)
        {
            if (_isModifyingVideoClip && _selectedTimelineClip != null)
            {
                HandleVideoClipModification(_selectedTimelineClip, _preModificationEndTime);
            }
            _isModifyingVideoClip = false;
        }
        private void RemoveVideoAdorner()
        {
            if (_videoAdorner != null)
            {
                try { _videoAdorner.ReleaseMouseCapture(); } catch { }
                _videoAdorner.DragDelta -= VideoAdorner_DragDelta;
                _videoAdorner.ScaleChanged -= VideoAdorner_ScaleChanged;
                _videoAdorner.DragCompleted -= VideoAdorner_DragCompleted;

                var layer = AdornerLayer.GetAdornerLayer(PlayerAdornerDecorator);
                layer?.Remove(_videoAdorner);

                _videoAdorner = null;
                _activeTransformingVideoAsset = null;
            }
        }
        private void VideoAdorner_DragDelta(double horizontalChange, double verticalChange)
        {
            if (_activeTransformingVideoAsset == null || videoGrid.ActualWidth == 0 || videoGrid.ActualHeight == 0) return;
            double currentScale = _activeTransformingVideoAsset.Scale;
            if (currentScale <= 0.001) return;
            double dx = horizontalChange / (videoGrid.ActualWidth * currentScale);
            double dy = verticalChange / (videoGrid.ActualHeight * currentScale);
            _activeTransformingVideoAsset.PositionX += dx;
            _activeTransformingVideoAsset.PositionY += dy;
            UpdateVideoTransform();
        }
        private void VideoAdorner_ScaleChanged(double newScale)
        {
            if (_activeTransformingVideoAsset == null) return;
            _activeTransformingVideoAsset.Scale = newScale;
            UpdateVideoTransform(_activeTransformingVideoAsset);
        }
        private void VideoAdorner_DragCompleted()
        {
            if (_activeTransformingVideoAsset == null) return;

            _undoRedoService.AddState(CaptureEditorSnapshot());
            SaveProjectCurrent();
        }
        private void UpdateVideoTransform()
        {
            var asset = _activeTransformingVideoAsset ?? (_selectedTimelineClip?.SourceData as MediaAsset);
            UpdateVideoTransform(asset);
        }
        private void UpdateVideoTransform(MediaAsset assetToUpdate, [CallerMemberName] string caller = null)
        {
            var transformTargetPlayer = FFMEPlayer;
            var currentAssetBeingPlayed = _activeVideoClip?.SourceData as MediaAsset;
            if (assetToUpdate == null)
            {
                if (transformTargetPlayer.RenderTransform is TransformGroup tg_reset)
                {
                    if (tg_reset.Children.Count > 0 && tg_reset.Children[0] is ScaleTransform st) { st.ScaleX = 1; st.ScaleY = 1; }
                    if (tg_reset.Children.Count > 1 && tg_reset.Children[1] is TranslateTransform tt) { tt.X = 0; tt.Y = 0; }
                }
                _videoAdorner?.InvalidateVisual();
                _videoAdorner?.InvalidateArrange();
                return;
            }
            if (assetToUpdate != currentAssetBeingPlayed)
            {
                return;
            }

            double scaleX = assetToUpdate.ScaleX;
            double scaleY = assetToUpdate.ScaleY;
            double posX = assetToUpdate.PositionX;
            double posY = assetToUpdate.PositionY;

            if (transformTargetPlayer.RenderTransform is TransformGroup transformGroup)
            {
                if (transformGroup.Children.Count > 0 && transformGroup.Children[0] is ScaleTransform scaleTransform)
                {
                    scaleTransform.ScaleX = scaleX;
                    scaleTransform.ScaleY = scaleY;
                }
                if (transformGroup.Children.Count > 1 && transformGroup.Children[1] is TranslateTransform translateTransform)
                {
                    translateTransform.X = (posX - 0.5) * videoGrid.ActualWidth;
                    translateTransform.Y = (posY - 0.5) * videoGrid.ActualHeight;
                }
            }
            _videoAdorner?.InvalidateVisual();
            _videoAdorner?.InvalidateArrange();
        }
        private async void SmartMergeButton_Click(object sender, RoutedEventArgs e)
        {
            if (SrtSubtitleLinesView == null || SrtSubtitleLinesView.Count < 2)
            {
                CustomMessageBox.Show("Không có đủ dòng phụ đề để thực hiện gộp.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool wasPlaying = _isTimelinePlaying;
            if (wasPlaying)
            {
                PauseTimelinePlayback();
            }
            List<SrtSubtitleLine> removedLines = SrtFileUtils.MergeDuplicates(SrtSubtitleLinesView);

            if (removedLines.Any())
            {
                var clipsToRemoveFromTimeline = new List<TimelineClipViewModel>();
                foreach (var removedLine in removedLines)
                {
                    _currentProject.Subtitles.Remove(removedLine);
                    var clipVM = TimelineClips.FirstOrDefault(c => c.SourceData == removedLine);
                    if (clipVM != null)
                    {
                        clipsToRemoveFromTimeline.Add(clipVM);
                    }
                }

                foreach (var clipVM in clipsToRemoveFromTimeline)
                {
                    TimelineClips.Remove(clipVM);
                }
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();

                _undoRedoService.AddState(CaptureEditorSnapshot());
                await SaveProjectCurrentAsync();
                CustomMessageBox.Show($"Đã gộp thành công {removedLines.Count} dòng phụ đề.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                CustomMessageBox.Show("Không tìm thấy dòng phụ đề nào liền kề và trùng lặp để gộp.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            if (wasPlaying)
            {
                StartTimelinePlayback();
            }
        }

        private void UpdatePlayerLayout()
        {
            if (CurrentProject != null && CurrentProject.ProjectReferenceVideoWidth > 0 && CurrentProject.ProjectReferenceVideoHeight > 0)
            {
                videoGrid.Width = CurrentProject.ProjectReferenceVideoWidth;
                videoGrid.Height = CurrentProject.ProjectReferenceVideoHeight;
            }
            else
            {
                videoGrid.Width = DEFAULT_REFERENCE_WIDTH;
                videoGrid.Height = DEFAULT_REFERENCE_HEIGHT;
            }
            overlayCanvas.Width = videoGrid.Width;
            overlayCanvas.Height = videoGrid.Height;
        }
        private sealed class SmartCutSegmentMap
        {
            public TimeSpan SourceStart { get; init; }
            public TimeSpan SourceEnd { get; init; }
            public TimeSpan FinalStart { get; init; }
            public TimeSpan FinalEnd { get; init; }
            public bool IsGap { get; init; }
            public double Scale { get; init; }
        }
        private List<SmartCutSegmentMap> _smartCutSegmentMaps;
        private (List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)> SubtitleTimeline,
         List<SmartCutSegmentMap> SegmentMaps,
         TimeSpan FinalDuration)
BuildSmartCutSegmentMapForStaticReview(
    List<SrtSubtitleLine> orderedVoicedLines,
    List<TimeSpan> targetDurations,
    double slowedFactor,
    TimeSpan sourceTotalDuration)
        {
            // Danh sách để xuất phụ đề cuối cùng: mỗi line sẽ có FinalStart / FinalEnd để overlay và để đặt TTS.
            var subtitleTimeline = new List<(SrtSubtitleLine Line, TimeSpan FinalStart, TimeSpan FinalEnd)>();

            // Bản đồ các đoạn video/gap từ source timeline sang final timeline.
            var maps = new List<SmartCutSegmentMap>();

            // Kiểm tra input cơ bản
            if (orderedVoicedLines == null || targetDurations == null || orderedVoicedLines.Count == 0)
                return (subtitleTimeline, maps, TimeSpan.Zero);

            if (orderedVoicedLines.Count != targetDurations.Count)
                return (subtitleTimeline, maps, TimeSpan.Zero);

            // prevSrcEnd: mốc kết thúc source clip (sau khi áp dụng slowedFactor) của line trước.
            TimeSpan prevSrcEnd = TimeSpan.Zero;

            // cursorFinal: vị trí đang build trên timeline sau cùng (final export timeline).
            TimeSpan cursorFinal = TimeSpan.Zero;

            for (int i = 0; i < orderedVoicedLines.Count; i++)
            {
                var line = orderedVoicedLines[i];
                var targetVoiceDuration = targetDurations[i]; // độ dài voice TTS (sau khi tăng/giảm/giữ speed TTS)

                // Tính lại start/end của đoạn video gốc tương ứng sub line này,
                // có tính tới speed của clip video chính (slowedFactor).
                var srcStart = TimeSpan.FromSeconds(
                    Math.Max(0.0, line.StartTime.TotalSeconds * slowedFactor));

                var srcEnd = TimeSpan.FromSeconds(
                    Math.Max(srcStart.TotalSeconds + 1e-6, line.EndTime.TotalSeconds * slowedFactor));

                // Nếu có một khoảng trống giữa line trước và line này trong source,
                // ta phải giữ nguyên khoảng trống đó như 1 segment IsGap = true (scale luôn 1.0).
                if (srcStart > prevSrcEnd)
                {
                    var gapLen = srcStart - prevSrcEnd;

                    maps.Add(new SmartCutSegmentMap
                    {
                        SourceStart = prevSrcEnd,
                        SourceEnd = srcStart,
                        FinalStart = cursorFinal,
                        FinalEnd = cursorFinal + gapLen,
                        IsGap = true,
                        Scale = 1.0
                    });

                    // Đưa con trỏ final timeline đi qua khoảng gap giống hệt (không tua nhanh / chậm).
                    cursorFinal += gapLen;
                }

                var srcLen = srcEnd - srcStart;
                if (srcLen < TimeSpan.FromMilliseconds(1))
                    srcLen = TimeSpan.FromMilliseconds(1);

                // *** QUY TẮC CỐT LÕI StaticReview ***
                // - KHÔNG BAO GIỜ CO VIDEO: nếu voice không dài hơn video => giữ nguyên video
                // - Chỉ GIÃN video khi voice dài hơn video
                var finalLen = srcLen;
                if (targetVoiceDuration > srcLen)
                {
                    // voice dài hơn => giãn video để khớp
                    finalLen = targetVoiceDuration;
                }
                // else: voice ngắn hoặc bằng => GIỮ NGUYÊN video (finalLen = srcLen)

                double segmentScale = 1.0;
                if (finalLen > srcLen)
                {
                    // setpts = PTS * scale => scale >= 1.0 để GIÃN (tuyệt đối không < 1)
                    segmentScale = finalLen.TotalSeconds / srcLen.TotalSeconds;
                    if (segmentScale < 1.0) segmentScale = 1.0; // an toàn, không bao giờ cho phép co
                }
                else
                {
                    // finalLen == srcLen => giữ nguyên
                    segmentScale = 1.0;
                }

                maps.Add(new SmartCutSegmentMap
                {
                    SourceStart = srcStart,
                    SourceEnd = srcEnd,
                    FinalStart = cursorFinal,
                    FinalEnd = cursorFinal + finalLen,
                    IsGap = false,
                    Scale = segmentScale
                });

                // Ghi lại khoảng thời gian final cho phụ đề line này để overlay/đặt TTS
                subtitleTimeline.Add((line, cursorFinal, cursorFinal + finalLen));

                // Cập nhật mốc
                cursorFinal += finalLen;
                prevSrcEnd = srcEnd;
            }

            // Xử lý phần đuôi nếu còn dư của source (giữ nguyên như gap, scale = 1)
            if (sourceTotalDuration > prevSrcEnd)
            {
                var tailLen = sourceTotalDuration - prevSrcEnd;
                maps.Add(new SmartCutSegmentMap
                {
                    SourceStart = prevSrcEnd,
                    SourceEnd = sourceTotalDuration,
                    FinalStart = cursorFinal,
                    FinalEnd = cursorFinal + tailLen,
                    IsGap = true,
                    Scale = 1.0
                });
                cursorFinal += tailLen;
            }

            return (subtitleTimeline, maps, cursorFinal);
        }

        private TimeSpan MapSourceTimeToSmartCut(TimeSpan sourceTime)
        {
            if (_smartCutSegmentMaps == null || _smartCutSegmentMaps.Count == 0)
                return sourceTime;

            foreach (var seg in _smartCutSegmentMaps)
            {
                if (sourceTime >= seg.SourceStart && sourceTime <= seg.SourceEnd)
                {
                    var rel = sourceTime - seg.SourceStart;
                    var add = seg.IsGap
                        ? rel
                        : TimeSpan.FromSeconds(rel.TotalSeconds * seg.Scale);
                    return seg.FinalStart + add;
                }
            }
            var last = _smartCutSegmentMaps.LastOrDefault();
            if (last != null && sourceTime > last.SourceEnd)
                return last.FinalEnd + (sourceTime - last.SourceEnd);

            return sourceTime;
        }
        private async Task RecomputeStaticReviewVirtualTimelineAsync()
        {
            var (voicedSrtLines, timingSegments) = await ResolveSmartCutSourcesAsync();
            if (voicedSrtLines == null || voicedSrtLines.Count == 0 || timingSegments == null || timingSegments.Count == 0)
                return;

            var targetDurations = timingSegments.Select(seg => seg.EffectiveDuration).ToList();

            var mainVideoClip = TimelineClips.FirstOrDefault(c => c.ClipType == TimelineClipType.Video);
            var slowedFactor = (mainVideoClip?.Speed > 0) ? mainVideoClip.Speed : 1.0;
            var sourceTotalSec = await ProbeDurationSecondsAsync(mainVideoClip?.FilePath);
            double trimStart = (mainVideoClip?.SourceData as MediaAsset)?.TrimStartOffset.TotalSeconds ?? 0.0;
            double trimEnd = (mainVideoClip?.SourceData as MediaAsset)?.TrimEndOffset.TotalSeconds ?? 0.0;
            var sourceTotalDuration = TimeSpan.FromSeconds(Math.Max(0, sourceTotalSec - trimStart - trimEnd));


            var (subtitleTimeline, segmentMaps, finalDuration) = BuildSmartCutSegmentMapForStaticReview(voicedSrtLines, targetDurations, slowedFactor, sourceTotalDuration);
            _smartCutSubtitleTimeline = subtitleTimeline;
            _smartCutSegmentMaps = segmentMaps;
            _actualContentDuration = finalDuration;
            _totalTimelineDuration = finalDuration;
            UpdateTimelineScaleAndRender();
        }
        private async Task<bool> CheckGoogleAccountsAndShowGuideAsync()
        {
            if (_currentOcrMode != OcrMode.GoogleCloud)
            {
                return true;
            }
            LoadGoogleAccounts();

            if (_googleAccounts.Any())
            {
                return true;
            }
            string message = "Chức năng Google Cloud OCR yêu cầu cấu hình tài khoản trong thư mục 'Oauth2.0'.\n\n" +
                             "Cấu trúc yêu cầu:\n" +
                             "1. Tạo thư mục 'Oauth2.0' cùng cấp file .exe.\n" +
                             "2. Bên trong, tạo thư mục cho mỗi tài khoản (ví dụ: 'Acc01', 'Acc02',...).\n" +
                             "3. Trong mỗi thư mục tài khoản, đặt:\n" +
                             "   - File 'credentials.json' của tài khoản đó.\n" +
                             "   - Một file rỗng không có đuôi, với tên là ID của thư mục Google Drive.\n\n" +
                             "Nhấn 'OK', một link ảnh hướng dẫn và trang Google Drive sẽ được mở trong trình duyệt của bạn.";

            var result = CustomMessageBox.Show(message, "Thiếu Cấu Hình Google Cloud OCR", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                return false;
            }

            string imageUrl = "https://drive.google.com/file/d/1zH6PKihbBgO9F0kIMat9k396Spzuirdr/view";
            string driveUrl = "https://drive.google.com/drive/my-drive";

            try
            {
                Process.Start(new ProcessStartInfo(driveUrl) { UseShellExecute = true });
                await Task.Delay(500);
                Process.Start(new ProcessStartInfo(imageUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể mở trình duyệt. Vui lòng truy cập thủ công:\n- Hướng dẫn: {imageUrl}\n- Drive: {driveUrl}\nLỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
        private void OcrProvider_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            if (OcrProviderGoogleCloudRadio.IsChecked == true)
            {
                _currentOcrMode = OcrMode.GoogleCloud;
            }
            else
            {
                _currentOcrMode = OcrMode.GeminiApi;
            }

        }

        private void AddToTimelineButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not MediaAsset asset)
            {
                return;
            }

            if (asset.Type == AssetType.Video)
            {
                var lastVideoClip = TimelineClips
                    .Where(c => c.ClipType == TimelineClipType.Video && c.TrackIndex == 0)
                    .OrderBy(c => c.StartTime)
                    .LastOrDefault();
                TimeSpan startTime = lastVideoClip != null ? lastVideoClip.EndTime : TimeSpan.Zero;
                bool isFirstVideoClip = lastVideoClip == null;
                if (isFirstVideoClip)
                {
                    UpdateProjectReferenceDimensions(asset);
                    UpdatePlayerLayout(); // dùng 1 chỗ để set videoGrid/overlay
                    if (asset.Duration.TotalSeconds > 0 && TimelineScrollViewer.ViewportWidth > 0)
                    {
                        double desiredVisibleDurationSeconds = asset.Duration.TotalSeconds / 0.5;
                        double newPixelsPerSecond = TimelineScrollViewer.ViewportWidth / desiredVisibleDurationSeconds;
                        this._pixelsPerSecond = Math.Clamp(newPixelsPerSecond, GetDynamicMinPixelsPerSecond(), MAX_PIXELS_PER_SECOND);
                    }
                }
                var newClipInstance = asset.Clone();
                _currentProject.TimelineMediaClips.Add(newClipInstance);
                var newVM = new TimelineClipViewModel(newClipInstance);
                newVM.StartTime = startTime;
                newVM.TrackIndex = 0;
                QueueWaveformGeneration(newClipInstance);
                TimelineClips.Add(newVM);
                if (isFirstVideoClip)
                {
                    UpdateProjectReferenceDimensions(asset);
                    videoGrid.Width = CurrentProject.ProjectReferenceVideoWidth;
                    videoGrid.Height = CurrentProject.ProjectReferenceVideoHeight;
                    if (asset.Duration.TotalSeconds > 0 && TimelineScrollViewer.ViewportWidth > 0)
                    {
                        double desiredVisibleDurationSeconds = asset.Duration.TotalSeconds / 0.5;
                        double newPixelsPerSecond = TimelineScrollViewer.ViewportWidth / desiredVisibleDurationSeconds;
                        this._pixelsPerSecond = Math.Clamp(newPixelsPerSecond, GetDynamicMinPixelsPerSecond(), MAX_PIXELS_PER_SECOND);
                    }
                }
                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
            }
            else if (asset.Type == AssetType.Subtitle)
            {
                int track = -2;
                // Gọi hàm LoadSrtFromAssetAsync với dropTime là null để giữ nguyên timecode
                LoadSrtFromAssetAsync(asset, null, track);

            }
            else
            {
                var startTime = _playhead;
                int track = 0;
                if (asset.Type == AssetType.Audio) track = 1;
                else if (asset.Type == AssetType.Image) track = -1;

                TimelineClipViewModel newVM;
                if (asset.Type == AssetType.Audio)
                {
                    var newAudioClip = new TimelineAudioClip
                    {
                        FilePath = asset.FilePath,
                        OriginalDuration = asset.Duration,
                        VolumeDb = asset.VolumeDb,
                        Speed = asset.Speed,
                        WavePeaks = asset.WavePeaks
                    };
                    _currentProject.VoicedSubtitles.Add(newAudioClip);
                    newVM = new TimelineClipViewModel(newAudioClip);
                    QueueWaveformGeneration(newAudioClip);
                }
                else
                {
                    var newClipInstance = asset.Clone();
                    _currentProject.TimelineMediaClips.Add(newClipInstance);
                    newVM = new TimelineClipViewModel(newClipInstance);
                    PopulateImageFilmstrip(newVM);
                }
                newVM.StartTime = startTime;
                newVM.TrackIndex = track;
                TimelineClips.Add(newVM);
                ResolveCollisionsForClip(newVM);

                RecalculateTotalDuration();
                UpdateTimelineScaleAndRender();
                _undoRedoService.AddState(CaptureEditorSnapshot());
                SaveProjectCurrent();
            }

            e.Handled = true;
        }
        #region Media Bin Logic

        private async void MediaBin_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (ActiveMediaAsset != null)
                {
                    await DeleteMediaAsset(ActiveMediaAsset);
                    e.Handled = true;
                }
            }
        }

        private async void DeleteMediaBinItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MediaAsset assetToDelete)
            {
                await DeleteMediaAsset(assetToDelete);
            }
        }
        private async void TranslateSubtitleFromMediaBin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not MediaAsset asset)
            {
                return;
            }
            if (asset.Type != AssetType.Subtitle || string.IsNullOrEmpty(asset.FilePath) || !File.Exists(asset.FilePath))
            {
                return;
            }
            if (SrtSubtitleLinesView.Any(l => !l.IsTextClip))
            {
                var result = CustomMessageBox.Show(
                    "Thao tác này sẽ xoá tất cả các dòng phụ đề hiện có khỏi bảng dịch và timeline. Các Text sẽ được giữ lại. Bạn có muốn tiếp tục?",
                    "Xác nhận",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            if (_isTimelinePlaying)
            {
                await PauseTimelinePlayback();
            }
            var srtClipsOnTimeline = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle).ToList();
            foreach (var clip in srtClipsOnTimeline)
            {
                TimelineClips.Remove(clip);
            }
            _currentProject.Subtitles.Clear();
            var subtitlesInView = SrtSubtitleLinesView.Where(l => !l.IsTextClip).ToList();
            foreach (var line in subtitlesInView)
            {
                SrtSubtitleLinesView.Remove(line);
            }
            await LoadSrtFile(asset.FilePath, addToTimelineAndProject: false);
            TranslateTab.IsChecked = true;
            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            await SaveProjectCurrentAsync();
        }
        private async Task DeleteMediaAsset(MediaAsset assetToDelete)
        {
            if (assetToDelete == null)
            {
                return;
            }

            bool wasPlaying = _isTimelinePlaying;
            if (wasPlaying)
            {
                await PauseTimelinePlayback();
            }
            switch (assetToDelete.Type)
            {
                case AssetType.Video:
                case AssetType.Image:
                    VideoImageAssets.Remove(assetToDelete);
                    break;
                case AssetType.Audio:
                    AudioAssets.Remove(assetToDelete);
                    break;
                case AssetType.Subtitle:
                    SubtitleAssets.Remove(assetToDelete);
                    break;
            }
            var clipsToRemove = TimelineClips
                .Where(vm =>
                {
                    if (vm.SourceData is MediaAsset ma)
                    {
                        return ma.FilePath == assetToDelete.FilePath;
                    }
                    return false;
                })
                .ToList();
            if (assetToDelete.Type == AssetType.Subtitle)
            {
                var srtClipsOnTimeline = TimelineClips.Where(c => c.ClipType == TimelineClipType.Subtitle).ToList();
                clipsToRemove.AddRange(srtClipsOnTimeline);
                clipsToRemove = clipsToRemove.Distinct().ToList();
                var srtLinesToRemove = srtClipsOnTimeline
                    .Select(c => c.SourceData as SrtSubtitleLine)
                    .Where(line => line != null)
                    .ToList();
                foreach (var line in srtLinesToRemove)
                {
                    SrtSubtitleLinesView.Remove(line);
                    _currentProject.Subtitles.Remove(line);
                }
            }
            foreach (var clipVM in clipsToRemove)
            {
                TimelineClips.Remove(clipVM);
                if (clipVM.SourceData is MediaAsset ma)
                {
                    _currentProject.TimelineMediaClips.Remove(ma);
                }
            }
            if (ActiveMediaAsset == assetToDelete)
            {
                ActiveMediaAsset = null;
            }

            RecalculateTotalDuration();
            UpdateTimelineScaleAndRender();
            _undoRedoService.AddState(CaptureEditorSnapshot());
            await SaveProjectCurrentAsync();

            if (wasPlaying)
            {
                await StartTimelinePlayback();
            }
        }

        #endregion

    }
    public static class AssetTypeExtensions
    {
        public static TimelineClipType ToTimelineClipType(this AssetType assetType)
        {
            return assetType switch
            {
                AssetType.Video => TimelineClipType.Video,
                AssetType.Audio => TimelineClipType.Audio,
                AssetType.Image => TimelineClipType.Image,
                AssetType.Subtitle => TimelineClipType.Subtitle,
                _ => throw new ArgumentOutOfRangeException(nameof(assetType), $"Not expected asset type value: {assetType}"),
            };
        }
    }
}

