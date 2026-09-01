using subphimv1.Models;
using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace subphimv1.Services
{
    public static class ExportDebugger
    {
        public static void LogWpfStateForDebug(
    string videoPath,
    VideoExportSettings exportSettings,
    ProjectState projectState,
    IEnumerable<SrtSubtitleLine> subtitles,
    double previewGridActualWidth,
    double previewGridActualHeight,
    double scaleFactor)
        {
            Debug.WriteLine("[DEBUGGER] Bắt đầu ghi log trạng thái WPF...");
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"WPF_Export_Debug_Log_{timestamp}.txt";
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);

            sb.AppendLine("===================================================");
            sb.AppendLine($"  WPF EXPORT STATE LOG - {DateTime.Now}");
            sb.AppendLine("===================================================");
            sb.AppendLine();

            // --- THÔNG SỐ CHUNG ---
            sb.AppendLine("--- 1) GENERAL ---");
            sb.AppendLine($"Video Path: {videoPath}");
            sb.AppendLine($"Export Resolution: {exportSettings.ResolutionWidth}x{exportSettings.ResolutionHeight}");
            sb.AppendLine($"Preview Grid Actual Size: {previewGridActualWidth:F2} x {previewGridActualHeight:F2}");
            sb.AppendLine($"ScaleFactor (PlayResY/PreviewHeight): {scaleFactor:F6}");
            sb.AppendLine();

            // --- TEMPLATE / GLOBAL ---
            sb.AppendLine("--- 2) PROJECT TEMPLATE (Global Defaults) ---");
            sb.AppendLine($"TemplateFontFamily: {projectState.TemplateFontFamily}");
            sb.AppendLine($"TemplateFontSize: {projectState.TemplateFontSize}");
            sb.AppendLine($"TemplateCharacterSpacing: {projectState.TemplateCharacterSpacing}");
            sb.AppendLine($"TemplateFontColor: {projectState.TemplateFontColor}");
            sb.AppendLine($"TemplateOpacity: {SafeProp(projectState, "TemplateOpacity")}");
            sb.AppendLine($"IsBackgroundEnabled: {projectState.IsBackgroundEnabled}");
            sb.AppendLine($"TemplateBackgroundColor: {projectState.TemplateBackgroundColor}");
            sb.AppendLine($"TemplateBackgroundPaddingX: {projectState.TemplateBackgroundPaddingX}");
            sb.AppendLine($"TemplateBackgroundPaddingY: {projectState.TemplateBackgroundPaddingY}");
            sb.AppendLine($"TemplateBackgroundCornerRadius: {SafeProp(projectState, "TemplateBackgroundCornerRadius")}");
            sb.AppendLine($"IsOutlineEnabled: {projectState.IsOutlineEnabled}");
            sb.AppendLine($"TemplateOutlineColor: {projectState.TemplateOutlineColor}");
            sb.AppendLine($"TemplateOutlineThickness: {projectState.TemplateOutlineThickness}");
            sb.AppendLine($"IsShadowEnabled: {projectState.IsShadowEnabled}");
            sb.AppendLine($"TemplateShadowColor: {projectState.TemplateShadowColor}");
            sb.AppendLine($"TemplateShadowBlur: {projectState.TemplateShadowBlur}");
            sb.AppendLine($"TemplateShadowDepth: {projectState.TemplateShadowDepth}");
            sb.AppendLine($"TemplateShadowDirection: {projectState.TemplateShadowDirection}");
            sb.AppendLine();

            // --- PER-LINE ---
            sb.AppendLine("--- 3) PER-LINE (Final values used for rendering) ---");
            var ordered = (subtitles ?? Enumerable.Empty<SrtSubtitleLine>()).OrderBy(s => s.Index).ToList();
            foreach (var sub in ordered)
            {
                sb.AppendLine("----------------------------------------");
                sb.AppendLine($"  Subtitle #{sub.Index} | {sub.TimeCode}");
                sb.AppendLine("----------------------------------------");

                string rawText = !string.IsNullOrWhiteSpace(sub.TranslatedText) ? sub.TranslatedText : sub.OriginalText;
                string textWithMarkers = (rawText ?? string.Empty);
                bool hasManualBreak = textWithMarkers.Contains(@"\N") || textWithMarkers.Contains("\n") || textWithMarkers.Contains("\r");
                sb.AppendLine($"Text (raw, \\N kept): {textWithMarkers.Replace(Environment.NewLine, "\\N")}");

                var st = sub.Style ?? new StyleState();

                // ---- Position/Transform ----
                double px = st.X * exportSettings.ResolutionWidth;
                double py = st.Y * exportSettings.ResolutionHeight;
                sb.AppendLine("  [Position & Transform]");
                sb.AppendLine($"    Xrel: {st.X:F6}, Yrel: {st.Y:F6}  ->  Xpx: {px:F2}, Ypx: {py:F2}");
                sb.AppendLine($"    ScaleX: {st.ScaleX:F4}, ScaleY: {st.ScaleY:F4}, Rotation: {st.Rotation:F2}");

                // ---- Layout/Wrap từ adorner ----
                double fixedBoxW_preview = st.FixedTextBoxWidth; // giá trị gốc ở Preview px
                bool allowAutoWrap = st.AllowAutoWrap;
                double effWrapW_preview = fixedBoxW_preview > 1
                    ? Math.Max(0, fixedBoxW_preview - (st.BackgroundPaddingX * 2.0 * st.ScaleX))
                    : 0;
                // Quy đổi sang PlayRes px để đo cùng đơn vị với FontSize*scaleFactor
                double effWrapW_playres = effWrapW_preview > 0 ? effWrapW_preview * scaleFactor : 0;

                sb.AppendLine("  [Layout & Wrap]");
                sb.AppendLine($"    FixedTextBoxWidth: preview={fixedBoxW_preview:F2} px  |  AllowAutoWrap: {allowAutoWrap}");
                sb.AppendLine($"    EffectiveWrapWidth: preview={effWrapW_preview:F2} px  |  playres={effWrapW_playres:F2} px");
                sb.AppendLine($"    ContainsManualBreak(\\N/newline): {hasManualBreak}");

                // ---- Đo text theo WPF (đơn vị PlayRes) ----
                var measured = MeasureTextForLog(textWithMarkers, st, scaleFactor, effWrapW_playres /* đã quy đổi */);
                sb.AppendLine("  [Measured Text (WPF, PlayRes px)]");
                sb.AppendLine($"    MeasuredTextWidthPx : {measured.textWidthPx:F2}");
                sb.AppendLine($"    MeasuredTextHeightPx: {measured.textHeightPx:F2}");
                sb.AppendLine($"    Lines (split by \\n) : {measured.lineCount}");

                // ---- Padding & Nền (đơn vị PlayRes) ----
                double padXRaw = st.BackgroundPaddingX;
                double padYRaw = st.BackgroundPaddingY;
                double padXFinal = padXRaw * scaleFactor;
                double padYFinal = padYRaw * scaleFactor;
                double textWScaled = measured.textWidthPx * st.ScaleX;
                double textHScaled = measured.textHeightPx * st.ScaleY;
                double rectW = textWScaled + (padXFinal * 2.0);
                double rectH = textHScaled + (padYFinal * 2.0);
                double left = px - rectW / 2.0;
                double top = py - rectH / 2.0;
                double rx = st.BackgroundCornerRadius * st.ScaleX * scaleFactor;
                double ry = st.BackgroundCornerRadius * st.ScaleY * scaleFactor;

                sb.AppendLine("  [Background Calculation]");
                sb.AppendLine($"    Enabled: {st.IsBackgroundEnabled}");
                sb.AppendLine($"    BG Color: {st.BackgroundColorHex}");
                sb.AppendLine($"    Padding (Raw Style): PadX={padXRaw:F2}, PadY={padYRaw:F2}");
                sb.AppendLine($"    Padding (Final Px at PlayRes): padXFinal={padXFinal:F2}, padYFinal={padYFinal:F2}");
                sb.AppendLine($"    Scaled Text (PlayRes): textWScaled={textWScaled:F2}, textHScaled={textHScaled:F2}");
                sb.AppendLine($"    RectW = {rectW:F2}  |  RectH = {rectH:F2}");
                sb.AppendLine($"    Rect Left = {left:F2}  |  Rect Top = {top:F2}");
                sb.AppendLine($"    CornerRadius (Final Px): rx={rx:F2}, ry={ry:F2}");
                sb.AppendLine();
            }

            AppendPaddingReport(sb, scaleFactor);
            TryWrite(sb.ToString(), filePath, "[DEBUGGER] Đã ghi log trạng thái WPF thành công vào: ");
        }


        private static readonly object _padLock = new object();
        private class PaddingSample
        {
            public int Index;
            public string Text;
            public double PadX, PadY;
            public double ScaleX, ScaleY;
            public double ContentW;           // bề rộng chữ (ActualWidth của control chữ)
            public double InnerPaddingLR;     // padding trái+phải (preview px, chưa scale)
            public double PreviewNoScaleW;    // ContentW + PaddingLR
            public double PreviewScaledW;     // PreviewNoScaleW * ScaleX
            public double RenderSizeW;        // outerBorder.RenderSize.Width (chưa transform)
            public double TransformedW;       // width sau TransformToAncestor(canvas)
            public double FixedBoxW;          // khung cố định (nếu có)
            public bool BackgroundEnabled;
        }
        private static readonly Dictionary<int, PaddingSample> _paddingSamples = new Dictionary<int, PaddingSample>();

        public static void PaddingProbeClear()
        {
            lock (_padLock) _paddingSamples.Clear();
        }

        public static void PaddingProbeAdd(
            int index, string text,
            double padX, double padY,
            double scaleX, double scaleY,
            double contentW, double innerPaddingLR,
            double previewNoScaleW, double previewScaledW,
            double renderSizeW, double transformedW,
            double fixedBoxW, bool backgroundEnabled)
        {
            var sample = new PaddingSample
            {
                Index = index,
                Text = text ?? "",
                PadX = padX,
                PadY = padY,
                ScaleX = scaleX,
                ScaleY = scaleY,
                ContentW = contentW,
                InnerPaddingLR = innerPaddingLR,
                PreviewNoScaleW = previewNoScaleW,
                PreviewScaledW = previewScaledW,
                RenderSizeW = renderSizeW,
                TransformedW = transformedW,
                FixedBoxW = fixedBoxW,
                BackgroundEnabled = backgroundEnabled
            };
            lock (_padLock) _paddingSamples[index] = sample;
        }

        private static string TrimForLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", "⏎");
            return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
        }

        private static void AppendPaddingReport(StringBuilder sb, double scaleFactor)
        {
            sb.AppendLine();
            sb.AppendLine("=== 4) WPF PREVIEW METRICS vs PREDICTED (Padding) ===");
            lock (_padLock)
            {
                foreach (var kv in _paddingSamples.OrderBy(k => k.Key))
                {
                    var m = kv.Value;
                    double playResScaledW = m.PreviewScaledW * scaleFactor;
                    sb.AppendLine($"# {m.Index}  text='{TrimForLog(m.Text)}'");
                    sb.AppendLine($"   contentW={m.ContentW:F2}  padLR={m.InnerPaddingLR:F2}  previewNoScaleW={m.PreviewNoScaleW:F2}");
                    sb.AppendLine($"   scaleX={m.ScaleX:F4}  -> previewScaledW={m.PreviewScaledW:F2}  renderSizeW={m.RenderSizeW:F2}  transformedW={m.TransformedW:F2}");
                    sb.AppendLine($"   → playResScaledW={playResScaledW:F2}  comp: padX={m.PadX:F2}, padY={m.PadY:F2}, bgOn={m.BackgroundEnabled}  fixedBoxW={m.FixedBoxW:F2}");
                    sb.AppendLine();
                }
            }
        }


        public static void LogAssConversionForDebug(string assContent, int playResX, int playResY, double scaleFactor)
        {
            WriteAssLogInternal(assContent, playResX, playResY, scaleFactor, null, null, null, null, null);
        }
        public static void LogAssConversionForDebug(
            string assContent,
            int playResX,
            int playResY,
            double scaleFactor,
            string ffmpegExePath,
            string ffmpegArgs,
            string subtitleFilterLine,
            string fontsDirUsed,
            string tempAssPath)
        {
            WriteAssLogInternal(assContent, playResX, playResY, scaleFactor, ffmpegExePath, ffmpegArgs, subtitleFilterLine, fontsDirUsed, tempAssPath);
        }

        private static void WriteAssLogInternal(
            string assContent,
            int playResX,
            int playResY,
            double scaleFactor,
            string ffmpegExePath,
            string ffmpegArgs,
            string subtitleFilterLine,
            string fontsDirUsed,
            string tempAssPath)
        {
            Debug.WriteLine("[DEBUGGER] Bắt đầu ghi log chuyển đổi ASS...");
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"ASS_Conversion_Debug_Log_{timestamp}.txt";
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);

            sb.AppendLine("===================================================");
            sb.AppendLine($"  ASS CONVERSION LOG - {DateTime.Now}");
            sb.AppendLine("===================================================");
            sb.AppendLine();

            sb.AppendLine("--- INPUT PARAMETERS ---");
            sb.AppendLine($"PlayResX: {playResX}");
            sb.AppendLine($"PlayResY: {playResY}");
            sb.AppendLine($"ScaleFactor: {scaleFactor:F6}");
            if (!string.IsNullOrWhiteSpace(tempAssPath)) sb.AppendLine($"Temp ASS Path: {tempAssPath}");
            if (!string.IsNullOrWhiteSpace(fontsDirUsed)) sb.AppendLine($"fontsdir (libass): {fontsDirUsed}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(ffmpegExePath) || !string.IsNullOrWhiteSpace(ffmpegArgs))
            {
                sb.AppendLine("--- FFMPEG INVOCATION ---");
                if (!string.IsNullOrWhiteSpace(ffmpegExePath)) sb.AppendLine($"ffmpeg.exe: {ffmpegExePath}");
                if (!string.IsNullOrWhiteSpace(ffmpegArgs)) sb.AppendLine($"args: {ffmpegArgs}");
                if (!string.IsNullOrWhiteSpace(subtitleFilterLine)) sb.AppendLine($"subtitles filter: {subtitleFilterLine}");
                sb.AppendLine();
            }
            var (wrapStyle, scaledBS, styleCount) = ParseAssHeaderQuick(assContent ?? string.Empty);
            sb.AppendLine("--- PARSED ASS HEADER QUICK VIEW ---");
            sb.AppendLine($"WrapStyle: {wrapStyle}");
            sb.AppendLine($"ScaledBorderAndShadow: {scaledBS}");
            sb.AppendLine($"#Styles: {styleCount}");
            sb.AppendLine();

            sb.AppendLine("===================================================");
            sb.AppendLine("            GENERATED .ASS CONTENT");
            sb.AppendLine("===================================================");
            sb.AppendLine();
            sb.Append(assContent ?? string.Empty);

            TryWrite(sb.ToString(), filePath, "[DEBUGGER] Đã ghi log chuyển đổi ASS thành công vào: ");
        }

        private static void TryWrite(string content, string filePath, string okPrefix)
        {
            try
            {
                File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Debug.WriteLine(okPrefix + filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DEBUGGER] LỖI khi ghi file: {ex.Message}");
            }
        }

        private static string SafeProp(object obj, string propName)
        {
            try
            {
                if (obj == null) return "N/A";
                var p = obj.GetType().GetProperty(propName);
                if (p == null) return "N/A";
                var v = p.GetValue(obj);
                return v == null ? "null" : v.ToString();
            }
            catch { return "N/A"; }
        }

        private static (int assAA, string BBGGRR) ComputeAssAlphaAndBBGGRR(string hex, double opacityFactor)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                double aHex = c.A / 255.0;
                double aFinal = Math.Max(0, Math.Min(1, opacityFactor)) * aHex;
                byte assAA = (byte)(255 - Math.Round(aFinal * 255.0));
                string bbggrr = $"{c.B:X2}{c.G:X2}{c.R:X2}";
                return (assAA, bbggrr);
            }
            catch
            {
                return (0x00, "FFFFFF");
            }
        }
        private static (double textWidthPx, double textHeightPx, int lineCount) MeasureTextForLog(
            string textWithMarkers,
            StyleState st,
            double scaleFactor,
            double effectiveWrapWidthPx)
        {
            string plain = (textWithMarkers ?? string.Empty)
                .Replace("\\r", "\n")
                .Replace("\\N", "\n");
            var typeface = new Typeface(
                new FontFamily(st.FontFamilyName ?? "Arial"),
                st.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                st.FontWeightValue > 500 ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            double sizePx = st.FontSize * scaleFactor;
            if (sizePx <= 0) sizePx = 1;

            var ft = new FormattedText(
                plain,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                sizePx,
                Brushes.Black,
                1.0
            );

            if (effectiveWrapWidthPx > 1)
                ft.MaxTextWidth = effectiveWrapWidthPx;

            double width = ft.WidthIncludingTrailingWhitespace;
            double height = ft.Height;
            if (Math.Abs(st.CharacterSpacing) > 0.01)
            {
                int chars = plain.Replace("\n", "").Length;
                width += Math.Max(0, chars - 1) * (st.CharacterSpacing * scaleFactor);
            }

            int lineCount = Math.Max(1, plain.Split(new[] { '\n' }).Length);
            return (width, height, lineCount);
        }

        private static (string wrapStyle, string scaledBorderShadow, int styleCount) ParseAssHeaderQuick(string ass)
        {
            string wrap = "";
            string scaled = "";
            int styles = 0;

            using (var sr = new StringReader(ass ?? string.Empty))
            {
                string? line;
                bool inStyles = false;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("WrapStyle", StringComparison.OrdinalIgnoreCase))
                        wrap = line.Split(':').LastOrDefault()?.Trim() ?? "";
                    else if (line.StartsWith("ScaledBorderAndShadow", StringComparison.OrdinalIgnoreCase))
                        scaled = line.Split(':').LastOrDefault()?.Trim() ?? "";
                    else if (line.StartsWith("[V4+ Styles]"))
                        inStyles = true;
                    else if (inStyles && line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
                        styles++;
                    else if (inStyles && line.StartsWith("["))
                        inStyles = false;
                }
            }
            return (wrap, scaled, styles);
        }
    }
}