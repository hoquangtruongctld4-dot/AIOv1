using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using subphimv1.Models;

namespace subphimv1.Services
{
    public static class PresetManager
    {
        private static readonly string PresetsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        static PresetManager()
        {
            Directory.CreateDirectory(PresetsFolder);
        }

        public static void SavePreset(ProjectState state, string presetName)
        {
            var presetState = new ProjectState
            {
                ProjectName = presetName,
                // AIOSubPhim Crop
                VsfCropTop = state.VsfCropTop,
                VsfCropBottom = state.VsfCropBottom,
                VsfCropLeft = state.VsfCropLeft,
                VsfCropRight = state.VsfCropRight,
                // Template
                TemplateX = state.TemplateX,
                TemplateY = state.TemplateY,
                TemplateScaleX = state.TemplateScaleX,
                TemplateScaleY = state.TemplateScaleY,
                TemplateRotation = state.TemplateRotation,
                //TemplateWidth = state.TemplateWidth,
                TemplateOpacity = state.TemplateOpacity,
                TemplateFontFamily = state.TemplateFontFamily,
                TemplateFontSize = state.TemplateFontSize,
                TemplateFontColor = state.TemplateFontColor,
                TemplateFontWeight = state.TemplateFontWeight,
                TemplateIsItalic = state.TemplateIsItalic,
                TemplateIsUnderlined = state.TemplateIsUnderlined,
                TemplateCharacterSpacing = state.TemplateCharacterSpacing,
                IsBackgroundEnabled = state.IsBackgroundEnabled,
                TemplateBackgroundColor = state.TemplateBackgroundColor,
                TemplateBackgroundPaddingX = state.TemplateBackgroundPaddingX,
                TemplateBackgroundPaddingY = state.TemplateBackgroundPaddingY,
                TemplateBackgroundCornerRadius = state.TemplateBackgroundCornerRadius,
                TemplateBackgroundOpacity = state.TemplateBackgroundOpacity,
                IsOutlineEnabled = state.IsOutlineEnabled,
                TemplateOutlineColor = state.TemplateOutlineColor,
                TemplateOutlineThickness = state.TemplateOutlineThickness,
                IsShadowEnabled = state.IsShadowEnabled,
                TemplateShadowColor = state.TemplateShadowColor,
                TemplateShadowBlur = state.TemplateShadowBlur,
                TemplateShadowDepth = state.TemplateShadowDepth,
                TemplateShadowDirection = state.TemplateShadowDirection
            };

            string presetFilePath = Path.Combine(PresetsFolder, $"{presetName}.json");
            string jsonString = JsonSerializer.Serialize(presetState, _jsonOptions);
            File.WriteAllText(presetFilePath, jsonString);
        }

        public static ProjectState LoadPreset(string presetName)
        {
            string presetFilePath = Path.Combine(PresetsFolder, $"{presetName}.json");
            if (!File.Exists(presetFilePath))
            {
                return null;
            }
            string jsonString = File.ReadAllText(presetFilePath);
            return JsonSerializer.Deserialize<ProjectState>(jsonString);
        }

        public static List<string> GetAllPresetNames()
        {
            if (!Directory.Exists(PresetsFolder)) return new List<string>();
            return Directory.GetFiles(PresetsFolder, "*.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .ToList();
        }
    }
    public static class ProjectManager
    {
        private static readonly string ProjectsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects");
        private static readonly string LastProjectPathFile = Path.Combine(ProjectsFolder, "last_project.txt");

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        static ProjectManager()
        {
            Directory.CreateDirectory(ProjectsFolder);
        }

        private static string GetProjectFilePath(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = $"Project_{DateTime.Now:yyyyMMdd_HHmmss}";
            }
            return Path.Combine(ProjectsFolder, $"{projectName}.json");
        }

        /// <summary>
        /// Lưu SNAPSHOT (đầy đủ trạng thái phiên làm việc).
        /// </summary>
        public static void SaveSnapshot(EditorSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Directory.CreateDirectory(ProjectsFolder);

            string projectFilePath = GetProjectFilePath(snapshot.Project.ProjectName);
            string jsonString = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(projectFilePath, jsonString);
            File.WriteAllText(LastProjectPathFile, projectFilePath);
        }

        /// <summary>
        /// Lưu SNAPSHOT async.
        /// </summary>
        public static async Task SaveSnapshotAsync(EditorSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Directory.CreateDirectory(ProjectsFolder);

            string projectFilePath = GetProjectFilePath(snapshot.Project.ProjectName);
            string jsonString = JsonSerializer.Serialize(snapshot, _jsonOptions);
            await File.WriteAllTextAsync(projectFilePath, jsonString);
            await File.WriteAllTextAsync(LastProjectPathFile, projectFilePath);
        }

        /// <summary>
        /// LOAD: ưu tiên đọc EditorSnapshot; nếu file cũ (ProjectState) thì bọc vào snapshot.
        /// </summary>
        public static EditorSnapshot LoadSnapshot(string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(projectFilePath);

            // 1) Thử EditorSnapshot
            try
            {
                var snap = JsonSerializer.Deserialize<EditorSnapshot>(json);
                if (snap != null && snap.Project != null)
                {
                    File.WriteAllText(LastProjectPathFile, projectFilePath);
                    return snap;
                }
            }
            catch { }

            // 2) Thử ProjectState (tương thích ngược)
            try
            {
                var legacy = JsonSerializer.Deserialize<ProjectState>(json);
                if (legacy != null)
                {
                    File.WriteAllText(LastProjectPathFile, projectFilePath);
                    return EditorSnapshot.FromLegacyProject(legacy);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Load file gần nhất (nếu có).
        /// </summary>
        public static EditorSnapshot LoadLastSnapshot()
        {
            if (File.Exists(LastProjectPathFile))
            {
                string lastPath = File.ReadAllText(LastProjectPathFile);
                if (File.Exists(lastPath))
                {
                    return LoadSnapshot(lastPath);
                }
            }
            return null;
        }

        /// <summary>
        /// Tạo project mới (snapshot rỗng + ProjectState mặc định).
        /// </summary>
        public static EditorSnapshot CreateNewSnapshot()
        {
            var state = new ProjectState
            {
                ProjectName = $"Untitled Project {DateTime.Now:yyyy-MM-dd HH.mm.ss}"
            };
            return EditorSnapshot.FromLegacyProject(state);
        }

        /// <summary>
        /// Lấy mọi đường dẫn project trong thư mục.
        /// </summary>
        public static List<string> GetAllProjectPaths()
        {
            if (!Directory.Exists(ProjectsFolder)) return new List<string>();
            return Directory.GetFiles(ProjectsFolder, "*.json").ToList();
        }

        /// <summary>
        /// Xóa project theo đường dẫn và cập nhật last_project.txt nếu cần.
        /// </summary>
        public static void DeleteProject(string projectFilePath)
        {
            try
            {
                if (File.Exists(projectFilePath))
                {
                    File.Delete(projectFilePath);
                }
                if (File.Exists(LastProjectPathFile))
                {
                    string lastPath = File.ReadAllText(LastProjectPathFile);
                    if (string.Equals(lastPath, projectFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(LastProjectPathFile);
                    }
                }
            }
            catch { }
        }

        // ========= Giữ API cũ để không vỡ compile (DEPRECATED) =========
        [Obsolete("Hãy dùng SaveSnapshot/SaveSnapshotAsync với EditorSnapshot.")]
        public static void SaveProject(ProjectState state)
        {
            var snap = EditorSnapshot.FromLegacyProject(state);
            SaveSnapshot(snap);
        }

        [Obsolete("Hãy dùng SaveSnapshot/SaveSnapshotAsync với EditorSnapshot.")]
        public static async Task SaveProjectAsync(ProjectState state)
        {
            var snap = EditorSnapshot.FromLegacyProject(state);
            await SaveSnapshotAsync(snap);
        }

        [Obsolete("Dùng CreateNewSnapshot thay cho CreateNewProject")]
        public static ProjectState CreateNewProject()
        {
            return CreateNewSnapshot().Project;
        }
    }
}
