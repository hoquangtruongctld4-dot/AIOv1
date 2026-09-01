using System;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace subphimv1.Models
{

    public class StyleState
    {

        public double X { get; set; } = 0.5;
        public double Y { get; set; } = 0.9;
        public double? Width { get; set; } = null;
        public double ScaleX { get; set; } = 0.8;
        public double ScaleY { get; set; } = 0.8;
        public double Rotation { get; set; } = 0;
        public string FontFamilyName { get; set; } = "Arial";
        public double FontSize { get; set; } = 60;
        public string FontColorHex { get; set; } = "#FFFFFFFF";
        public int FontWeightValue { get; set; } = 400;
        public bool IsItalic { get; set; } = false;
        public bool IsUnderlined { get; set; } = false;
        public double CharacterSpacing { get; set; } = 0;
        public double FixedTextBoxWidth { get; set; } = 0;
        public bool AllowAutoWrap { get; set; } = true;
        public int Alignment { get; set; } = 0;
        public bool IsBackgroundEnabled { get; set; } = false;
        public string BackgroundColorHex { get; set; } = "#80000000";
        public double BackgroundCornerRadius { get; set; } = 10;
        public double BackgroundPaddingX { get; set; } = 30;
        public double BackgroundPaddingY { get; set; } = 10;
        public TextEdgeStyle EdgeStyle { get; set; } = TextEdgeStyle.Outline;
        public string OutlineColorHex { get; set; } = "#FF000000";
        public double OutlineThickness { get; set; } = 2;
        public string ShadowColorHex { get; set; } = "#B3000000";
        public double ShadowBlur { get; set; } = 5;
        public double ShadowDepth { get; set; } = 5;
        public double ShadowDirection { get; set; } = 315;

        public double Opacity { get; set; } = 1.0;
        public StyleState Clone()
        {
            return (StyleState)this.MemberwiseClone();
        }
    }
}