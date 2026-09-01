using subphimv1.Subphim;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace subphimv1.Models
{

    public enum SubtitleDisplayMode
    {
        Original,
        Translated
    }
    public class ProjectState
    {
        // ==== Blur settings (preview + export) ====
        public enum BlurApplyMode { None, PerSubtitle, AllTime }

        public BlurApplyMode BlurMode { get; set; } = BlurApplyMode.None;

        /// <summary>
        /// Khung mờ chuẩn hoá theo Player (X,Y,Width,Height) trong [0..1].
        /// X,Y là toạ độ tâm trái-trên, Width/Height là tỉ lệ theo kích thước khung hình player.
        /// </summary>
        public System.Windows.Rect? BlurRectNormalized { get; set; } = null;

        /// <summary>
        /// Cường độ mờ mặc định để preview WPF (0..100). Ánh xạ gần với giá trị tối đa ffmpeg (boxblur / gblur).
        /// </summary>
        public double BlurPreviewRadius { get; set; } = 50.0;

        public SubtitleDisplayMode SelectedSubtitleDisplayMode { get; set; } = SubtitleDisplayMode.Original;
        public string ProjectName { get; set; } = "Untitled Project";
        public List<MediaAsset> TimelineMediaClips { get; set; } = new List<MediaAsset>();
        public string VideoPath { get; set; } = "";
        public double VsfCropTop { get; set; } = 0.7;
        public double VsfCropBottom { get; set; } = 0.9;
        public double VsfCropLeft { get; set; } = 0.1;
        public double VsfCropRight { get; set; } = 0.9;
        public double ProjectReferenceVideoWidth { get; set; } = 1280;
        public double ProjectReferenceVideoHeight { get; set; } = 720;
        public List<TimelineAudioClip> VoicedSubtitles { get; set; } = new List<TimelineAudioClip>();
        public List<SrtSubtitleLine> Subtitles { get; set; } = new List<SrtSubtitleLine>();
        public double TemplateX { get; set; } = 0.5;
        public double TemplateY { get; set; } = 0.9;
        public double TemplateScaleX { get; set; } = 0.8;
        public double TemplateScaleY { get; set; } = 0.8;
        public double TemplateRotation { get; set; } = 0;
        public double? TemplateWidth { get; set; } = null;
        public List<SrtSubtitleLine> TextClips { get; set; } = new List<SrtSubtitleLine>();
        public double TemplateOpacity { get; set; } = 1.0;
        // Font
        public string TemplateFontFamily { get; set; } = "Arial";
        public double TemplateFontSize { get; set; } = 60;
        public string TemplateFontColor { get; set; } = Colors.White.ToString();
        public int TemplateFontWeight { get; set; } = 400;
        public bool TemplateIsItalic { get; set; } = false;
        public bool TemplateIsUnderlined { get; set; } = false;

        public double TemplateCharacterSpacing { get; set; } = 0;
        public int TemplateAlignment { get; set; } = 0;
        // Phông nền
        public bool IsBackgroundEnabled { get; set; } = false;
        public string TemplateBackgroundColor { get; set; } = "#80000000";
        public double TemplateBackgroundPaddingX { get; set; } = 30; // Đệm ngang (trái/phải)
        public double TemplateBackgroundPaddingY { get; set; } = 10;
        public double TemplateBackgroundCornerRadius { get; set; } = 10;
        // MỚI: Độ mờ cho phông nền
        public double TemplateBackgroundOpacity { get; set; } = 0.5;

        // Viền
        public bool IsOutlineEnabled { get; set; } = false;
        public string TemplateOutlineColor { get; set; } = Colors.Black.ToString();
        public double TemplateOutlineThickness { get; set; } = 2;

        // Bóng
        public bool IsShadowEnabled { get; set; } = false;
        public string TemplateShadowColor { get; set; } = "#B3000000";
        public double TemplateShadowBlur { get; set; } = 5;
        public double TemplateShadowDepth { get; set; } = 5;
        public double TemplateShadowDirection { get; set; } = 315;

        public StyleState GetTemplateAsStyleState()
        {
            var style = new StyleState
            {
                FontFamilyName = this.TemplateFontFamily,
                FontSize = this.TemplateFontSize,
                FontColorHex = this.TemplateFontColor,
                FontWeightValue = this.TemplateFontWeight,
                IsItalic = this.TemplateIsItalic,
                IsUnderlined = this.TemplateIsUnderlined,
                CharacterSpacing = this.TemplateCharacterSpacing,
                Opacity = this.TemplateOpacity,
                Alignment = this.TemplateAlignment,

                // Nền
                IsBackgroundEnabled = this.IsBackgroundEnabled,
                BackgroundColorHex = this.TemplateBackgroundColor,
                BackgroundPaddingX = this.TemplateBackgroundPaddingX,
                BackgroundPaddingY = this.TemplateBackgroundPaddingY,
                BackgroundCornerRadius = this.TemplateBackgroundCornerRadius,


                // Viền
                OutlineColorHex = this.TemplateOutlineColor,
                OutlineThickness = this.TemplateOutlineThickness,

                // Bóng
                ShadowColorHex = this.TemplateShadowColor,
                ShadowBlur = this.TemplateShadowBlur,
                ShadowDepth = this.TemplateShadowDepth,
                ShadowDirection = this.TemplateShadowDirection,

                X = this.TemplateX,
                Y = this.TemplateY,
                ScaleX = this.TemplateScaleX,
                ScaleY = this.TemplateScaleY,
                Rotation = this.TemplateRotation,
                Width = this.TemplateWidth
            };
            if (this.IsBackgroundEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.None;
            }
            else if (this.IsOutlineEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.Outline;
            }
            else if (this.IsShadowEnabled)
            {
                style.EdgeStyle = TextEdgeStyle.Shadow;
            }
            else
            {
                style.EdgeStyle = TextEdgeStyle.None;
            }

            return style;
        }
    }
}