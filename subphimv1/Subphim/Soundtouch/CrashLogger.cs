using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

public static class CrashLogger
{
    // Cờ chống tái nhập theo thread. Nếu đang log mà lại phát sinh FirstChance, ta bỏ qua để tránh đệ quy.
    [ThreadStatic]
    private static bool _inLogger;

    private static readonly object _sync = new object();
    private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ERROR_LOG.txt");

    /// <summary>
    /// Ghi một dòng log an toàn, không ném ngoại lệ ra ngoài, có chống tái nhập.
    /// </summary>
    public static void Write(string message,
        [CallerMemberName] string member = null,
        [CallerFilePath] string file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!BeginLog()) return;
        try
        {
            string fileName = SafeGetFileName(file);
            string lineText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] {fileName}:{line} {member} | {message}";
            SafeAppend(lineText);
        }
        finally
        {
            EndLog();
        }
    }

    /// <summary>
    /// Ghi ngoại lệ đầy đủ stack trace, an toàn.
    /// </summary>
    public static void Write(Exception ex,
        [CallerMemberName] string member = null,
        [CallerFilePath] string file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!BeginLog()) return;
        try
        {
            string fileName = SafeGetFileName(file);
            string lineText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] {fileName}:{line} {member} | EX: {ex}";
            SafeAppend(lineText);
        }
        finally
        {
            EndLog();
        }
    }

    /// <summary>
    /// Mở cổng log cho thread hiện tại. Ngăn log lồng nhau khi FirstChance nổ trong lúc ghi file.
    /// </summary>
    private static bool BeginLog()
    {
        if (_inLogger) return false; // đang ở trong logger → bỏ qua để tránh đệ quy
        _inLogger = true;
        return true;
    }

    private static void EndLog()
    {
        _inLogger = false;
    }

    private static string SafeGetFileName(string path)
    {
        try { return Path.GetFileName(path); } catch { return "(unknown)"; }
    }

    /// <summary>
    /// Ghi nối vào file với chia sẻ đọc/ghi, có khóa liên tiến trình, có retry ngắn hạn. Tuyệt đối không gọi Debug.WriteLine ở đây.
    /// </summary>
    private static void SafeAppend(string line)
    {
        // Khóa tiến trình hiện tại để ghép dòng cho liền mạch.
        lock (_sync)
        {
            // Thử ghi tối đa 3 lần để né đụng độ handle tạm thời.
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // FileShare.ReadWrite để không khóa cứng file nếu nơi khác cũng đọc.
                    using (var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    {
                        sw.WriteLine(line);
                        sw.Flush();
                        return; // thành công
                    }
                }
                catch
                {
                    // Không ném ngoại lệ. Tránh kích hoạt FirstChance lặp.
                    if (attempt < maxAttempts)
                    {
                        // Backoff rất ngắn để nhường I/O
                        Thread.Sleep(5);
                        continue;
                    }
                    // Bỏ cuộc lặng lẽ ở lần cuối.
                    return;
                }
            }
        }
    }
}
