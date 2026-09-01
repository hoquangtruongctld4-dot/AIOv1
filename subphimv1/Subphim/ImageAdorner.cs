using subphimv1.Models;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace subphimv1.Subphim
{
    public class ImageAdorner : Adorner
    {
        public event Action<double, double> DragDelta;
        public event Action<double> ScaleChanged;
        public event Action RotationStarted;
        public event Action RotationCompleted;
        public event Action DragStarted;
        public event Action DragCompleted;
        public event Action<double> RotationChanged;

        private bool _isDraggingSelf = false;
        private Point _lastMousePosition;
        private readonly UIElement _referenceElement;
        private bool _isScaling = false;
        private bool _isRotating = false;
        private Point _rotateCenter;
        private double _initialAngle;
        private Point _scaleCenter;
        private double _initialScale;
        private Vector _startVector;
        public ProjectState CurrentProject { get; set; }
        private readonly Thumb _topLeft, _topRight, _bottomLeft, _bottomRight, _rotateThumb;
        private readonly VisualCollection _visualChildren;
        private readonly MainWindow _mainWindow;
        private readonly Pen _borderPen = new Pen(Brushes.White, 3.0);
        private const double HandleSize = 18.0;
        private const double RotateHandleSize = 50.0;

        public ImageAdorner(UIElement adornedElement, UIElement referenceElement, ProjectState project, MainWindow mainWindow)
            : base(adornedElement)
        {
            CurrentProject = project ?? throw new ArgumentNullException(nameof(project));
            _mainWindow = mainWindow ?? Application.Current?.MainWindow as MainWindow
                          ?? throw new ArgumentNullException(nameof(mainWindow));
            _referenceElement = referenceElement ?? throw new ArgumentNullException(nameof(referenceElement));
            _visualChildren = new VisualCollection(this);
            _topLeft = CreateCornerThumb(Cursors.SizeNWSE);
            _topRight = CreateCornerThumb(Cursors.SizeNESW);
            _bottomLeft = CreateCornerThumb(Cursors.SizeNESW);
            _bottomRight = CreateCornerThumb(Cursors.SizeNWSE);
            _rotateThumb = CreateRotateThumb();
            _topLeft.DragStarted += Corner_DragStarted;
            _topRight.DragStarted += Corner_DragStarted;
            _bottomLeft.DragStarted += Corner_DragStarted;
            _bottomRight.DragStarted += Corner_DragStarted;
            _topLeft.DragDelta += Corner_DragDelta;
            _topRight.DragDelta += Corner_DragDelta;
            _bottomLeft.DragDelta += Corner_DragDelta;
            _bottomRight.DragDelta += Corner_DragDelta;
            _topLeft.DragCompleted += DragCompleted_Handler;
            _topRight.DragCompleted += DragCompleted_Handler;
            _bottomLeft.DragCompleted += DragCompleted_Handler;
            _bottomRight.DragCompleted += DragCompleted_Handler;
            _rotateThumb.DragStarted += Rotate_DragStarted;
            _rotateThumb.DragDelta += Rotate_DragDelta;
            _rotateThumb.DragCompleted += DragCompleted_Handler;
            this.MouseLeftButtonDown += Move_MouseDown;
            this.MouseMove += Move_MouseMove;
            this.MouseLeftButtonUp += Move_MouseUp;
        }
        #region Pan (Di chuyển) Logic
        private void Rotate_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isRotating = true;
            var r = GetImageContentRect();
            if (r.IsEmpty)
            {
                _isRotating = false;
                return;
            }
            _rotateCenter = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            Point mousePos = Mouse.GetPosition(this);
            _startVector = Point.Subtract(mousePos, _rotateCenter);
            if (DataContext is MediaAsset asset)
            {
                _initialAngle = asset.Rotation;
            }
            else
            {
                _initialAngle = 0;
            }
            DragStarted?.Invoke();
            RotationStarted?.Invoke();
            e.Handled = true;
        }
        private Thumb CreateRotateThumb()
        {
            var thumb = new Thumb
            {
                Cursor = Cursors.Hand,
                Width = RotateHandleSize,
                Height = RotateHandleSize,
            };
            var template = new ControlTemplate(typeof(Thumb));
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            var ellipseFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            ellipseFactory.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
            gridFactory.AppendChild(ellipseFactory);
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetValue(TextBlock.TextProperty, "\uE895"); // Icon xoay
            textBlockFactory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            textBlockFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textBlockFactory.SetValue(TextBlock.FontSizeProperty, RotateHandleSize * 0.6); // Kích thước icon
            textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            gridFactory.AppendChild(textBlockFactory);

            template.VisualTree = gridFactory;
            thumb.Template = template;

            _visualChildren.Add(thumb);
            return thumb;
        }
        private void Rotate_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_isRotating) return; 
            if (!_isRotating || _startVector.Length < 0.001)
            {
                return;
            }
            Vector currentVector = Point.Subtract(Mouse.GetPosition(this), _rotateCenter);
            if (currentVector.Length < 0.001) return;
            double angleStart = Math.Atan2(_startVector.Y, _startVector.X);
            double angleCurrent = Math.Atan2(currentVector.Y, currentVector.X);
            double angleDeltaDegrees = (angleCurrent - angleStart) * 180.0 / Math.PI;
            double newAngle = _initialAngle + angleDeltaDegrees;
            newAngle = (newAngle % 360.0 + 360.0) % 360.0;
            RotationChanged?.Invoke(Math.Round(newAngle, 2));
            e.Handled = true;
        }
        private Point _startMouseNorm;
        private Point _startAssetPos;

        private Rect GetVideoFrameOnPlayerRect()
        {
            var project = this.CurrentProject;
            var container = this.AdornedElement as FrameworkElement;
            double referenceWidth = project.ProjectReferenceVideoWidth;
            double referenceHeight = project.ProjectReferenceVideoHeight;
            double containerWidth = container.RenderSize.Width;
            double containerHeight = container.RenderSize.Height;

            double refAR = referenceWidth / referenceHeight;
            double contAR = containerWidth / containerHeight;

            double W, H, X, Y;
            if (contAR > refAR) { H = containerHeight; W = H * refAR; X = (containerWidth - W) / 2; Y = 0; }
            else { W = containerWidth; H = W / refAR; X = 0; Y = (containerHeight - H) / 2; }
            return new Rect(X, Y, W, H);
        }
        protected override HitTestResult HitTestCore(PointHitTestParameters parameters)
        {
            try
            {
                var pt = parameters.HitPoint;
                var contentRect = GetImageContentRect();
                if (contentRect.IsEmpty || !contentRect.Contains(pt))
                    return null;
                if (_mainWindow?.SubtitleRenderCanvas != null)
                {
                    var tx = this.TransformToVisual(_mainWindow.SubtitleRenderCanvas);
                    if (tx != null && tx.TryTransform(pt, out var p))
                    {
                        var hit = _mainWindow.SubtitleRenderCanvas.InputHitTest(p) as DependencyObject;
                        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                        {
                            if (d is FrameworkElement fe)
                            {
                                var dc = fe.DataContext;
                                if (dc is SrtSubtitleLine) return null;
                                if (dc is MediaAsset other && !ReferenceEquals(other, this.DataContext)) return null;
                            }
                        }
                    }
                }
            }
            catch { }
            return new PointHitTestResult(this, parameters.HitPoint);
        }
        private Point ToNormalizedVideo(Point pInRef)
        {
            Rect vf = GetVideoFrameOnPlayerRect();
            double x = (pInRef.X - vf.X) / vf.Width;
            double y = (pInRef.Y - vf.Y) / vf.Height;
            x = Math.Clamp(x, 0.0, 1.0);
            y = Math.Clamp(y, 0.0, 1.0);
            return new Point(x, y);
        }
        private (double normW, double normH) GetFinalImageSizeNormalized()
        {
            Rect vf = GetVideoFrameOnPlayerRect();
            Rect r = GetImageContentRect();
            if (vf.Width <= 0 || vf.Height <= 0 || r.IsEmpty) return (0, 0);
            return (r.Width / vf.Width, r.Height / vf.Height);
        }
        private void Move_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(this);
            if (!GetImageContentRect().Contains(pt)) return;
            if (_mainWindow?.SubtitleRenderCanvas != null)
            {
                var tx = this.TransformToVisual(_mainWindow.SubtitleRenderCanvas);
                if (tx != null && tx.TryTransform(pt, out var p))
                {
                    var hit = _mainWindow.SubtitleRenderCanvas.InputHitTest(p) as DependencyObject;
                    var fe = FindVisualParent<FrameworkElement>(hit);
                    if (fe?.DataContext is SrtSubtitleLine ||
                        (fe?.DataContext is MediaAsset ma && ma != this.DataContext))
                    {
                        return;
                    }
                }
            }
            if (e.Source == this)
            {
                _isDraggingSelf = true;
                _startMouseNorm = ToNormalizedVideo(e.GetPosition(_referenceElement));
                if (DataContext is MediaAsset asset)
                {
                    _startAssetPos = new Point(asset.PositionX, asset.PositionY);
                }
                this.CaptureMouse();
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
            if (_isDraggingSelf && DataContext is subphimv1.Subphim.MediaAsset asset)
            {
                Point curMouseNorm = ToNormalizedVideo(e.GetPosition(_referenceElement));
                double dX = curMouseNorm.X - _startMouseNorm.X;
                double dY = curMouseNorm.Y - _startMouseNorm.Y;
                double newX = _startAssetPos.X + dX;
                double newY = _startAssetPos.Y + dY;
                var (nW, nH) = GetFinalImageSizeNormalized();
                double halfW = nW * 0.5;
                double halfH = nH * 0.5;
                if (halfW > 0 && halfH > 0)
                {
                    newX = Math.Clamp(newX, halfW, 1.0 - halfW);
                    newY = Math.Clamp(newY, halfH, 1.0 - halfH);
                }
                else
                {
                    newX = Math.Clamp(newX, 0.0, 1.0);
                    newY = Math.Clamp(newY, 0.0, 1.0);
                }
                if (!double.IsNaN(newX) && !double.IsNaN(newY))
                {
                    asset.PositionX = newX;
                    asset.PositionY = newY;
                    InvalidateVisual();
                }
                DragDelta?.Invoke(dX, dY);
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
                var r = GetImageContentRect();
                if (r.IsEmpty)
                {
                    _isScaling = false;
                    return;
                }

                _scaleCenter = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                Point mousePos = Mouse.GetPosition(this);
                _startVector = Point.Subtract(mousePos, _scaleCenter);
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
            if (_isScaling && AdornedElement is FrameworkElement element && _startVector.Length > 0.001)
            {
                Point mousePos = Mouse.GetPosition(this);
                var currentVector = Point.Subtract(mousePos, _scaleCenter);
                if (currentVector.Length < 0.001)
                {
                    return;
                }

                double scaleFactor = currentVector.Length / _startVector.Length;
                double newScale = _initialScale * scaleFactor;
                newScale = Math.Clamp(newScale, 0.1, 5.0);
                newScale = Math.Round(newScale, 2);
                ScaleChanged?.Invoke(newScale);
            }
            else {}
        }
        private void DragCompleted_Handler(object sender, DragCompletedEventArgs e)
        {
            if (_isRotating)
            {
                RotationCompleted?.Invoke();
            }
            _isScaling = false;
            _isRotating = false;
            DragCompleted?.Invoke();
        }
        #endregion
        private Rect GetImageContentRect()
        {
            var asset = DataContext as MediaAsset;
            var container = this.AdornedElement as FrameworkElement;
            var project = this.CurrentProject;
            if (asset == null || container == null || project == null) return Rect.Empty;
            if (asset.Width <= 0 || asset.Height <= 0) return Rect.Empty;
            if (project.ProjectReferenceVideoWidth <= 0 || project.ProjectReferenceVideoHeight <= 0) return Rect.Empty;
            if (container.RenderSize.Width <= 0 || container.RenderSize.Height <= 0) return Rect.Empty;
            double referenceWidth = project.ProjectReferenceVideoWidth;
            double referenceHeight = project.ProjectReferenceVideoHeight;
            double containerWidth = container.RenderSize.Width;
            double containerHeight = container.RenderSize.Height;
            double referenceAspectRatio = referenceWidth / referenceHeight;
            double containerAspectRatio = containerWidth / containerHeight;
            double finalVideoFrameWidth, finalVideoFrameHeight, videoFrameX, videoFrameY;
            if (containerAspectRatio > referenceAspectRatio)
            {
                finalVideoFrameHeight = containerHeight;
                finalVideoFrameWidth = finalVideoFrameHeight * referenceAspectRatio;
                videoFrameX = (containerWidth - finalVideoFrameWidth) / 2;
                videoFrameY = 0;
            }
            else
            {
                finalVideoFrameWidth = containerWidth;
                finalVideoFrameHeight = finalVideoFrameWidth / referenceAspectRatio;
                videoFrameX = 0;
                videoFrameY = (containerHeight - finalVideoFrameHeight) / 2;
            }

            double imageAspectRatio = (double)asset.Width / asset.Height;
            double imageInitialWidth, imageInitialHeight;
            if (finalVideoFrameWidth / finalVideoFrameHeight > imageAspectRatio)
            {
                imageInitialHeight = finalVideoFrameHeight;
                imageInitialWidth = imageInitialHeight * imageAspectRatio;
            }
            else
            {
                imageInitialWidth = finalVideoFrameWidth;
                imageInitialHeight = imageInitialWidth / imageAspectRatio;
            }
            double scaleX = Math.Max(asset.ScaleX, 0.0001);
            double scaleY = Math.Max(asset.ScaleY, 0.0001);
            double positionX = asset.PositionX;
            double positionY = asset.PositionY;
            double finalWidth = imageInitialWidth * scaleX;
            double finalHeight = imageInitialHeight * scaleY;
            double centerX = videoFrameX + (positionX * finalVideoFrameWidth);
            double centerY = videoFrameY + (positionY * finalVideoFrameHeight);
            double finalLeft = centerX - finalWidth / 2;
            double finalTop = centerY - finalHeight / 2;
            return new Rect(finalLeft, finalTop, finalWidth, finalHeight);
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            Rect contentRect = GetImageContentRect();
            if (contentRect.IsEmpty) return;
            var asset = DataContext as MediaAsset;
            double rotationAngle = asset?.Rotation ?? 0.0;
            var rotateTransform = new RotateTransform(rotationAngle, contentRect.X + contentRect.Width / 2, contentRect.Y + contentRect.Height / 2);
            Point topLeft = rotateTransform.Transform(contentRect.TopLeft);
            Point topRight = rotateTransform.Transform(contentRect.TopRight);
            Point bottomLeft = rotateTransform.Transform(contentRect.BottomLeft);
            Point bottomRight = rotateTransform.Transform(contentRect.BottomRight);
            drawingContext.DrawLine(_borderPen, topLeft, topRight);
            drawingContext.DrawLine(_borderPen, topRight, bottomRight);
            drawingContext.DrawLine(_borderPen, bottomRight, bottomLeft);
            drawingContext.DrawLine(_borderPen, bottomLeft, topLeft);
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
            Rect r = GetImageContentRect();
            if (r.IsEmpty)
            {
                _topLeft.Arrange(new Rect());
                _topRight.Arrange(new Rect());
                _bottomLeft.Arrange(new Rect());
                _bottomRight.Arrange(new Rect());
                _rotateThumb.Arrange(new Rect());
                return finalSize;
            }
            var asset = DataContext as MediaAsset;
            double rotationAngle = asset?.Rotation ?? 0.0;
            var rotateTransform = new RotateTransform(rotationAngle, r.X + r.Width / 2, r.Y + r.Height / 2);
            Point topLeft = rotateTransform.Transform(r.TopLeft);
            Point topRight = rotateTransform.Transform(r.TopRight);
            Point bottomLeft = rotateTransform.Transform(r.BottomLeft);
            Point bottomRight = rotateTransform.Transform(r.BottomRight);
            _topLeft.Arrange(new Rect(topLeft.X - HandleSize / 2, topLeft.Y - HandleSize / 2, HandleSize, HandleSize));
            _topRight.Arrange(new Rect(topRight.X - HandleSize / 2, topRight.Y - HandleSize / 2, HandleSize, HandleSize));
            _bottomLeft.Arrange(new Rect(bottomLeft.X - HandleSize / 2, bottomLeft.Y - HandleSize / 2, HandleSize, HandleSize));
            _bottomRight.Arrange(new Rect(bottomRight.X - HandleSize / 2, bottomRight.Y - HandleSize / 2, HandleSize, HandleSize));
            Point bottomCenter = new Point(r.Left + r.Width / 2, r.Bottom);
            Point rotatedBottomCenter = rotateTransform.Transform(bottomCenter);
            var direction = rotatedBottomCenter - rotateTransform.Transform(new Point(r.Left + r.Width / 2, r.Top));
            direction.Normalize();
            double handleOffset = RotateHandleSize / 2 + 15;
            Point rotateHandleCenter = rotatedBottomCenter + direction * handleOffset;
            _rotateThumb.Arrange(new Rect(rotateHandleCenter.X - RotateHandleSize / 2, rotateHandleCenter.Y - RotateHandleSize / 2, RotateHandleSize, RotateHandleSize));

            return finalSize;
        }
        protected override int VisualChildrenCount => 5;
        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visualChildren.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _visualChildren[index];
        }
    }
}