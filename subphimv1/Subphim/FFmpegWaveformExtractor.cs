using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace subphimv1.Waveform
{
    public static unsafe class FFmpegWaveformExtractor
    {
        private static bool _isRegistered = false;

        public static void Register(string ffmpegDllFolder)
        {
            if (_isRegistered) return;
            if (!Directory.Exists(ffmpegDllFolder)) throw new DirectoryNotFoundException($"Thư mục DLL của FFmpeg không tồn tại: {ffmpegDllFolder}");
            ffmpeg.RootPath = ffmpegDllFolder;
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            _isRegistered = true;
        }

        public static float[] ExtractPeaks(string mediaPath, int targetRate = 11025, int binSize = 128, float gamma = 0.6f)
        {
            if (!File.Exists(mediaPath)) throw new FileNotFoundException("File media không tồn tại.", mediaPath);
            if (!_isRegistered) throw new InvalidOperationException("FFmpegWaveformExtractor chưa được khởi tạo. Vui lòng gọi Register() trước.");

            AVFormatContext* fmt = null;
            int err = ffmpeg.avformat_open_input(&fmt, mediaPath, null, null);
            if (err < 0) throw new ApplicationException($"avformat_open_input thất bại: {FF(err)}");

            try
            {
                err = ffmpeg.avformat_find_stream_info(fmt, null);
                if (err < 0) throw new ApplicationException($"avformat_find_stream_info thất bại: {FF(err)}");

                int aIndex = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
                if (aIndex < 0) throw new ApplicationException("Không tìm thấy luồng âm thanh trong file.");

                AVStream* aStream = fmt->streams[aIndex];
                AVCodecParameters* par = aStream->codecpar;

                AVCodec* dec = ffmpeg.avcodec_find_decoder(par->codec_id);
                if (dec == null) throw new ApplicationException("Không tìm thấy bộ giải mã âm thanh phù hợp.");

                AVCodecContext* decCtx = ffmpeg.avcodec_alloc_context3(dec);
                if (decCtx == null) throw new ApplicationException("Cấp phát ngữ cảnh giải mã (AVCodecContext) thất bại.");

                try
                {
                    FFThrow(ffmpeg.avcodec_parameters_to_context(decCtx, par), "avcodec_parameters_to_context");
                    if (decCtx->channel_layout == 0)
                        decCtx->channel_layout = (ulong)ffmpeg.av_get_default_channel_layout(decCtx->channels);

                    FFThrow(ffmpeg.avcodec_open2(decCtx, dec, null), "avcodec_open2");

                    long out_ch_layout = ffmpeg.AV_CH_LAYOUT_MONO;
                    AVSampleFormat out_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
                    SwrContext* swr = ffmpeg.swr_alloc_set_opts(
                        null,
                        out_ch_layout, out_fmt, targetRate,
                        (long)decCtx->channel_layout, decCtx->sample_fmt, decCtx->sample_rate,
                        0, null);
                    if (swr == null) throw new ApplicationException("swr_alloc_set_opts thất bại.");
                    try
                    {
                        FFThrow(ffmpeg.swr_init(swr), "swr_init");

                        var peaks = new List<float>(1024);
                        int acc = 0; int maxAbs = 0;

                        AVPacket* pkt = ffmpeg.av_packet_alloc();
                        if (pkt == null) throw new ApplicationException("av_packet_alloc thất bại");

                        AVFrame* frame = ffmpeg.av_frame_alloc();
                        if (frame == null) throw new ApplicationException("av_frame_alloc thất bại");

                        try
                        {
                            int outChannels = ffmpeg.av_get_channel_layout_nb_channels((ulong)out_ch_layout);
                            int outBps = ffmpeg.av_get_bytes_per_sample(out_fmt);
                            int maxDstSamples = 8192;
                            byte* outBuf = (byte*)ffmpeg.av_malloc((ulong)(maxDstSamples * outChannels * outBps));
                            if (outBuf == null) throw new ApplicationException("av_malloc cho outBuf thất bại.");

                            try
                            {
                                while (ffmpeg.av_read_frame(fmt, pkt) >= 0)
                                {
                                    if (pkt->stream_index != aIndex) { ffmpeg.av_packet_unref(pkt); continue; }

                                    FFThrow(ffmpeg.avcodec_send_packet(decCtx, pkt), "send_packet");
                                    ffmpeg.av_packet_unref(pkt);

                                    while (true)
                                    {
                                        int r = ffmpeg.avcodec_receive_frame(decCtx, frame);
                                        if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF) break;
                                        FFThrow(r, "receive_frame");

                                        long delay = ffmpeg.swr_get_delay(swr, decCtx->sample_rate);
                                        int dstNbSamples = (int)ffmpeg.av_rescale_rnd(delay + frame->nb_samples, targetRate, decCtx->sample_rate, AVRounding.AV_ROUND_UP);
                                        if (dstNbSamples > maxDstSamples)
                                        {
                                            maxDstSamples = dstNbSamples;
                                            outBuf = (byte*)ffmpeg.av_realloc(outBuf, (ulong)(maxDstSamples * outChannels * outBps));
                                            if (outBuf == null) throw new ApplicationException("av_realloc thất bại.");
                                        }

                                        int got = ffmpeg.swr_convert(swr, &outBuf, dstNbSamples, frame->extended_data, frame->nb_samples);
                                        FFThrow(got < 0 ? got : 0, "swr_convert");
                                        int sampleCount = got * outChannels;

                                        short* p = (short*)outBuf;
                                        for (int i = 0; i < sampleCount; i++)
                                        {
                                            // SỬA LỖI QUAN TRỌNG: Ép kiểu sang (int) TRƯỚC KHI gọi Math.Abs
                                            int a = Math.Abs((int)p[i]);
                                            if (a > maxAbs) maxAbs = a;
                                            if (++acc >= binSize)
                                            {
                                                peaks.Add(maxAbs / 32768f);
                                                acc = 0; maxAbs = 0;
                                            }
                                        }
                                    }
                                }

                                // flush decoder
                                FFThrow(ffmpeg.avcodec_send_packet(decCtx, null), "send_flush");
                                while (true)
                                {
                                    int r = ffmpeg.avcodec_receive_frame(decCtx, frame);
                                    if (r == ffmpeg.AVERROR_EOF || r == ffmpeg.AVERROR(ffmpeg.EAGAIN)) break;
                                    FFThrow(r, "receive_frame(flush)");

                                    long delay = ffmpeg.swr_get_delay(swr, decCtx->sample_rate);
                                    int dstNbSamples = (int)ffmpeg.av_rescale_rnd(delay + frame->nb_samples, targetRate, decCtx->sample_rate, AVRounding.AV_ROUND_UP);
                                    if (dstNbSamples > maxDstSamples)
                                    {
                                        maxDstSamples = dstNbSamples;
                                        outBuf = (byte*)ffmpeg.av_realloc(outBuf, (ulong)(maxDstSamples * outChannels * outBps));
                                        if (outBuf == null) throw new ApplicationException("av_realloc thất bại.");
                                    }

                                    int got = ffmpeg.swr_convert(swr, &outBuf, dstNbSamples, frame->extended_data, frame->nb_samples);
                                    FFThrow(got < 0 ? got : 0, "swr_convert");
                                    int sampleCount = got * outChannels;

                                    short* p = (short*)outBuf;
                                    for (int i = 0; i < sampleCount; i++)
                                    {
                                        // SỬA LỖI QUAN TRỌNG: Áp dụng sửa lỗi tương tự cho phần flush
                                        int a = Math.Abs((int)p[i]);
                                        if (a > maxAbs) maxAbs = a;
                                        if (++acc >= binSize)
                                        {
                                            peaks.Add(maxAbs / 32768f);
                                            acc = 0; maxAbs = 0;
                                        }
                                    }
                                }

                                if (acc > 0) peaks.Add(maxAbs / 32768f);

                                var arr = peaks.ToArray();
                                for (int i = 0; i < arr.Length; i++)
                                {
                                    float x = MathF.Min(1f, arr[i]);
                                    arr[i] = MathF.Pow(x, gamma);
                                }
                                return arr;
                            }
                            finally { ffmpeg.av_free(outBuf); }
                        }
                        finally
                        {
                            ffmpeg.av_frame_free(&frame);
                            ffmpeg.av_packet_free(&pkt);
                        }
                    }
                    finally { ffmpeg.swr_free(&swr); }
                }
                finally
                {
                    ffmpeg.avcodec_free_context(&decCtx);
                }
            }
            finally
            {
                ffmpeg.avformat_close_input(&fmt);
            }
        }

        static void FFThrow(int code, string where)
        {
            if (code >= 0) return;
            throw new ApplicationException($"{where} thất bại: {FF(code)}");
        }
        static string FF(int err)
        {
            var buf = stackalloc byte[1024];
            ffmpeg.av_strerror(err, buf, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buf) ?? err.ToString();
        }
    }
}