using Microsoft.Win32;
using subphimv1.Helpers;
using subphimv1.Services; // Thêm using này
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace subphimv1
{

    public static class AntiDebug
    {
        private static Timer _timer;
        private static readonly string[] SuspiciousProcesses =
        {
            "ollydbg", "x64dbg", "x32dbg", "dnspy", "ilspy",
            "ida", "ida64", "idag", "idag64", "idaw", "idaw64", "idau", "idau64",
            "scylla", "scylla_x64", "scylla_x86",
            "protection_id", "protectionid",
            "windbg", "kd", "dbgclr", " ImmunityDebugger", "debuggernsm",
            "cheatengine", "cheatengine-i386", "cheatengine-x86_64",
            "httpdebugger", "httpdebuggerui", "httpdebuggerpro",
            "fiddler", "processhacker", "megadumper", "reclass", "charles"
        };
        private const string BanRegistryPath = @"Software\Security";
        private const string BanRegistryKey = "ID";

        private static Timer? _antiDebugTimer;
        private static bool _isActionTaken = false;
        private static readonly object _lockObject = new object();

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength, ref int returnLength);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsDebuggerPresent();
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] ref bool isDebuggerPresent);

        /// <summary>
        /// Khởi tạo và bắt đầu các chu trình kiểm tra anti-debug.
        /// </summary>
        /// <param name="checkIntervalMilliseconds">Khoảng thời gian giữa các lần kiểm tra (mili giây).</param>
        public static void Initialize(int checkIntervalMilliseconds)
        {
            _timer = new Timer(
                callback: state => CheckForDebugger(),
                state: null,
                dueTime: 0,
                period: checkIntervalMilliseconds
            );
        }
        private static void CheckForDebugger()
        {
            bool isDebugging = false;

            // Kết hợp nhiều phương thức kiểm tra
            if (Debugger.IsAttached) isDebugging = true;
            if (IsDebuggerPresent()) isDebugging = true;

            // Có thể thêm các phương pháp kiểm tra khác ở đây...

            if (isDebugging)
            {
                // Dừng bộ đếm thời gian để tránh gọi lại nhiều lần
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);

                // Thực hiện hành động một cách bất đồng bộ
                HandleDetection();
            }
        }
        private static async void HandleDetection()
        {
            string reason = "Debugger Detected";
            string hwid = HwidHelper.GetHwid();
            _ = ApiService.ReportViolationAsync(reason, hwid);
            await Task.Delay(500);
            Application.Current.Shutdown();
        }
        public static void Dispose()
        {
            _timer?.Dispose();
        }


        private static void RunChecks()
        {
            lock (_lockObject)
            {
                if (_isActionTaken) return;

                // Kiểm tra ban một lần nữa trong timer
                if (IsHWIDBanned())
                {
                    _isActionTaken = true;
                    StopTimer();
                    Environment.Exit(0);
                    return;
                }

                // Chạy các kiểm tra khác
                if (IsDebuggingDetected() || IsSuspiciousProcessRunning())
                {
                    _isActionTaken = true;
                    StopTimer();

                    BanCurrentHWID();
                    SelfDestruct();
                    Environment.Exit(0);
                    return;
                }
            }
        }

        private static void StopTimer()
        {
            _antiDebugTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _antiDebugTimer?.Dispose();
            _antiDebugTimer = null;
        }

        private static bool IsDebuggingDetected()
        {
            if (Debugger.IsAttached) return true;

            try
            {
                int isDebugged = 0;
                int returnLength = 0;
                // ProcessDebugPort (7)
                int status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 7, ref isDebugged, sizeof(int), ref returnLength);
                if (status == 0 && isDebugged != 0) return true;
            }
            catch { /* Bỏ qua nếu có lỗi */ }

            try
            {
                bool isDebuggerPresent = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebuggerPresent);
                if (isDebuggerPresent) return true;
            }
            catch { /* Bỏ qua nếu có lỗi */ }


            return false;
        }

        private static bool IsSuspiciousProcessRunning()
        {
            var currentProcesses = Process.GetProcesses();
            return currentProcesses.Any(p =>
            {
                try
                {
                    // Lấy tên tiến trình một cách an toàn
                    if (p == null || p.HasExited) return false;
                    string processName = p.ProcessName.ToLowerInvariant();
                    return SuspiciousProcesses.Any(s => processName.Contains(s));
                }
                catch { /* Bỏ qua các lỗi truy cập hoặc tiến trình đã thoát */ }
                return false;
            });
        }

        private static void SelfDestruct()
        {
            try
            {
                string appPath = Process.GetCurrentProcess().MainModule.FileName;
                string batPath = Path.Combine(Path.GetTempPath(), "delself_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bat");

                // File batch sẽ chờ 2 giây, xóa file exe, rồi tự xóa chính nó
                string script = $@"
@echo off
chcp 65001 > nul
echo Dang cho ung dung dong...
timeout /t 2 /nobreak > nul
echo Dang xoa file thuc thi...
del ""{appPath}""
echo Tu huy...
del ""%~f0""
";
                File.WriteAllText(batPath, script, Encoding.UTF8);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = batPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true // Dùng ShellExecute để chạy độc lập
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
            }
        }

        private static string GetHWID()
        {
            // Kết hợp nhiều thông tin hơn để HWID khó giả mạo hơn
            string uniqueIdentifier = $"{GetIdentifier("Win32_Processor", "ProcessorId")}-{GetIdentifier("Win32_BaseBoard", "SerialNumber")}-{GetIdentifier("Win32_DiskDrive", "SerialNumber")}";

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(uniqueIdentifier));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string GetIdentifier(string wmiClass, string wmiProperty)
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT {wmiProperty} FROM {wmiClass}");
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (mo[wmiProperty] != null)
                        return mo[wmiProperty].ToString() ?? "";
                }
            }
            catch { /* Bỏ qua lỗi WMI */ }
            return "unknown";
        }

        private static void BanCurrentHWID()
        {
            try
            {
                string hwid = GetHWID();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BanRegistryPath, true))
                {
                    key?.SetValue(BanRegistryKey, hwid, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private static bool IsHWIDBanned()
        {
            try
            {
                string currentHwid = GetHWID();
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(BanRegistryPath, false))
                {
                    if (key != null)
                    {
                        string? savedHwid = key.GetValue(BanRegistryKey) as string;
                        return !string.IsNullOrEmpty(savedHwid) && savedHwid.Equals(currentHwid, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return false;
        }
    }

}