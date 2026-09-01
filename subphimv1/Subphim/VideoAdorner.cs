using subphimv1.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace subphimv1.Subphim
{
    public class VideoAdorner : Adorner
    {
        public event Action<double, double> DragDelta;
        public event Action<double> ScaleChanged;
        public event Action DragStarted;
        public event Action DragCompleted;

        private bool _isDraggingSelf = false;
        private Point _lastMousePosition;
        private readonly UIElement _referenceElement;
        private bool _isScaling = false;
        private Point _scaleCenter;
        private double _initialScale;
        private Vector _startVector;

        private readonly Thumb _topLeft, _topRight, _bottomLeft, _bottomRight;
        private readonly VisualCollection _visualChildren;
        private readonly Pen _borderPen = new Pen(Brushes.White, 6.0);
        private readonly Pen _snappedPen = new Pen(Brushes.LimeGreen, 6.0);
        private const double EdgeSnapTolerance = 2.0;
        private const double HandleSize = 25.0;
        private readonly MainWindow _mainWindow;
        public VideoAdorner(UIElement adornedElement, UIElement referenceElement, MainWindow mainWindow)
            : base(adornedElement)
        {
            _referenceElement = referenceElement ?? throw new ArgumentNullException(nameof(referenceElement));
            _mainWindow = mainWindow ?? Application.Current?.MainWindow as MainWindow
                          ?? throw new ArgumentNullException(nameof(mainWindow));

            _visualChildren = new VisualCollection(this);
            _topLeft = CreateCornerThumb(Cursors.SizeNWSE);
            _topRight = CreateCornerThumb(Cursors.SizeNESW);
            _bottomLeft = CreateCornerThumb(Cursors.SizeNESW);
            _bottomRight = CreateCornerThumb(Cursors.SizeNWSE);
            _topLeft.DragStarted += Corner_DragStarted;
            _topRight.DragStarted += Corner_DragStarted;
            _bottomLeft.DragStarted += Corner_DragStarted;
            _bottomRight.DragStarted += Corner_DragStarted;

            _topLeft.DragDelta += Corner_DragDelta;
            _topRight.DragDelta += Corner_DragDelta;
            _bottomLeft.DragDelta += Corner_DragDelta;
            _bottomRight.DragDelta += Corner_DragDelta;

            this.MouseLeftButtonDown += Move_MouseDown;
            this.MouseMove += Move_MouseMove;
            this.MouseLeftButtonUp += Move_MouseUp;
        }
        public VideoAdorner(UIElement adornedElement, UIElement referenceElement)
            : this(adornedElement, referenceElement, Application.Current?.MainWindow as MainWindow) { }
        protected override HitTestResult HitTestCore(PointHitTestParameters parameters)
        {
            var pt = parameters.HitPoint;
            var videoRect = GetVideoContentRect();
            if (videoRect.IsEmpty || !videoRect.Contains(pt))
                return null;
            try
            {
                if (_mainWindow?.SubtitleRenderCanvas != null)
                {
                    var tx = this.TransformToVisual(_mainWindow.SubtitleRenderCanvas);
                    if (tx != null && tx.TryTransform(pt, out var ptOnCanvas))
                    {
                        var hit = _mainWindow.SubtitleRenderCanvas.InputHitTest(ptOnCanvas) as DependencyObject;
                        if (hit != null)
                        {
                            if (IsOverlayElement(hit) ||
                                hit is System.Windows.Controls.Primitives.Thumb ||
                                FindVisualParent<System.Windows.Controls.Primitives.Thumb>(hit) != null)
                            {
                                return null;
                            }
                        }
                    }
                }
            }
            catch {}
            return new PointHitTestResult(this, parameters.HitPoint);
        }

        private Rect GetVideoSafeAreaRect()
        {
            var asset = DataContext as MediaAsset;
            var container = this.AdornedElement as FrameworkElement;

            if (asset == null || asset.Width <= 0 || asset.Height <= 0 || container == null ||
                container.RenderSize.Width <= 0 || container.RenderSize.Height <= 0)
            {
                return Rect.Empty;
            }

            double containerWidth = container.RenderSize.Width;
            double containerHeight = container.RenderSize.Height;

            double videoAspectRatio = (double)asset.Width / asset.Height;
            double containerAspectRatio = containerWidth / containerHeight;

            double finalWidth, finalHeight, x, y;

            if (containerAspectRatio > videoAspectRatio)
            {
                finalHeight = containerHeight;
                finalWidth = finalHeight * videoAspectRatio;
                x = (containerWidth - finalWidth) / 2;
                y = 0;
            }
            else
            {
                finalWidth = containerWidth;
                finalHeight = finalWidth / videoAspectRatio;
                x = 0;
                y = (containerHeight - finalHeight) / 2;
            }

            return new Rect(x, y, finalWidth, finalHeight);
        }
        private static bool IsOverlayElement(DependencyObject d)
        {
            while (d != null)
            {
                if (d is FrameworkElement fe)
                {
                    // Gia cố: nếu là vùng blur preview mình tự đánh dấu, cũng coi là overlay
                    if (fe.Uid == "BLUR_PREVIEW")
                        return true;

                    var dc = fe.DataContext;
                    if (dc != null)
                    {
                        string typeName = dc.GetType().Name;
                        if (typeName.Contains("SrtSubtitleLine") || typeName.Contains("MediaAsset"))
                            return true;
                    }
                }
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }


        #region Pan (Di chuyển) Logic
        private void Move_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mainWindow?.SubtitleRenderCanvas != null)
            {
                var tx = this.TransformToVisual(_mainWindow.SubtitleRenderCanvas);
                if (tx != null && tx.TryTransform(e.GetPosition(this), out var ptOnCanvas))
                {
                    var hit = _mainWindow.SubtitleRenderCanvas.InputHitTest(ptOnCanvas) as DependencyObject;
                    if (hit != null &&
                        (IsOverlayElement(hit) ||
                         hit is System.Windows.Controls.Primitives.Thumb ||
                         FindVisualParent<System.Windows.Controls.Primitives.Thumb>(hit) != null))
                    {
                        return;
                    }
                }
            }

            if (e.Source == this)
            {
                _isDraggingSelf = true;
                _lastMousePosition = e.GetPosition(_referenceElement);
                CaptureMouse();
                DragStarted?.Invoke();
                e.Handled = true;
            }
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null)
            {
                return parent;
            }
            else
            {
                return FindVisualParent<T>(parentObject);
            }
        }
        private void Move_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSelf)
            {
                Point cur = e.GetPosition(_referenceElement);
                double dX = cur.X - _lastMousePosition.X;
                double dY = cur.Y - _lastMousePosition.Y;
                if (Math.Abs(dX) > 0 || Math.Abs(dY) > 0)
                {
                    DragDelta?.Invoke(dX, dY);
                    _lastMousePosition = cur;
                }
            }
        }

        private void Move_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSelf)
            {
                _isDraggingSelf = false;
                this.ReleaseMouseCapture();
                DragCompleted?.Invoke();
                e.Handled = true;
            }
        }
        #endregion

        #region Scale (Phóng to/Thu nhỏ) Logic
        private void Corner_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (AdornedElement is FrameworkElement element)
            {
                _isScaling = true;
                _scaleCenter = new Point(_referenceElement.RenderSize.Width / 2, _referenceElement.RenderSize.Height / 2);
                _startVector = Point.Subtract(Mouse.GetPosition(_referenceElement), _scaleCenter);

                if (DataContext is MediaAsset asset)
                {
                    _initialScale = asset.Scale;
                }
                else
                {
                    _initialScale = 1.0;
                }
                DragStarted?.Invoke();
            }
        }

        private void Corner_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_isScaling && AdornedElement is FrameworkElement element && _startVector.Length > 0)
            {
                var currentVector = Point.Subtract(Mouse.GetPosition(_referenceElement), _scaleCenter);
                double scaleFactor = currentVector.Length / _startVector.Length;
                double newScale = _initialScale * scaleFactor;
                newScale = Math.Clamp(newScale, 0.1, 5.0);

                newScale = Math.Round(newScale, 2);
                ScaleChanged?.Invoke(newScale);
            }
        }
        private void DragCompleted_Handler(object sender, DragCompletedEventArgs e)
        {
            _isScaling = false;
            DragCompleted?.Invoke();
        }
        #endregion
        private ScaleTransform GetPlayerZoomTransform()
        {
            if (_referenceElement is FrameworkElement fe && fe.RenderTransform is TransformGroup tg)
            {
                return tg.Children.OfType<ScaleTransform>().FirstOrDefault();
            }
            return null;
        }
        private Rect GetVideoContentRect()
        {
            var asset = DataContext as MediaAsset;
            var container = this.AdornedElement as FrameworkElement;

            if (asset == null || asset.Width <= 0 || asset.Height <= 0 || container == null ||
                container.RenderSize.Width <= 0 || container.RenderSize.Height <= 0)
            {
                return Rect.Empty;
            }

            double containerWidth = container.RenderSize.Width;
            double containerHeight = container.RenderSize.Height;

            double videoAspectRatio = (double)asset.Width / asset.Height;
            double containerAspectRatio = containerWidth / containerHeight;

            double finalWidth, finalHeight, x, y;

            if (containerAspectRatio > videoAspectRatio)
            {
                finalHeight = containerHeight;
                finalWidth = finalHeight * videoAspectRatio;
                x = (containerWidth - finalWidth) / 2;
                y = 0;
            }
            else
            {
                finalWidth = containerWidth;
                finalHeight = finalWidth / videoAspectRatio;
                x = 0;
                y = (containerHeight - finalHeight) / 2;
            }
            double assetScaleX = asset?.ScaleX ?? 1.0;
            double assetScaleY = asset?.ScaleY ?? 1.0;
            double positionX = asset?.PositionX ?? 0.5;
            double positionY = asset?.PositionY ?? 0.5;

            double w = finalWidth * assetScaleX;
            double h = finalHeight * assetScaleY;

            double cx = x + finalWidth / 2 + (positionX - 0.5) * finalWidth;
            double cy = y + finalHeight / 2 + (positionY - 0.5) * finalHeight;

            return new Rect(cx - w / 2, cy - h / 2, w, h);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_mainWindow?.PlayerClipArea == null) return;
            var clipBounds = new Rect(_mainWindow.PlayerClipArea.RenderSize);
            var transform = _mainWindow.PlayerClipArea.TransformToVisual(this.AdornedElement);
            var clipRect = transform.TransformBounds(clipBounds);
            var clipGeometry = new RectangleGeometry(clipRect);
            drawingContext.PushClip(clipGeometry);

            try
            {
                Rect videoContentRect = GetVideoContentRect();
                if (videoContentRect.IsEmpty) return;
                Rect safeAreaRect = GetVideoSafeAreaRect();
                if (safeAreaRect.IsEmpty) return;

                var playerZoom = GetPlayerZoomTransform();
                double playerScale = Math.Min(playerZoom?.ScaleX ?? 1.0, playerZoom?.ScaleY ?? 1.0);
                double compensatedThickness = _borderPen.Thickness / playerScale;
                var compensatedPen = new Pen(_borderPen.Brush, compensatedThickness);
                var compensatedSnappedPen = new Pen(_snappedPen.Brush, compensatedThickness * 1.5);

                bool highlightEnabled = videoContentRect.Width > safeAreaRect.Width + EdgeSnapTolerance ||
                                        videoContentRect.Height > safeAreaRect.Height + EdgeSnapTolerance;
                if (highlightEnabled)
                {
                    bool isTopSnapped = Math.Abs(videoContentRect.Top - safeAreaRect.Top) < EdgeSnapTolerance;
                    bool isBottomSnapped = Math.Abs(videoContentRect.Bottom - safeAreaRect.Bottom) < EdgeSnapTolerance;
                    bool isLeftSnapped = Math.Abs(videoContentRect.Left - safeAreaRect.Left) < EdgeSnapTolerance;
                    bool isRightSnapped = Math.Abs(videoContentRect.Right - safeAreaRect.Right) < EdgeSnapTolerance;
                    drawingContext.DrawLine(isTopSnapped ? compensatedSnappedPen : compensatedPen, videoContentRect.TopLeft, videoContentRect.TopRight);
                    drawingContext.DrawLine(isRightSnapped ? compensatedSnappedPen : compensatedPen, videoContentRect.TopRight, videoContentRect.BottomRight);
                    drawingContext.DrawLine(isBottomSnapped ? compensatedSnappedPen : compensatedPen, videoContentRect.BottomRight, videoContentRect.BottomLeft);
                    drawingContext.DrawLine(isLeftSnapped ? compensatedSnappedPen : compensatedPen, videoContentRect.BottomLeft, videoContentRect.TopLeft);
                }
                else
                {
                    drawingContext.DrawRectangle(Brushes.Transparent, compensatedPen, videoContentRect);
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }
        private Thumb CreateCornerThumb(Cursor cursor)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                Width = HandleSize,
                Height = HandleSize,
            };
            var template = new ControlTemplate(typeof(Thumb));
            var ellipseFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            ellipseFactory.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);

            template.VisualTree = ellipseFactory;
            thumb.Template = template;

            _visualChildren.Add(thumb);
            return thumb;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_mainWindow?.PlayerClipArea == null || !this.AdornedElement.IsVisible)
            {
                _topLeft.Visibility = Visibility.Collapsed;
                _topRight.Visibility = Visibility.Collapsed;
                _bottomLeft.Visibility = Visibility.Collapsed;
                _bottomRight.Visibility = Visibility.Collapsed;
                return base.ArrangeOverride(finalSize);
            }

            Rect videoContentRect = GetVideoContentRect();
            if (videoContentRect.IsEmpty)
            {
                _topLeft.Visibility = Visibility.Collapsed;
                _topRight.Visibility = Visibility.Collapsed;
                _bottomLeft.Visibility = Visibility.Collapsed;
                _bottomRight.Visibility = Visibility.Collapsed;
                return finalSize;
            }
            var playerZoom = GetPlayerZoomTransform();
            double playerScaleX = playerZoom?.ScaleX ?? 1.0;
            double playerScaleY = playerZoom?.ScaleY ?? 1.0;
            var inverseTransform = new ScaleTransform(1.0 / playerScaleX, 1.0 / playerScaleY);
            double halfHandleW = HandleSize / 2;
            double halfHandleH = HandleSize / 2;
            var topLeftRect = new Rect(new Point(videoContentRect.Left - halfHandleW, videoContentRect.Top - halfHandleH), new Size(HandleSize, HandleSize));
            _topLeft.Arrange(topLeftRect);
            _topLeft.RenderTransform = inverseTransform;

            var topRightRect = new Rect(new Point(videoContentRect.Right - halfHandleW, videoContentRect.Top - halfHandleH), new Size(HandleSize, HandleSize));
            _topRight.Arrange(topRightRect);
            _topRight.RenderTransform = inverseTransform;

            var bottomLeftRect = new Rect(new Point(videoContentRect.Left - halfHandleW, videoContentRect.Bottom - halfHandleH), new Size(HandleSize, HandleSize));
            _bottomLeft.Arrange(bottomLeftRect);
            _bottomLeft.RenderTransform = inverseTransform;

            var bottomRightRect = new Rect(new Point(videoContentRect.Right - halfHandleW, videoContentRect.Bottom - halfHandleH), new Size(HandleSize, HandleSize));
            _bottomRight.Arrange(bottomRightRect);
            _bottomRight.RenderTransform = inverseTransform;
            var clipBounds = new Rect(_mainWindow.PlayerClipArea.RenderSize);
            var transform = _mainWindow.PlayerClipArea.TransformToVisual(this.AdornedElement);
            var clipRectInAdornerSpace = transform.TransformBounds(clipBounds);
            _topLeft.Visibility = clipRectInAdornerSpace.IntersectsWith(topLeftRect) ? Visibility.Visible : Visibility.Collapsed;
            _topRight.Visibility = clipRectInAdornerSpace.IntersectsWith(topRightRect) ? Visibility.Visible : Visibility.Collapsed;
            _bottomLeft.Visibility = clipRectInAdornerSpace.IntersectsWith(bottomLeftRect) ? Visibility.Visible : Visibility.Collapsed;
            _bottomRight.Visibility = clipRectInAdornerSpace.IntersectsWith(bottomRightRect) ? Visibility.Visible : Visibility.Collapsed;

            return finalSize;
        }
        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];
    }
}