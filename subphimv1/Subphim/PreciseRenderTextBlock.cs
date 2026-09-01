using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace subphimv1.Subphim
{
    public class PreciseRenderTextBlock : FrameworkElement
    {
        private readonly List<FormattedText> _characterFormatTexts = new();
        private double _maxBaseline;      // cache baseline lớn nhất
        private double _maxHeight;        // cache chiều cao dòng

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(string.Empty,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty CharacterSpacingProperty =
            DependencyProperty.Register(nameof(CharacterSpacing), typeof(double), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(0.0,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(new FontFamily("Arial"),
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(12.0,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty FontStyleProperty =
            DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(FontStyles.Normal,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(FontWeights.Normal,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty ForegroundProperty =
            DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(Brushes.Black,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public static readonly DependencyProperty TextDecorationsProperty =
            DependencyProperty.Register(nameof(TextDecorations), typeof(TextDecorationCollection), typeof(PreciseRenderTextBlock),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFormattedTextInvalidated));

        public new static readonly DependencyProperty EffectProperty = UIElement.EffectProperty.AddOwner(typeof(PreciseRenderTextBlock));

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty); set => SetValue(CharacterSpacingProperty, value); }
        public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
        public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public FontStyle FontStyle { get => (FontStyle)GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }
        public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public TextDecorationCollection TextDecorations { get => (TextDecorationCollection)GetValue(TextDecorationsProperty); set => SetValue(TextDecorationsProperty, value); }
        public new Effect Effect { get => (Effect)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }

        private static void OnFormattedTextInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PreciseRenderTextBlock)d;
            control._characterFormatTexts.Clear();
            control._maxBaseline = 0;
            control._maxHeight = 0;
            control.InvalidateMeasure();
            control.InvalidateVisual();
        }

        private void EnsureFormattedTextIsCreated()
        {
            if (_characterFormatTexts.Count > 0 || string.IsNullOrEmpty(Text)) return;

            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            foreach (char c in Text)
            {
                var ft = new FormattedText(
                    c.ToString(),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontSize,
                    Foreground,
                    ppd);

                if (TextDecorations != null)
                    ft.SetTextDecorations(TextDecorations);

                _characterFormatTexts.Add(ft);
                if (ft.Baseline > _maxBaseline) _maxBaseline = ft.Baseline;
                if (ft.Height > _maxHeight) _maxHeight = ft.Height;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureFormattedTextIsCreated();
            if (_characterFormatTexts.Count == 0) return new Size(0, 0);

            double totalWidth = 0;
            for (int i = 0; i < _characterFormatTexts.Count; i++)
            {
                totalWidth += _characterFormatTexts[i].WidthIncludingTrailingWhitespace;
                if (i < _characterFormatTexts.Count - 1)
                    totalWidth += CharacterSpacing; // chỉ giữa các ký tự
            }

            return new Size(totalWidth, _maxHeight);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            EnsureFormattedTextIsCreated();
            if (_characterFormatTexts.Count == 0) return;

            double x = 0;
            for (int i = 0; i < _characterFormatTexts.Count; i++)
            {
                var ft = _characterFormatTexts[i];
                // Vẽ tại y = Baseline để đỉnh chữ ở y = 0
                drawingContext.DrawText(ft, new Point(x, _maxBaseline));
                x += ft.WidthIncludingTrailingWhitespace;
                if (i < _characterFormatTexts.Count - 1)
                    x += CharacterSpacing;
            }
        }
    }
}
