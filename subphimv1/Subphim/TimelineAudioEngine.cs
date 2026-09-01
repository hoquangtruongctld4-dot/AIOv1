using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using subphimv1.Models;
using subphimv1.Services;
using subphimv1.SoundTouch;
using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Audio
{
    /// <summary>
    /// Gói toàn bộ thông tin audio đang phát cho một clip cụ thể trên timeline.
    /// Reader bây giờ là WaveStream (có thể là AudioFileReader HOẶC MediaFoundationReader),
    /// để chúng ta hỗ trợ cả WAV chuẩn lẫn các file "WAV giả" do CapCut xuất.
    /// </summary>
    internal class AudioSource
    {
        public TimelineClipViewModel ClipVM { get; }
        public ISampleProvider SampleProvider { get; }
        public VolumeSampleProvider VolumeProvider { get; }
        public SoundTouchSampleProvider SpeedProvider { get; }
        public WaveStream Reader { get; }

        public AudioSource(
            TimelineClipViewModel clipVM,
            WaveStream reader,
            ISampleProvider sampleProvider,
            VolumeSampleProvider volumeProvider,
            SoundTouchSampleProvider speedProvider)
        {
            ClipVM = clipVM;
            Reader = reader;
            SampleProvider = sampleProvider;
            VolumeProvider = volumeProvider;
            SpeedProvider = speedProvider;
        }
    }

    /// <summary>
    /// Engine playback audio cho timeline: trộn các clip audio đang "active"
    /// tại thời điểm playhead hiện tại vào một MixingSampleProvider, phát qua WasapiOut.
    /// </summary>
    public class TimelineAudioEngine : IDisposable
    {
        private readonly IWavePlayer _outputDevice;
        private readonly MixingSampleProvider _masterMixer;
        private readonly Dictionary<TimelineClipViewModel, AudioSource> _activeAudioSources;
        private readonly BufferedWaveProvider _bufferedWaveProvider;
        private Task _audioPumpTask;
        private CancellationTokenSource _cancellationTokenSource;
        private TimeSpan _previousTickTime = TimeSpan.Zero;

        public TimelineAudioEngine()
        {
            // Mixer cố định 44.1kHz stereo float
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            _masterMixer = new MixingSampleProvider(waveFormat)
            {
                ReadFully = true
            };

            _outputDevice = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 150);
            _outputDevice.Init(_masterMixer);

            _activeAudioSources = new Dictionary<TimelineClipViewModel, AudioSource>();
        }

        public void Play()
        {
            if (_outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public void Pause()
        {
            if (_outputDevice.PlaybackState == PlaybackState.Playing)
            {
                _outputDevice.Pause();
            }
        }

        /// <summary>
        /// Nhảy seek toàn timeline: clear mixer, rebuild các clip nào đang "bao" playhead.
        /// </summary>
        public void SeekTo(TimeSpan currentTime, IEnumerable<TimelineClipViewModel> allClips)
        {
            _masterMixer.RemoveAllMixerInputs();

            foreach (var source in _activeAudioSources.Values)
            {
                // để Dispose đúng chỗ trong RemoveAudioSource, ta không dispose ở đây
            }

            _activeAudioSources.Clear();

            var clipsToPlayNow = allClips
                .Where(c => HasAudio(c)
                            && c.StartTime <= currentTime
                            && c.EndTime > currentTime);

            foreach (var clip in clipsToPlayNow)
            {
                AddAudioSource(clip, currentTime);
            }

            _previousTickTime = currentTime;
        }

        /// <summary>
        /// Gọi liên tục khi playhead chạy: bật clip mới bắt đầu, tắt clip đã kết thúc.
        /// </summary>
        public void Update(TimeSpan currentTime, IEnumerable<TimelineClipViewModel> allClips)
        {
            // 1. Loại bỏ source nào đã hết hiệu lực
            var finishedSources = _activeAudioSources.Keys
                .Where(c => c.EndTime <= currentTime)
                .ToList();

            foreach (var clip in finishedSources)
            {
                RemoveAudioSource(clip);
            }

            // 2. Thêm source nào vừa "bắt đầu" giữa previousTickTime -> currentTime
            var justStartedSources = allClips
                .Where(c => HasAudio(c)
                            && !_activeAudioSources.ContainsKey(c)
                            && c.StartTime > _previousTickTime
                            && c.StartTime <= currentTime);

            foreach (var clip in justStartedSources)
            {
                AddAudioSource(clip, currentTime);
            }

            _previousTickTime = currentTime;
        }

        /// <summary>
        /// Khi user đổi VolumeDb / Speed / PitchCorrection trong UI với clip đang phát,
        /// cập nhật trực tiếp provider tương ứng thay vì rebuild.
        /// </summary>
        public void UpdateClipProperties(TimelineClipViewModel clipVM)
        {
            if (_activeAudioSources.TryGetValue(clipVM, out var source))
            {
                // [DEBUG] BẮT ĐẦU: Chèn code gỡ lỗi
                System.Diagnostics.Debug.WriteLine($"[DEBUG] AUDIO ENGINE: Updating properties for clip '{clipVM.FileName}' during playback.");
                // [DEBUG] KẾT THÚC

                float newVolumeGain = (float)Math.Pow(10, clipVM.VolumeDb / 20.0);
                source.VolumeProvider.Volume = newVolumeGain;

                // [DEBUG] BẮT ĐẦU: Chèn code gỡ lỗi
                System.Diagnostics.Debug.WriteLine($"[DEBUG] AUDIO ENGINE: Setting SpeedProvider.Speed to {(float)clipVM.Speed}");
                // [DEBUG] KẾT THÚC
                source.SpeedProvider.Speed = (float)clipVM.Speed;

                if (clipVM.SourceData is TimelineAudioClip tac)
                {
                    // [DEBUG] BẮT ĐẦU: Chèn code gỡ lỗi
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] AUDIO ENGINE: Setting SpeedProvider.PitchCorrection to {tac.PitchCorrection}");
                    // [DEBUG] KẾT THÚC
                    source.SpeedProvider.PitchCorrection = tac.PitchCorrection;
                }
            }
            else
            {
                // [DEBUG] BẮT ĐẦU: Chèn code gỡ lỗi
                System.Diagnostics.Debug.WriteLine($"[DEBUG] AUDIO ENGINE: Could not find active audio source for clip '{clipVM.FileName}' to update properties.");
                // [DEBUG] KẾT THÚC
            }
        }
        /// <summary>
        /// HÀM MỚI: mở file audio 1 cách "chịu lỗi".
        /// - Thử AudioFileReader trước (chuẩn cũ).
        /// - Nếu fail FormatException "Not a WAVE file - no RIFF header" (CapCut WAV giả),
        ///   fallback sang MediaFoundationReader để đọc bằng Windows Media Foundation.
        /// Trả về WaveStream (cha chung) để code phía sau xử lý bình thường.
        /// </summary>
        private WaveStream OpenAudioReaderRobust(string filePath)
        {
            WaveStream result = null;

            // Thử AudioFileReader trước
            try
            {
                result = new AudioFileReader(filePath);
                return result;
            }
            catch (FormatException fe)
            {
                Debug.WriteLine($"[AudioEngine] AudioFileReader không đọc được '{filePath}': {fe.Message} -> thử MediaFoundationReader fallback.");
                // tiếp tục fallback
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine] AudioFileReader lỗi không mong đợi với '{filePath}': {ex.Message} -> thử MediaFoundationReader fallback.");
                // tiếp tục fallback
            }

            // Fallback: MediaFoundationReader
            try
            {
                // MediaFoundationReader dùng Windows Media Foundation, có thể
                // giải mã nhiều định dạng (AAC, M4A, ...), kể cả trường hợp
                // file có phần mở rộng .wav nhưng header KHÔNG phải RIFF/WAVE.
                result = new MediaFoundationReader(filePath);
                return result;
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"[AudioEngine] MediaFoundationReader cũng không mở được '{filePath}': {ex2.Message}");
            }

            // Cả hai đều thất bại -> trả null để caller biết bỏ clip này
            return null;
        }

        /// <summary>
        /// Tạo AudioSource cho 1 clip và add vào mixer nếu mở file thành công.
        /// Đây là nơi ta sửa để hỗ trợ WAV CapCut bị lỗi header.
        /// </summary>
        private void AddAudioSource(TimelineClipViewModel clipVM, TimeSpan currentTime)
        {
            // Bảo vệ: path trống hoặc file không tồn tại thì bỏ qua
            if (string.IsNullOrEmpty(clipVM.FilePath) ||
                !System.IO.File.Exists(clipVM.FilePath))
            {
                return;
            }

            // Nếu clip đã có source đang chạy thì không tạo lại
            if (_activeAudioSources.ContainsKey(clipVM))
            {
                return;
            }

            // ===== BẮT ĐẦU SỬA LỖI WAV CAPCUT =====
            // Thay vì gọi thẳng new AudioFileReader(...) như code cũ,
            // ta dùng OpenAudioReaderRobust để có fallback MediaFoundationReader.
            WaveStream reader = OpenAudioReaderRobust(clipVM.FilePath);
            if (reader == null)
            {
                Debug.WriteLine($"[AudioEngine] Không thể mở file audio (kể cả fallback): {clipVM.FilePath}");
                return;
            }
            // ===== KẾT THÚC SỬA LỖI WAV CAPCUT =====

            // Tính vị trí phát ban đầu trong source dựa trên vị trí playhead timeline
            // currentTime là playhead toàn timeline.
            // clipVM.StartTime là lúc clip bắt đầu trên timeline.
            // clipVM.Speed là speed playback (ví dụ 1.2x).
            TimeSpan timeIntoClipOnTimeline = currentTime - clipVM.StartTime;

            // Nếu speed != 1.0 thì timeline đi nhanh hơn/chậm hơn source thực.
            TimeSpan timeIntoSourceFile =
                TimeSpan.FromSeconds(timeIntoClipOnTimeline.TotalSeconds * clipVM.Speed);

            // Nếu người dùng cắt đầu clip (trimStartOffset) thì cộng vào
            TimeSpan sourceTrimStart = GetTrimStart(clipVM);

            // Vị trí thực sự trong file nguồn mà ta nên bắt đầu đọc
            TimeSpan positionInFile = timeIntoSourceFile + sourceTrimStart;

            // Nếu seek vượt quá độ dài file thì thôi, không phát nữa
            if (positionInFile >= reader.TotalTime)
            {
                reader.Dispose();
                return;
            }

            // Seek reader tới đúng chỗ để phát "từ giữa"
            reader.CurrentTime = positionInFile;

            // Bây giờ build pipeline SampleProvider -> Speed/Pitch -> Volume -> Stereo -> Mixer

            // Chuyển reader (WaveStream) -> ISampleProvider float
            ISampleProvider sourceProvider = reader.ToSampleProvider();

            // Resample trước để khớp sample rate mixer (mixer đang 44100 Hz float stereo)
            if (sourceProvider.WaveFormat.SampleRate != _masterMixer.WaveFormat.SampleRate)
            {
                sourceProvider = new WdlResamplingSampleProvider(
                    sourceProvider,
                    _masterMixer.WaveFormat.SampleRate
                );
            }

            // Tầng SoundTouch để xử lý Speed và PitchCorrection
            // LƯU Ý: ta dùng số kênh thật của nguồn sau resample
            var speedProvider = new SoundTouchSampleProvider(
                sourceProvider,
                sourceProvider.WaveFormat.SampleRate,
                sourceProvider.WaveFormat.Channels
            );

            // Đồng bộ với UI: clipVM.Speed và PitchCorrection (nếu là TimelineAudioClip)
            speedProvider.Speed = (float)clipVM.Speed;

            if (clipVM.SourceData is TimelineAudioClip tac)
            {
                // PitchCorrection = true => thay đổi tempo nhưng giữ pitch
                // PitchCorrection = false => thay đổi rate, pitch thay đổi theo speed
                speedProvider.PitchCorrection = tac.PitchCorrection;
            }

            // Áp volume dB (VolumeDb lưu trong model, ví dụ -3 dB)
            float initialVolumeGain = (float)Math.Pow(10, clipVM.VolumeDb / 20.0);
            var volumeProvider = new VolumeSampleProvider(speedProvider)
            {
                Volume = initialVolumeGain
            };

            // Nếu nguồn mono thì duplicate channel sang stereo
            ISampleProvider finalProvider =
                (speedProvider.WaveFormat.Channels == 1)
                    ? new MonoToStereoSampleProvider(volumeProvider)
                    : (ISampleProvider)volumeProvider;

            // Thêm vào mixer đầu ra chung
            _masterMixer.AddMixerInput(finalProvider);

            // Lưu lại để chúng ta có thể update speed/volume realtime, dispose đúng lúc, v.v.
            var audioSource = new AudioSource(
                clipVM,
                reader,
                finalProvider,
                volumeProvider,
                speedProvider
            );

            _activeAudioSources[clipVM] = audioSource;

            // Lắng nghe thay đổi runtime (VolumeDb, Speed, PitchCorrection)
            if (clipVM.SourceData is INotifyPropertyChanged model)
            {
                model.PropertyChanged += ActiveSource_PropertyChanged;
            }
        }

        /// <summary>
        /// Khi model (TimelineAudioClip / MediaAsset) đổi thuộc tính lúc đang phát,
        /// ta update provider tương ứng mà không cần rebuild source.
        /// </summary>
        private void ActiveSource_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TimelineAudioClip.VolumeDb) &&
                e.PropertyName != nameof(MediaAsset.VolumeDb) &&
                e.PropertyName != nameof(TimelineAudioClip.Speed) &&
                e.PropertyName != nameof(MediaAsset.Speed) &&
                e.PropertyName != nameof(TimelineAudioClip.PitchCorrection))
            {
                return;
            }

            var sourceModel = sender as INotifyPropertyChanged;
            if (sourceModel == null) return;

            var activeSource = _activeAudioSources.Values
                .FirstOrDefault(s => s.ClipVM.SourceData == sourceModel);

            if (activeSource != null)
            {
                if (e.PropertyName == nameof(TimelineAudioClip.VolumeDb) ||
                    e.PropertyName == nameof(MediaAsset.VolumeDb))
                {
                    float newVolumeGain = (float)Math.Pow(10, activeSource.ClipVM.VolumeDb / 20.0);
                    activeSource.VolumeProvider.Volume = newVolumeGain;
                }
                else if (e.PropertyName == nameof(TimelineAudioClip.Speed) ||
                         e.PropertyName == nameof(MediaAsset.Speed))
                {
                    activeSource.SpeedProvider.Speed = (float)activeSource.ClipVM.Speed;
                }
                else if (e.PropertyName == nameof(TimelineAudioClip.PitchCorrection))
                {
                    if (activeSource.ClipVM.SourceData is TimelineAudioClip tac)
                    {
                        activeSource.SpeedProvider.PitchCorrection = tac.PitchCorrection;
                    }
                }
            }
        }

        /// <summary>
        /// Gỡ 1 clip audio ra khỏi mixer và dispose tài nguyên audio tương ứng.
        /// </summary>
        private void RemoveAudioSource(TimelineClipViewModel clipVM)
        {
            if (_activeAudioSources.TryGetValue(clipVM, out var source))
            {
                if (source.ClipVM.SourceData is INotifyPropertyChanged model)
                {
                    model.PropertyChanged -= ActiveSource_PropertyChanged;
                }

                _masterMixer.RemoveMixerInput(source.SampleProvider);
                _activeAudioSources.Remove(clipVM);

                // Giải phóng theo đúng thứ tự để không giữ handle file
                try { source.SpeedProvider?.Dispose(); } catch { }
                try { source.Reader?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Kiểm tra clip có audio để phát không. Hiện tại chỉ phát loại TimelineClipType.Audio.
        /// </summary>
        private bool HasAudio(TimelineClipViewModel c)
        {
            return c.ClipType == TimelineClipType.Audio;
        }

        /// <summary>
        /// Lấy offset trim đầu clip (nếu user kéo cắt đầu).
        /// </summary>
        private TimeSpan GetTrimStart(TimelineClipViewModel c)
        {
            if (c.SourceData is MediaAsset ma) return ma.TrimStartOffset;
            if (c.SourceData is TimelineAudioClip tac) return tac.TrimStartOffset;
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Lấy duration gốc của source (không speed).
        /// (Hiện tại chưa dùng trong AddAudioSource nhưng để nguyên cho logic khác.)
        /// </summary>
        private TimeSpan GetSourceDuration(TimelineClipViewModel c)
        {
            if (c.SourceData is MediaAsset ma) return ma.Duration;
            if (c.SourceData is TimelineAudioClip tac) return tac.OriginalDuration;
            return c.Duration;
        }

        public void Dispose()
        {
            _outputDevice?.Stop();
            _outputDevice?.Dispose();

            _masterMixer.RemoveAllMixerInputs();

            foreach (var source in _activeAudioSources.Values)
            {
                source.Reader?.Dispose();
            }

            _activeAudioSources.Clear();
        }
    }
}
