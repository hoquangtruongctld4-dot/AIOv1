using NAudio.Wave;
using System;
using System.Diagnostics;

namespace subphimv1.Services
{

    public class SpeedChangingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider sourceProvider;
        private readonly int channels;
        private float _playbackRate = 1.0f;
        private float[] sourceBuffer;
        private int sourceBufferCount;
        private int sourceBufferOffset;

        public WaveFormat WaveFormat => sourceProvider.WaveFormat;

        public float PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (value > 0)
                {
                    _playbackRate = value;
                }
            }
        }

        public SpeedChangingSampleProvider(ISampleProvider sourceProvider)
        {
            this.sourceProvider = sourceProvider;
            this.channels = sourceProvider.WaveFormat.Channels;
            this.sourceBuffer = new float[sourceProvider.WaveFormat.SampleRate * channels * 2];
        }
        public int Read(float[] buffer, int offset, int count)
        {
            if (_playbackRate == 1.0f)
            {
                return sourceProvider.Read(buffer, offset, count);
            }

            int samplesWritten = 0;
            int sourceSamplesNeeded = (int)Math.Ceiling(count * _playbackRate / channels) * channels;
            if (sourceBufferCount < sourceSamplesNeeded)
            {
                for (int i = 0; i < sourceBufferCount; i++)
                {
                    sourceBuffer[i] = sourceBuffer[sourceBufferOffset + i];
                }
                sourceBufferOffset = 0;
                int samplesRead = sourceProvider.Read(sourceBuffer, sourceBufferCount, sourceBuffer.Length - sourceBufferCount);
                sourceBufferCount += samplesRead;
            }
            float sourcePosition = 0;
            for (int i = 0; i < count / channels; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int index1 = (int)Math.Floor(sourcePosition) * channels + c;
                    int index2 = index1 + channels;

                    if (sourceBufferOffset + index2 < sourceBufferCount)
                    {
                        float sample1 = sourceBuffer[sourceBufferOffset + index1];
                        float sample2 = sourceBuffer[sourceBufferOffset + index2];
                        float fraction = sourcePosition - (int)Math.Floor(sourcePosition);
                        float interpolatedSample = sample1 + (sample2 - sample1) * fraction;
                        buffer[offset + samplesWritten++] = interpolatedSample;
                    }
                    else
                    {
                        if (sourceBufferOffset + index1 < sourceBufferCount)
                        {
                            buffer[offset + samplesWritten++] = sourceBuffer[sourceBufferOffset + index1];
                        }
                        else
                        {
                            goto endOfRead;
                        }
                    }
                }
                sourcePosition += _playbackRate;
            }

        endOfRead:
            int samplesConsumed = (int)Math.Ceiling(sourcePosition) * channels;
            sourceBufferOffset += samplesConsumed;
            sourceBufferCount -= samplesConsumed;
            for (int i = samplesWritten; i < count; i++)
            {
                buffer[offset + i] = 0;
            }

            return samplesWritten;
        }
    }
}