#define WINDOWS
using FFmpeg.AutoGen;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace subphimv1.Filmstrip
{
    public sealed class FilmstripService : IDisposable
    {
        // ==== cấu hình ====
        private const int MaxConcurrentDecodes = 2;
        private const int MaxRamCacheItems = 512;
        private const string DiskCacheFolderName = "FilmstripCache"; // %LocalAppData%\AIOLauncher\FilmstripCache

        private readonly string _videoPath;
        private readonly string _diskCacheDir;
        private readonly SemaphoreSlim _decodeGate = new(MaxConcurrentDecodes, MaxConcurrentDecodes);
        private readonly object _ffmpegLock = new object();
        private unsafe AVFormatContext* _fmt = null;
        private unsafe AVCodecContext* _dec = null;
        private unsafe SwsContext* _sws = null;
        private unsafe AVStream* _vStream = null;
        private int _vIndex = -1;
        private AVRational _timeBase;
        private int _srcW, _srcH, _pixFmt;
        private int _swsSrcFmt, _swsSrcW, _swsSrcH, _swsDstW, _swsDstH;
        private static readonly ConcurrentDictionary<string, BitmapImage> _ramCache = new();
        private static readonly object _swsLock = new(); // sws không thread-safe với cùng ctx

        public static FilmstripService Open(string videoPath)
        {
            FFmpegLoader.EnsureRegistered(); 
            return new FilmstripService(videoPath);
        }

        private FilmstripService(string videoPath)
        {
            _videoPath = videoPath ?? throw new ArgumentNullException(nameof(videoPath));
            _diskCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                         "AIOLauncher", DiskCacheFolderName);
            Directory.CreateDirectory(_diskCacheDir);
            OpenCore();
        }

        private unsafe void OpenCore()
        {
            System.Diagnostics.Debug.WriteLine($"[FilmstripService] OpenCore: Bắt đầu mở file. Path: '{_videoPath}'");

            // KIỂM TRA QUAN TRỌNG NHẤT: File có thực sự tồn tại ở đường dẫn này không?
            if (!File.Exists(_videoPath))
            {
                System.Diagnostics.Debug.WriteLine($"[FATAL CRASH PREDICTION] File does not exist at path: '{_videoPath}'. FFmpeg will crash.");
                throw new FileNotFoundException("Video file not found for FilmstripService.", _videoPath);
            }

            AVFormatContext* fmt = null;

            System.Diagnostics.Debug.WriteLine("[FilmstripService] OpenCore: Chuẩn bị gọi avformat_open_input...");
            ffmpeg.avformat_open_input(&fmt, _videoPath, null, null).ThrowIfError();
            System.Diagnostics.Debug.WriteLine("[FilmstripService] OpenCore: avformat_open_input THÀNH CÔNG.");

            System.Diagnostics.Debug.WriteLine("[FilmstripService] OpenCore: Chuẩn bị gọi avformat_find_stream_info...");
            ffmpeg.avformat_find_stream_info(fmt, null).ThrowIfError();
            System.Diagnostics.Debug.WriteLine("[FilmstripService] OpenCore: avformat_find_stream_info THÀNH CÔNG.");

            int best = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (best < 0) throw new InvalidOperationException("No video stream");
            var vStream = fmt->streams[best];

            var codec = ffmpeg.avcodec_find_decoder(vStream->codecpar->codec_id);
            if (codec == null) throw new InvalidOperationException("Decoder not found");

            AVCodecContext* dec = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(dec, vStream->codecpar).ThrowIfError();

            dec->skip_frame = AVDiscard.AVDISCARD_NONKEY;
            dec->thread_count = Math.Max(1, Environment.ProcessorCount / 2);

            ffmpeg.avcodec_open2(dec, codec, null).ThrowIfError();

            _fmt = fmt; _dec = dec; _vStream = vStream; _vIndex = best;
            _timeBase = vStream->time_base;
            _srcW = dec->width; _srcH = dec->height; _pixFmt = (int)dec->pix_fmt;
            System.Diagnostics.Debug.WriteLine("[FilmstripService] OpenCore: Mở file và khởi tạo context thành công.");
        }
        public unsafe void Dispose()
        {
            if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }

            // Sử dụng fixed để ghim con trỏ trước khi giải phóng
            fixed (AVCodecContext** pDec = &_dec)
            {
                if (*pDec != null) { ffmpeg.avcodec_free_context(pDec); }
            }
            _dec = null;

            fixed (AVFormatContext** pFmt = &_fmt)
            {
                if (*pFmt != null) { ffmpeg.avformat_close_input(pFmt); }
            }
            _fmt = null;

            _decodeGate.Dispose();
        }

        public async Task<IReadOnlyList<BitmapImage>> GetFilmstripAsync(TimeSpan start, TimeSpan duration, int count, int targetHeight, CancellationToken ct)
        {
            if (count <= 0) return Array.Empty<BitmapImage>();

            // Hàm nội bộ để phân bổ timestamp một cách chính xác
            IReadOnlyList<TimeSpan> Distribute(TimeSpan s, TimeSpan d, int c)
            {
                if (c <= 1) return new[] { s };
                var list = new TimeSpan[c];
                // SỬA LỖI: Chia cho (count - 1) để thumbnail cuối cùng nằm ở cuối duration
                double step = c > 1 ? d.TotalSeconds / (c - 1) : 0;
                for (int i = 0; i < c; i++)
                    list[i] = s + TimeSpan.FromSeconds(i * step);
                return list;
            }

            var stamps = Distribute(start, duration, count);

            // Ưu tiên song song giới hạn
            var results = new BitmapImage[count];
            var tasks = Enumerable.Range(0, count).Select(async i =>
            {
                ct.ThrowIfCancellationRequested();
                results[i] = await GetThumbnailAtAsync(stamps[i], targetHeight, ct).ConfigureAwait(false);
            });
            await Task.WhenAll(tasks);
            return results;
        }
        public async Task<BitmapImage> GetThumbnailAtAsync(TimeSpan t, int targetHeight, CancellationToken ct)
        {
            string cacheKey = CacheKey(_videoPath, t, targetHeight);
            if (_ramCache.TryGetValue(cacheKey, out var hit)) return hit;

            // cache đĩa
            var diskFile = Path.Combine(_diskCacheDir, SanitizeFileName(cacheKey) + ".jpg");
            if (File.Exists(diskFile))
            {
                var bi = LoadBitmapImage(File.ReadAllBytes(diskFile));
                if (bi != null)
                {
                    _ramCache[cacheKey] = bi;
                    TrimRamCache();
                    return bi;
                }
            }

            await _decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // double-check sau khi vào cửa
                if (_ramCache.TryGetValue(cacheKey, out hit)) return hit;

                var jpgBytes = DecodeOneJpegAt(t, targetHeight);
                if (jpgBytes != null && jpgBytes.Length > 0)
                {
                    await File.WriteAllBytesAsync(diskFile, jpgBytes, ct);

                    var bi2 = LoadBitmapImage(jpgBytes);
                    if (bi2 != null)
                    {
                        _ramCache[cacheKey] = bi2;
                        TrimRamCache();
                        return bi2;
                    }
                }
                return null;
            }
            finally
            {
                _decodeGate.Release();
            }
        }

        private unsafe byte[] DecodeOneJpegAt(TimeSpan ts, int targetH)
        {
            // SỬA LỖI: Sử dụng lock để đảm bảo chỉ một luồng có thể truy cập
            // các context FFmpeg (không thread-safe) tại một thời điểm.
            lock (_ffmpegLock)
            {
                System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Bắt đầu giải mã tại {ts.TotalSeconds:F3}s cho file '{Path.GetFileName(_videoPath)}'");

                // KIỂM TRA CÁC CON TRỎ QUAN TRỌNG
                if (_fmt == null || _dec == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: LỖI NGHIÊM TRỌNG - Context bị null trước khi giải mã.");
                    throw new ObjectDisposedException("FilmstripService", "FFmpeg contexts are null, service might have been disposed.");
                }

                try
                {
                    long targetTs = ToTs(ts);
                    ffmpeg.avcodec_flush_buffers(_dec);
                    int flags = ffmpeg.AVSEEK_FLAG_BACKWARD;

                    System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Seeking to timestamp {targetTs}...");
                    ffmpeg.av_seek_frame(_fmt, _vIndex, targetTs, flags).ThrowIfError();
                    System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Seek thành công.");

                    // SỬA LỖI: av_seek_frame có thể không đủ, cần flush cả format context
                    // để đảm bảo bộ đệm nội bộ được xóa sạch sau khi seek.
                    ffmpeg.avformat_flush(_fmt);

                    using var pkt = new Packet();
                    using var frm = new Frame();
                    using var rgb = new Frame();

                    int dstH = targetH <= 0 ? Math.Max(1, _srcH / 8) : targetH;
                    int dstW = (int)Math.Max(1, Math.Round((double)_srcW * dstH / _srcH));

                    rgb.Ptr->format = (int)AVPixelFormat.AV_PIX_FMT_BGR24;
                    rgb.Ptr->width = dstW;
                    rgb.Ptr->height = dstH;
                    ffmpeg.av_frame_get_buffer(rgb.Ptr, 1).ThrowIfError();

                    while (ffmpeg.av_read_frame(_fmt, pkt.Ptr) >= 0)
                    {
                        try
                        {
                            if (pkt.Ptr->stream_index != _vIndex) continue;

                            ffmpeg.avcodec_send_packet(_dec, pkt.Ptr).ThrowIfConsume();
                            while (true)
                            {
                                int r = ffmpeg.avcodec_receive_frame(_dec, frm.Ptr);
                                if (r == ffmpeg.AVERROR(ffmpeg.EAGAIN) || r == ffmpeg.AVERROR_EOF) break;
                                r.ThrowIfError();

                                EnsureSws(frm.Ptr->format, frm.Ptr->width, frm.Ptr->height, dstW, dstH);

                                lock (_swsLock)
                                {
                                    // Thêm kiểm tra _sws không bị null do luồng khác dispose
                                    if (_sws == null) throw new ObjectDisposedException("SwsContext", "SwsContext was disposed unexpectedly.");

                                    ffmpeg.sws_scale(_sws,
                                        frm.Ptr->data, frm.Ptr->linesize, 0, frm.Ptr->height,
                                        rgb.Ptr->data, rgb.Ptr->linesize);
                                }

                                System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Giải mã và scale thành công tại {ts.TotalSeconds:F3}s.");
                                return EncodeJpeg(rgb.Ptr);
                            }
                        }
                        finally
                        {
                            ffmpeg.av_packet_unref(pkt.Ptr);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Không tìm thấy frame nào sau khi seek. Trả về ảnh rỗng.");
                    return EmptyJpeg(dstW: Math.Max(1, _srcW / 8), dstH: Math.Max(1, _srcH / 8));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DecodeOneJpegAt] Thread {Thread.CurrentThread.ManagedThreadId}: Đã bắt được Exception trong quá trình giải mã: {ex}");
                    // Trả về ảnh rỗng thay vì ném lỗi để không làm sập toàn bộ chuỗi tác vụ
                    return EmptyJpeg(dstW: Math.Max(1, _srcW / 8), dstH: Math.Max(1, _srcH / 8));
                }
            } // Kết thúc khối lock
        }

        private unsafe void EnsureSws(int srcFmt, int srcW, int srcH, int dstW, int dstH)
        {
            lock (_swsLock)
            {
                bool same =
                    _sws != null &&
                    _swsSrcFmt == srcFmt &&
                    _swsSrcW == srcW && _swsSrcH == srcH &&
                    _swsDstW == dstW && _swsDstH == dstH;

                if (!same)
                {
                    if (_sws != null)
                    {
                        ffmpeg.sws_freeContext(_sws);
                        _sws = null;
                    }

                    _sws = ffmpeg.sws_getContext(
                        srcW, srcH, (AVPixelFormat)srcFmt,
                        dstW, dstH, AVPixelFormat.AV_PIX_FMT_BGR24,
                        ffmpeg.SWS_BILINEAR, null, null, null);

                    if (_sws == null) throw new InvalidOperationException("sws_getContext failed");

                    _swsSrcFmt = srcFmt;
                    _swsSrcW = srcW;
                    _swsSrcH = srcH;
                    _swsDstW = dstW;
                    _swsDstH = dstH;
                }
            }
        }
        private long ToTs(TimeSpan t)
        {
            double tb = ffmpeg.av_q2d(_timeBase);
            long ts = (long)Math.Round(t.TotalSeconds / tb);
            if (ts < 0) ts = 0;
            return ts;
        }
        private static IReadOnlyList<TimeSpan> Distribute(TimeSpan start, TimeSpan dur, int count)
        {
            if (count <= 1) return new[] { start };
            var list = new TimeSpan[count];
            // SỬA LỖI: Chia cho (count - 1) để điểm cuối cùng nằm chính xác ở cuối duration.
            // Nếu count > 1, phải có ít nhất 1 khoảng step.
            double step = count > 1 ? dur.TotalSeconds / (count - 1) : 0;
            for (int i = 0; i < count; i++)
                list[i] = start + TimeSpan.FromSeconds(i * step);
            return list;
        }

        private static string CacheKey(string path, TimeSpan t, int h)
            => $"{path}|{t.Ticks}|H{h}";

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Length > 150 ? s.Substring(s.Length - 150) : s;
        }

        private static void TrimRamCache()
        {
            if (_ramCache.Count <= MaxRamCacheItems) return;
            // xóa bừa để đơn giản. Có thể nâng cấp LRU sau.
            foreach (var key in _ramCache.Keys.Take(_ramCache.Count - MaxRamCacheItems))
                _ramCache.TryRemove(key, out _);
        }

        private static BitmapImage LoadBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        private unsafe static byte[] EncodeJpeg(AVFrame* bgr)
        {
            // bgr24 -> BitmapSource -> JpegBitmapEncoder
            int w = bgr->width;
            int h = bgr->height;
            int stride = bgr->linesize[0];
            byte[] buf = new byte[h * stride];
            Marshal.Copy((IntPtr)bgr->data[0], buf, 0, buf.Length);

            var bs = BitmapSource.Create(
                w, h, 96, 96,
                System.Windows.Media.PixelFormats.Bgr24,
                null, buf, stride);

            var enc = new JpegBitmapEncoder { QualityLevel = 75 };
            enc.Frames.Add(BitmapFrame.Create(bs));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private static byte[] EmptyJpeg(int dstW, int dstH)
        {
            var wb = new WriteableBitmap(
                dstW, dstH, 96, 96, PixelFormats.Bgr24, null);
            int stride = dstW * 3;
            byte[] black = ArrayPool<byte>.Shared.Rent(dstH * stride);
            Array.Clear(black, 0, dstH * stride);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, dstW, dstH), black, stride, 0);
            var enc = new JpegBitmapEncoder { QualityLevel = 60 };
            enc.Frames.Add(BitmapFrame.Create(wb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            ArrayPool<byte>.Shared.Return(black);
            return ms.ToArray();
        }

        private sealed unsafe class Frame : IDisposable
        {
            // Thay đổi: Chuyển từ property tự động sang field tường minh
            private AVFrame* _ptr;
            public AVFrame* Ptr => _ptr;

            public Frame()
            {
                _ptr = ffmpeg.av_frame_alloc();
            }

            public void Dispose()
            {
                if (_ptr != null)
                {
                    fixed (AVFrame** p = &_ptr) ffmpeg.av_frame_free(p);
                    _ptr = null;
                }
            }
        }
        private sealed unsafe class Packet : IDisposable
        {
            private AVPacket* _ptr;
            public AVPacket* Ptr => _ptr;

            public Packet()
            {
                _ptr = ffmpeg.av_packet_alloc();
            }

            public void Dispose()
            {
                if (_ptr != null)
                {
                    fixed (AVPacket** q = &_ptr) ffmpeg.av_packet_free(q);
                    _ptr = null;
                }
            }
        }
    }


    internal static class FFErr
    {
        public static void ThrowIfError(this int err)
        {
            if (err < 0) throw new InvalidOperationException(GetErrorString(err));
        }
        public static int ThrowIfConsume(this int err)
        {
            if (err == ffmpeg.AVERROR(ffmpeg.EAGAIN) || err == ffmpeg.AVERROR_EOF) return err;
            if (err < 0) throw new InvalidOperationException(GetErrorString(err));
            return err;
        }
        private static unsafe string GetErrorString(int err)
        {
            const int AV_ERROR_MAX_STRING_SIZE = 64;
            byte* errbuf = stackalloc byte[AV_ERROR_MAX_STRING_SIZE];
            ffmpeg.av_strerror(err, errbuf, (ulong)AV_ERROR_MAX_STRING_SIZE);
            return Marshal.PtrToStringAnsi((IntPtr)errbuf) ?? $"fferr {err}";
        }
    }
}