using NAudio.Wave;
using System;
using System.Diagnostics;

namespace subphimv1.SoundTouch
{
    public class SoundTouchSampleProvider : ISampleProvider, IDisposable
    {
        private readonly ISampleProvider _sourceProvider;
        private readonly SoundTouch _soundTouch;
        private readonly int _channels;
        private readonly float[] _sourceBuffer;
        private float _speed;
        private bool _pitchCorrection;
        public WaveFormat WaveFormat { get; }
        public SoundTouchSampleProvider(ISampleProvider sourceProvider, int sampleRate, int channels)
        {
            _sourceProvider = sourceProvider;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _channels = channels;
            _soundTouch = new SoundTouch();
            _soundTouch.SampleRate = (uint)sampleRate;
            _soundTouch.Channels = (uint)channels;
            _soundTouch[SoundTouch.Setting.SequenceMilliseconds] = 42; 
            _soundTouch[SoundTouch.Setting.SeekWindowMilliseconds] = 22; 
            _soundTouch[SoundTouch.Setting.OverlapMilliseconds] = 12;
            _soundTouch[SoundTouch.Setting.UseQuickSeek] = 1;
            _sourceBuffer = new float[sampleRate * channels / 2]; 
            _speed = 1.0f;
            PitchCorrection = true; 
        }
        public float Speed
        {
            get => _speed;
            set
            {
                if (Math.Abs(_speed - value) > 0.001f)
                {
                    _speed = value;
                    UpdateSoundTouchSettings();
                }
            }
        }
        public bool PitchCorrection
        {
            get => _pitchCorrection;
            set
            {
                if (_pitchCorrection != value)
                {
                    _pitchCorrection = value;
                    UpdateSoundTouchSettings();
                }
            }
        }
        private void UpdateSoundTouchSettings()
        {
            // Đưa bộ xử lý về trạng thái trung tính trước
            _soundTouch.Rate = 1.0f;
            _soundTouch.Tempo = 1.0f;
            _soundTouch.Pitch = 1.0f;

            if (_pitchCorrection)
            {
                // Giữ cao độ, thay đổi trường độ
                _soundTouch.Tempo = _speed;
                _soundTouch.Pitch = 1.0f;
            }
            else
            {
                // Đổi tốc độ có đổi cao độ
                _soundTouch.Rate = _speed;
            }
        }
        private float[] _workOut;
        public int Read(float[] buffer, int offset, int count)
        {
            // Bypass hoàn toàn khi speed ~ 1.0 để tránh artefact ở clip cực ngắn
            if (Math.Abs(_speed - 1.0f) < 0.0001f)
            {
                return _sourceProvider.Read(buffer, offset, count);
            }

            if (_workOut == null || _workOut.Length < count)
            {
                _workOut = new float[count];
            }

            int samplesWritten = 0;

            while (samplesWritten < count)
            {
                if (_soundTouch.AvailableSampleCount == 0)
                {
                    int toRead = Math.Min(_sourceBuffer.Length, count - samplesWritten);
                    int read = _sourceProvider.Read(_sourceBuffer, 0, toRead);

                    if (read > 0)
                    {
                        _soundTouch.PutSamples(_sourceBuffer, (uint)(read / _channels));
                    }
                    else
                    {
                        _soundTouch.Flush();

                        if (_soundTouch.AvailableSampleCount == 0)
                        {
                            break;
                        }
                    }
                }

                int framesToRecv = (count - samplesWritten) / _channels;
                if (framesToRecv <= 0)
                {
                    break;
                }

                int recvFrames = (int)_soundTouch.ReceiveSamples(_workOut, (uint)framesToRecv);
                if (recvFrames == 0)
                {
                    if (_soundTouch.UnprocessedSampleCount == 0)
                    {
                        break;
                    }
                    continue;
                }

                int recvSamples = recvFrames * _channels;
                Array.Copy(_workOut, 0, buffer, offset + samplesWritten, recvSamples);
                samplesWritten += recvSamples;
            }

            return samplesWritten;
        }
        public void Dispose()
        {
            _soundTouch?.Dispose();
        }
    }
}