// VỊ TRÍ: subphimv1/Services/TempFileManager.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace subphimv1.Services
{
    public static class TempFileManager
    {
        private static readonly List<string> _sessionFiles = new List<string>();
        private static readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "SubphimV1");
        private static readonly string _tempDirectory;
        private static readonly List<string> _tempFiles = new List<string>();
        private static readonly List<string> _tempDirectories = new List<string>();
        private static readonly object _lock = new object();

        static TempFileManager()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "AIOSubPhim_temp_files");
        }

        public static void CleanupPreviousSessionFiles()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    var files = Directory.GetFiles(_tempDirectory);
                    var dirs = Directory.GetDirectories(_tempDirectory);

                    foreach (string file in files)
                    {
                        File.Delete(file);
                    }
                    foreach (string dir in dirs)
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (Exception)
                {
                }
            }
            else
            {
                Directory.CreateDirectory(_tempDirectory);
            }
        }
        public static string CreateTempFile(string extension)
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }
            string tempFilePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString() + extension);

            lock (_lock)
            {
                _tempFiles.Add(tempFilePath);
            }
            return tempFilePath;
        }
        public static string CreateTempDirectory()
        {
            string tempDirPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirPath);
            lock (_lock)
            {
                _tempDirectories.Add(tempDirPath);
            }
            return tempDirPath;
        }
        public static void RegisterForCleanup(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && !_sessionFiles.Contains(filePath))
            {
                _sessionFiles.Add(filePath);
            }
        }
        public static void CleanupCurrentSessionFiles()
        {
            lock (_lock)
            {
                foreach (var file in _tempFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                _tempFiles.Clear();
                foreach (var dir in _tempDirectories)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                _tempDirectories.Clear();
            }
        }
    }
}