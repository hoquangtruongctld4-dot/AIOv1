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

    public enum ResizeDirection { Left, Right }

    public class ResizeEventArgs : EventArgs
    {
        public ResizeDirection Direction { get; }
        public double HorizontalChange { get; }
        public ResizeEventArgs(ResizeDirection direction, double horizontalChange)
        {
            Direction = direction;
            HorizontalChange = horizontalChange;
        }
    }

    public class SubtitleAdorner : Adorner
    {
        public event Action<ResizeDirection> ResizeStarted;
        public event Action<double> RotationChanged;
        public event Action<double> SizeCommitted;
        public event Action RotationStarted;
        public event Action RotationCompleted;
        public event Action<double, double> DragDelta;
        public event Action<double> UniformScaleDelta;
        public event EventHandler<ResizeEventArgs> Resized;
        public event Action DragCompleted;
        private bool _isDraggingSelf = false;
        private readonly bool _rotationEnabled;
        private bool _isResizing = false;
        private bool _isRotating = false;
        private Point _lastMousePosition;
        private Point _rotateCenterInStableParent;
        private Vector _startVectorRotate; 
        private double _initialAngle; 
        private UIElement _stableParent; 
        private readonly Thumb _topLeft, _topRight, _bottomLeft, _bottomRight, _rotateThumb;
        private readonly Thumb _leftMove, _rightMove;
        private readonly VisualCollection _visualChildren;
        private readonly FrameworkElement _adornedElement;
        private readonly Pen _borderPen = new Pen(Brushes.White, 3.0);
        private const double HandleSize = 18.0;
        private const double RotateHandleSize = 50.0;
        private readonly MainWindow _mainWindow;

        public SubtitleAdorner(FrameworkElement adornedElement, MainWindow mainWindow, bool rotationEnabled = true) : base(adornedElement)
        {
            _mainWindow = mainWindow;
            _adornedElement = adornedElement;
            _visualChildren = new VisualCollection(this);

            // lưu cờ cho phép xoay
            _rotationEnabled = rotationEnabled;

            _topLeft = CreateCornerThumb(Cursors.SizeNWSE);
            _topRight = CreateCornerThumb(Cursors.SizeNESW);
            _bottomLeft = CreateCornerThumb(Cursors.SizeNESW);
            _bottomRight = CreateCornerThumb(Cursors.SizeNWSE);

            _leftMove = CreateSideThumb(Cursors.SizeWE);
            _rightMove = CreateSideThumb(Cursors.SizeWE);

            // Tạo nút xoay như cũ, nhưng sẽ ẩn/disable nếu rotationEnabled == false
            _rotateThumb = CreateRotateThumb(Rotate_DragDelta, DragCompleted_Handler);

            _topLeft.DragDelta += Corner_DragDelta;
            _topRight.DragDelta += Corner_DragDelta;
            _bottomLeft.DragDelta += Corner_DragDelta;
            _bottomRight.DragDelta += Corner_DragDelta;

            _topLeft.DragCompleted += DragCompleted_Handler;
            _topRight.DragCompleted += DragCompleted_Handler;
            _bottomLeft.DragCompleted += DragCompleted_Handler;
            _bottomRight.DragCompleted += DragCompleted_Handler;

            _leftMove.DragDelta += Side_DragDelta;
            _rightMove.DragDelta += Side_DragDelta;
            _leftMove.DragCompleted += DragCompleted_Handler;
            _rightMove.DragCompleted += DragCompleted_Handler;

            _leftMove.DragStarted += (s, e) => { _isResizing = true; ResizeStarted?.Invoke(ResizeDirection.Left); };
            _rightMove.DragStarted += (s, e) => { _isResizing = true; ResizeStarted?.Invoke(ResizeDirection.Right); };

            // Chỉ gắn sự kiện bắt đầu xoay nếu cho phép xoay
            if (_rotationEnabled)
            {
                _rotateThumb.DragStarted += Rotate_DragStarted;
                _rotateThumb.IsHitTestVisible = true;
                _rotateThumb.Visibility = Visibility.Visible;
            }
            else
            {
                // Ẩn và vô hiệu hoá nút xoay cho blur
                _rotateThumb.IsHitTestVisible = false;
                _rotateThumb.Visibility = Visibility.Collapsed;
            }

            this.MouseLeftButtonDown += Move_MouseDown;
            this.MouseMove += Move_MouseMove;
            this.MouseLeftButtonUp += Move_MouseUp;
        }

        private Thumb CreateRotateThumb(DragDeltaEventHandler dragDeltaHandler, DragCompletedEventHandler dragCompletedHandler)
        {
            var thumb = new Thumb
            {
                Cursor = Cursors.Hand,
                Width = RotateHandleSize,
                Height = RotateHandleSize
            };

            var template = new ControlTemplate(typeof(Thumb));
            var gridFactory = new FrameworkElementFactory(typeof(Grid));

            var ellipseFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            ellipseFactory.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
            gridFactory.AppendChild(ellipseFactory);

            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetValue(TextBlock.TextProperty, "\uE895");
            textBlockFactory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            textBlockFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textBlockFactory.SetValue(TextBlock.FontSizeProperty, RotateHandleSize * 0.6);
            textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            gridFactory.AppendChild(textBlockFactory);

            template.VisualTree = gridFactory;
            thumb.Template = template;

            thumb.DragDelta += dragDeltaHandler;
            thumb.DragCompleted += dragCompletedHandler;
            _visualChildren.Add(thumb);
            return thumb;
        }
        private void Rotate_DragStarted(object sender, DragStartedEventArgs e)
        {
            // NEW: chặn xoay nếu không cho phép
            if (!_rotationEnabled)
            {
                e.Handled = true;
                return;
            }

            _isRotating = true;
            _stableParent = VisualTreeHelper.GetParent(this.AdornedElement) as UIElement;
            if (_stableParent == null)
            {
                _isRotating = false;
                return;
            }
            Point centerInSelf = new Point(this.AdornedElement.RenderSize.Width / 2, this.AdornedElement.RenderSize.Height / 2);
            _rotateCenterInStableParent = this.AdornedElement.TransformToAncestor(_stableParent).Transform(centerInSelf);
            Point startMousePos = Mouse.GetPosition(_stableParent);
            _startVectorRotate = Point.Subtract(startMousePos, _rotateCenterInStableParent);
            _initialAngle = 0;
            if (AdornedElement.RenderTransform is TransformGroup tg)
            {
                var rt = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                if (rt != null) _initialAngle = rt.Angle;
            }
            else if (AdornedElement.RenderTransform is RotateTransform rt)
            {
                _initialAngle = rt.Angle;
            }

            RotationStarted?.Invoke();
            e.Handled = true;
        }

        private void Rotate_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // NEW: chặn xoay nếu không cho phép
            if (!_rotationEnabled)
            {
                e.Handled = true;
                return;
            }

            if (!_isRotating || _stableParent == null || _startVectorRotate.Length < 0.001) return;

            Point currentMousePos = Mouse.GetPosition(_stableParent);
            Vector currentVector = Point.Subtract(currentMousePos, _rotateCenterInStableParent);
            if (currentVector.Length < 0.001) return;

            double angleStart = Math.Atan2(_startVectorRotate.Y, _startVectorRotate.X);
            double angleCurrent = Math.Atan2(currentVector.Y, currentVector.X);
            double angleDeltaDegrees = (angleCurrent - angleStart) * 180.0 / Math.PI;

            double newAngle = _initialAngle + angleDeltaDegrees;
            newAngle = (newAngle % 360.0 + 360.0) % 360.0;

            double finalAngle = Math.Round(newAngle, 2);
            RotationChanged?.Invoke(finalAngle);

            e.Handled = true;
        }


        private void Move_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == this)
            {
                var parent = VisualTreeHelper.GetParent(this.AdornedElement) as IInputElement;
                if (parent != null)
                {
                    _isDraggingSelf = true;
                    _lastMousePosition = e.GetPosition(parent);
                    this.CaptureMouse();
                    e.Handled = true;
                }
            }
        }
        private void Move_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSelf)
            {
                var parent = VisualTreeHelper.GetParent(this.AdornedElement) as IInputElement;
                if (parent != null)
                {
                    Point currentPosition = e.GetPosition(parent);
                    double dX = currentPosition.X - _lastMousePosition.X;
                    double dY = currentPosition.Y - _lastMousePosition.Y;
                    if (Math.Abs(dX) > 0 || Math.Abs(dY) > 0)
                    {
                        DragDelta?.Invoke(dX, dY);
                        _lastMousePosition = currentPosition;
                    }
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
        private void Corner_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!(AdornedElement is FrameworkElement adornedElement) || adornedElement.ActualWidth == 0) return;
            var thumb = sender as Thumb;
            double sensitivity = adornedElement.ActualWidth + adornedElement.ActualHeight;
            double scaleDelta = 0.0;
            if (thumb == _bottomRight) scaleDelta = (e.HorizontalChange + e.VerticalChange) / sensitivity;
            else if (thumb == _topLeft) scaleDelta = (-e.HorizontalChange - e.VerticalChange) / sensitivity;
            else if (thumb == _topRight) scaleDelta = (e.HorizontalChange - e.VerticalChange) / sensitivity;
            else if (thumb == _bottomLeft) scaleDelta = (-e.HorizontalChange + e.VerticalChange) / sensitivity;
            UniformScaleDelta?.Invoke(scaleDelta);
            e.Handled = true;
        }
        private void Side_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            if (thumb == _leftMove)
                Resized?.Invoke(this, new ResizeEventArgs(ResizeDirection.Left, e.HorizontalChange));
            else if (thumb == _rightMove)
                Resized?.Invoke(this, new ResizeEventArgs(ResizeDirection.Right, e.HorizontalChange));
            e.Handled = true;
        }
        private void Resize_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isResizing = true;
        }

        private void DragCompleted_Handler(object sender, DragCompletedEventArgs e)
        {
            bool wasRotating = _isRotating;
            bool wasResizing = _isResizing;

            _isRotating = false;
            _isResizing = false;
            _stableParent = null; 

            e.Handled = true;

            if (wasRotating)
                RotationCompleted?.Invoke();

            DragCompleted?.Invoke();

            if (wasResizing && AdornedElement is FrameworkElement fe)
                SizeCommitted?.Invoke(fe.ActualWidth);
        }

        private (double scaleX, double scaleY) GetCurrentScale()
        {
            if (AdornedElement is not FrameworkElement element) return (1.0, 1.0);

            var transform = element.RenderTransform;
            if (transform is ScaleTransform st)
            {
                return (st.ScaleX, st.ScaleY);
            }
            if (transform is TransformGroup tg)
            {
                var scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (scale != null)
                {
                    return (scale.ScaleX, scale.ScaleY);
                }
            }
            return (1.0, 1.0);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var clipBounds = new Rect(_mainWindow.PlayerClipArea.RenderSize);
            var transform = _mainWindow.PlayerClipArea.TransformToVisual(this.AdornedElement);
            var clipRect = transform.TransformBounds(clipBounds);
            var clipGeometry = new RectangleGeometry(clipRect);
            drawingContext.PushClip(clipGeometry);
            try
            {
                var (scaleX, scaleY) = GetCurrentScale();
                var drawingRect = new Rect(this.AdornedElement.RenderSize);
                var compensatedPen = new Pen(_borderPen.Brush, _borderPen.Thickness / Math.Min(scaleX, scaleY));
                drawingContext.DrawRectangle(Brushes.Transparent, compensatedPen, drawingRect);
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

        private Thumb CreateSideThumb(Cursor cursor)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                Width = HandleSize,
                Height = HandleSize
            };
            var template = new ControlTemplate(typeof(Thumb));
            var rectFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
            rectFactory.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
            rectFactory.SetValue(System.Windows.Shapes.Rectangle.WidthProperty, HandleSize * 0.4);
            rectFactory.SetValue(System.Windows.Shapes.Rectangle.HeightProperty, HandleSize);
            rectFactory.SetValue(System.Windows.Shapes.Rectangle.RadiusXProperty, 3.0);
            rectFactory.SetValue(System.Windows.Shapes.Rectangle.RadiusYProperty, 3.0);
            template.VisualTree = rectFactory;
            thumb.Template = template;
            _visualChildren.Add(thumb);
            return thumb;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_mainWindow?.PlayerClipArea == null || !this.AdornedElement.IsVisible)
            {
                return base.ArrangeOverride(finalSize);
            }

            var (scaleX, scaleY) = GetCurrentScale();
            scaleX = Math.Max(0.001, scaleX);
            scaleY = Math.Max(0.001, scaleY);
            var inverseTransform = new ScaleTransform(1.0 / scaleX, 1.0 / scaleY);

            double contentWidth = this.AdornedElement.RenderSize.Width;
            double contentHeight = this.AdornedElement.RenderSize.Height;

            double halfW = HandleSize / 2;
            double halfH = HandleSize / 2;
            double halfRotateW = RotateHandleSize / 2;
            double halfRotateH = RotateHandleSize / 2;

            var topLeftRect = new Rect(new Point(-halfW, -halfH), new Size(HandleSize, HandleSize));
            _topLeft.Arrange(topLeftRect);
            _topLeft.RenderTransform = inverseTransform;

            var topRightRect = new Rect(new Point(contentWidth - halfW, -halfH), new Size(HandleSize, HandleSize));
            _topRight.Arrange(topRightRect);
            _topRight.RenderTransform = inverseTransform;

            var bottomLeftRect = new Rect(new Point(-halfW, contentHeight - halfH), new Size(HandleSize, HandleSize));
            _bottomLeft.Arrange(bottomLeftRect);
            _bottomLeft.RenderTransform = inverseTransform;

            var bottomRightRect = new Rect(new Point(contentWidth - halfW, contentHeight - halfH), new Size(HandleSize, HandleSize));
            _bottomRight.Arrange(bottomRightRect);
            _bottomRight.RenderTransform = inverseTransform;

            var leftMoveRect = new Rect(new Point(-halfW, contentHeight / 2 - halfH), new Size(HandleSize, HandleSize));
            _leftMove.Arrange(leftMoveRect);
            _leftMove.RenderTransform = inverseTransform;

            var rightMoveRect = new Rect(new Point(contentWidth - halfW, contentHeight / 2 - halfH), new Size(HandleSize, HandleSize));
            _rightMove.Arrange(rightMoveRect);
            _rightMove.RenderTransform = inverseTransform;

            double rotateHandleYOffset = RotateHandleSize / 2 + 15;
            Point rotateHandleCenter = new Point(contentWidth / 2, contentHeight + rotateHandleYOffset);
            var rotateThumbRect = new Rect(new Point(rotateHandleCenter.X - halfRotateW, rotateHandleCenter.Y - halfRotateH), new Size(RotateHandleSize, RotateHandleSize));
            _rotateThumb.Arrange(rotateThumbRect);
            _rotateThumb.RenderTransform = inverseTransform;

            var clipBounds = new Rect(_mainWindow.PlayerClipArea.RenderSize);
            var transform = _mainWindow.PlayerClipArea.TransformToVisual(this.AdornedElement);
            var clipRectInAdornerSpace = transform.TransformBounds(clipBounds);

            _topLeft.Visibility = clipRectInAdornerSpace.IntersectsWith(topLeftRect) ? Visibility.Visible : Visibility.Collapsed;
            _topRight.Visibility = clipRectInAdornerSpace.IntersectsWith(topRightRect) ? Visibility.Visible : Visibility.Collapsed;
            _bottomLeft.Visibility = clipRectInAdornerSpace.IntersectsWith(bottomLeftRect) ? Visibility.Visible : Visibility.Collapsed;
            _bottomRight.Visibility = clipRectInAdornerSpace.IntersectsWith(bottomRightRect) ? Visibility.Visible : Visibility.Collapsed;
            _leftMove.Visibility = clipRectInAdornerSpace.IntersectsWith(leftMoveRect) ? Visibility.Visible : Visibility.Collapsed;
            _rightMove.Visibility = clipRectInAdornerSpace.IntersectsWith(rightMoveRect) ? Visibility.Visible : Visibility.Collapsed;

            // NEW: nếu không cho xoay => luôn ẩn rotate thumb
            if (_rotationEnabled)
            {
                _rotateThumb.Visibility = clipRectInAdornerSpace.IntersectsWith(rotateThumbRect) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _rotateThumb.Visibility = Visibility.Collapsed;
            }

            return finalSize;
        }

        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];
    }
}