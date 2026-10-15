using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LaserOne.Models;
using LaserOne.Tools;

namespace LaserOne.Views;

public class LaserOne : Control
{
    private enum ToolMode
    {
        Selection,
        Rectangle,
        Line,
        Circle
    }

    private ToolMode _currentTool = ToolMode.Selection;

    private double m_zoomFactor = 1.0;
    private Point m_panOffset = new Point(0, 0);
    private Point m_lastMousePos;
    private bool m_isPanning = false;
    private bool m_isInitialized = false;

    // Dimensioni Area di Lavoro (configurate da CncConfig)
    public double WorkAreaWidth { get; set; } = CncConfig.WorkAreaWidth;
    public double WorkAreaHeight { get; set; } = CncConfig.WorkAreaHeight;

    private const double RulerSize = 22.0;

    // Strumenti e stato di disegno
    private readonly RectangleTool _rectangleTool = new();
    private Point2D? _currentMouseWorldPos = null;

    // Stato temporaneo per Linea e Cerchio
    private Point2D? _drawingStartPoint = null;
    private bool _isDrawingShape = false;

    // Gestione Selezione
    private ShapeModel? _selectedShape = null;
    public event Action<ShapeModel?, bool>? SelectionChanged;

    // Gestione Snap alla Griglia (Passo griglia millimetrata a 1mm)
    public bool IsGridSnapEnabled { get; set; } = true;
    private const double GridStep = 1.0;

    // Lista delle forme disegnate sul canvas
    private readonly List<ShapeModel> _shapes = new();

    public LaserOne()
    {
        Focusable = true;
    }

    private void SetSelectedShape(ShapeModel? shape, bool isEnabled)
    {
        _selectedShape = shape;
        SelectionChanged?.Invoke(_selectedShape, isEnabled);
    }

    public void ActivateSelectionTool()
    {
        _currentTool = ToolMode.Selection;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        InvalidateVisual();
    }

    public void ActivateRectangleTool()
    {
        _currentTool = ToolMode.Rectangle;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        SetSelectedShape(null, false);
        InvalidateVisual();
    }

    public void ActivateLineTool()
    {
        _currentTool = ToolMode.Line;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        SetSelectedShape(null, false);
        InvalidateVisual();
    }

    public void ActivateCircleTool()
    {
        _currentTool = ToolMode.Circle;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        SetSelectedShape(null, false);
        InvalidateVisual();
    }

    public void UpdateSelectedShapeProperties(double x, double y, double width, double height)
    {
        if (_selectedShape != null)
        {
            _selectedShape.X = x;
            _selectedShape.Y = y;
            if (_selectedShape is RectangleShape rect)
            {
                rect.Width = Math.Max(1, width);
                rect.Height = Math.Max(1, height);
            }
            InvalidateVisual();
        }
    }

    private Point2D SnapToGrid(Point2D worldPoint)
    {
        // Limiti estesi al 30% per lato
        double minX = -WorkAreaWidth * 0.30;
        double maxX = WorkAreaWidth * 1.30;
        double minY = -WorkAreaHeight * 0.30;
        double maxY = WorkAreaHeight * 1.30;

        double clampedX = Math.Clamp(worldPoint.X, minX, maxX);
        double clampedY = Math.Clamp(worldPoint.Y, minY, maxY);

        if (!IsGridSnapEnabled)
            return new Point2D(clampedX, clampedY);

        double snappedX = Math.Round(clampedX / GridStep, MidpointRounding.AwayFromZero) * GridStep;
        double snappedY = Math.Round(clampedY / GridStep, MidpointRounding.AwayFromZero) * GridStep;
        return new Point2D(snappedX, snappedY);
    }

    private void CenterWorkArea()
    {
        if (Bounds.Width <= RulerSize || Bounds.Height <= RulerSize) return;

        double viewWidth = Bounds.Width - RulerSize;
        double viewHeight = Bounds.Height - RulerSize;

        m_panOffset = new Point(
            RulerSize + (viewWidth - WorkAreaWidth * m_zoomFactor) / 2.0,
            RulerSize + (viewHeight - WorkAreaHeight * m_zoomFactor) / 2.0
        );
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            if (!m_isInitialized)
            {
                m_zoomFactor = 1.0;
                CenterWorkArea();
                m_isInitialized = true;
            }
        }

        context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(Bounds.Size));

        // Clip geometrico per proteggere i righelli e i menu
        var clipRect = new Rect(RulerSize, RulerSize, Math.Max(0, Bounds.Width - RulerSize), Math.Max(0, Bounds.Height - RulerSize));

        using (context.PushClip(clipRect))
        {
            var transform = Matrix.CreateScale(m_zoomFactor, m_zoomFactor) * Matrix.CreateTranslation(m_panOffset.X, m_panOffset.Y);

            using (context.PushTransform(transform))
            {
                // Sforo del 30% per lato
                double minX = -WorkAreaWidth * 0.30;
                double maxX = WorkAreaWidth * 1.30;
                double minY = -WorkAreaHeight * 0.30;
                double maxY = WorkAreaHeight * 1.30;

                // Sfondo esterno (area oscurata a sforo +30% per lato)
                var outerBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(215, 215, 215)), new Pen(Brushes.Gray, 1.0 / m_zoomFactor), outerBounds);

                // Area di lavoro reale centrale
                var workBounds = new Rect(0, 0, WorkAreaWidth, WorkAreaHeight);
                context.DrawRectangle(Brushes.White, new Pen(Brushes.DarkOliveGreen, 2.0 / m_zoomFactor), workBounds);

                DrawGrid(context, minX, maxX, minY, maxY);

                foreach (var shape in _shapes)
                {
                    bool isSelected = (shape == _selectedShape);
                    var pen = isSelected
                        ? new Pen(Brushes.Blue, 2.0 / m_zoomFactor)
                        : new Pen(Brushes.Red, 1.5 / m_zoomFactor);

                    if (shape is RectangleShape rect)
                    {
                        var rectGeometry = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                        IBrush fill = isSelected
                            ? new SolidColorBrush(Color.FromArgb(40, 0, 120, 255))
                            : Brushes.Transparent;
                        context.DrawRectangle(fill, pen, rectGeometry);
                    }
                    else if (shape is LineShape line)
                    {
                        context.DrawLine(pen, new Point(line.X, line.Y), new Point(line.X2, line.Y2));
                    }
                    else if (shape is CircleShape circle)
                    {
                        IBrush fill = isSelected
                            ? new SolidColorBrush(Color.FromArgb(40, 0, 120, 255))
                            : Brushes.Transparent;
                        context.DrawEllipse(fill, pen, new Point(circle.X, circle.Y), circle.Radius, circle.Radius);
                    }
                }

                if (_currentTool == ToolMode.Rectangle && _rectangleTool.IsDrawing && _currentMouseWorldPos.HasValue)
                {
                    var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
                    var previewRect = RectangleTool.CreateRectangle(_rectangleTool.FirstCorner, snappedCurrentPos);
                    var previewGeom = new Rect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height);
                    var dashPen = new Pen(Brushes.Gray, 1.0 / m_zoomFactor);
                    context.DrawRectangle(Brushes.Transparent, dashPen, previewGeom);
                }
                else if (_currentTool == ToolMode.Line && _isDrawingShape && _drawingStartPoint.HasValue && _currentMouseWorldPos.HasValue)
                {
                    var startPoint = _drawingStartPoint.Value;
                    var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
                    var dashPen = new Pen(Brushes.Gray, 1.0 / m_zoomFactor);
                    context.DrawLine(dashPen, new Point(startPoint.X, startPoint.Y), new Point(snappedCurrentPos.X, snappedCurrentPos.Y));
                }
                else if (_currentTool == ToolMode.Circle && _isDrawingShape && _drawingStartPoint.HasValue && _currentMouseWorldPos.HasValue)
                {
                    var startPoint = _drawingStartPoint.Value;
                    var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
                    double dx = snappedCurrentPos.X - startPoint.X;
                    double dy = snappedCurrentPos.Y - startPoint.Y;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    var dashPen = new Pen(Brushes.Gray, 1.0 / m_zoomFactor);
                    context.DrawEllipse(Brushes.Transparent, dashPen, new Point(startPoint.X, startPoint.Y), radius, radius);
                }
            }
        }

        DrawRulers(context, Bounds.Width, Bounds.Height);
    }

    private void DrawGrid(DrawingContext context, double minX, double maxX, double minY, double maxY)
    {
        double step = GridStep; // 1mm

        // Linee sottili, delicate e vicine al colore dello sfondo
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 215, 215, 215)), 0.15);
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 195, 195, 195)), 0.25);
        var outerGridPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 160, 160, 160)), 0.35);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 100, 130, 100)), 0.6);

        for (double x = minX; x <= maxX; x += step)
        {
            bool isMajor = (Math.Abs(x % 10) < 0.001);
            var pen = (Math.Abs(x) < 0.001) ? axisPen : (x < 0 || x > WorkAreaWidth ? outerGridPen : (isMajor ? majorGridPen : gridPen));

            if (m_zoomFactor > 0.35 || isMajor)
            {
                context.DrawLine(pen, new Point(x, minY), new Point(x, maxY));
            }
        }

        for (double y = minY; y <= maxY; y += step)
        {
            bool isMajor = (Math.Abs(y % 10) < 0.001);
            var pen = (Math.Abs(y) < 0.001) ? axisPen : (y < 0 || y > WorkAreaHeight ? outerGridPen : (isMajor ? majorGridPen : gridPen));

            if (m_zoomFactor > 0.35 || isMajor)
            {
                context.DrawLine(pen, new Point(minX, y), new Point(maxX, y));
            }
        }
    }

    private void DrawRulers(DrawingContext context, double width, double height)
    {
        var rulerBg = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        var borderPen = new Pen(Brushes.LightGray, 1);
        var tickPen = new Pen(Brushes.DimGray, 1);
        var textBrush = Brushes.DarkSlateGray;

        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, width, RulerSize));
        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, RulerSize, height));
        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, RulerSize, RulerSize));

        double targetPixelSpacing = 80.0;
        double rawWorldSpacing = targetPixelSpacing / m_zoomFactor;
        double step = GetNiceStep(rawWorldSpacing);

        double worldLeft = (RulerSize - m_panOffset.X) / m_zoomFactor;
        double worldRight = (width - m_panOffset.X) / m_zoomFactor;
        double worldTop = (RulerSize - m_panOffset.Y) / m_zoomFactor;
        double worldBottom = (height - m_panOffset.Y) / m_zoomFactor;

        double startX = Math.Floor(worldLeft / step) * step;
        for (double x = startX; x <= worldRight; x += step)
        {
            double screenX = x * m_zoomFactor + m_panOffset.X;
            if (screenX >= RulerSize && screenX <= width)
            {
                context.DrawLine(tickPen, new Point(screenX, RulerSize - 5), new Point(screenX, RulerSize));

                string text = x.ToString("0", CultureInfo.InvariantCulture);
                var formattedText = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    9,
                    textBrush);

                context.DrawText(formattedText, new Point(screenX - formattedText.Width / 2, 3));
            }
        }

        double startY = Math.Floor(worldTop / step) * step;
        for (double y = startY; y <= worldBottom; y += step)
        {
            double screenY = y * m_zoomFactor + m_panOffset.Y;
            if (screenY >= RulerSize && screenY <= height)
            {
                context.DrawLine(tickPen, new Point(RulerSize - 5, screenY), new Point(RulerSize, screenY));

                string text = y.ToString("0", CultureInfo.InvariantCulture);
                var formattedText = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    9,
                    textBrush);

                context.DrawText(formattedText, new Point(4, screenY - formattedText.Height / 2));
            }
        }
    }

    private double GetNiceStep(double raw)
    {
        double exponent = Math.Floor(Math.Log10(raw));
        double fraction = raw / Math.Pow(10, exponent);
        double niceFraction;

        if (fraction <= 1.0) niceFraction = 1.0;
        else if (fraction <= 2.0) niceFraction = 2.0;
        else if (fraction <= 5.0) niceFraction = 5.0;
        else niceFraction = 10.0;

        return niceFraction * Math.Pow(10, exponent);
    }

    private Point2D ScreenToWorld(Point screenPoint)
    {
        double worldX = (screenPoint.X - m_panOffset.X) / m_zoomFactor;
        double worldY = (screenPoint.Y - m_panOffset.Y) / m_zoomFactor;
        return new Point2D(worldX, worldY);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsMiddleButtonPressed || e.KeyModifiers == KeyModifiers.Alt)
        {
            m_isPanning = true;
            m_lastMousePos = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Selection)
        {
            var worldPos = ScreenToWorld(e.GetPosition(this));
            ShapeModel? found = null;

            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                var shape = _shapes[i];
                if (shape is RectangleShape rect)
                {
                    if (worldPos.X >= rect.X && worldPos.X <= rect.X + rect.Width &&
                        worldPos.Y >= rect.Y && worldPos.Y <= rect.Y + rect.Height)
                    {
                        found = shape;
                        break;
                    }
                }
                else if (shape is CircleShape circle)
                {
                    double dx = worldPos.X - circle.X;
                    double dy = worldPos.Y - circle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= circle.Radius)
                    {
                        found = shape;
                        break;
                    }
                }
                else if (shape is LineShape line)
                {
                    double minX = Math.Min(line.X, line.X2);
                    double maxX = Math.Max(line.X, line.X2);
                    double minY = Math.Min(line.Y, line.Y2);
                    double maxY = Math.Max(line.Y, line.Y2);
                    if (worldPos.X >= minX - 3 && worldPos.X <= maxX + 3 &&
                        worldPos.Y >= minY - 3 && worldPos.Y <= maxY + 3)
                    {
                        found = shape;
                        break;
                    }
                }
            }

            SetSelectedShape(found, found != null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Rectangle)
        {
            var rawWorldPos = ScreenToWorld(e.GetPosition(this));
            var snappedWorldPos = SnapToGrid(rawWorldPos);

            _rectangleTool.OnMouseDown(snappedWorldPos, rect =>
            {
                _shapes.Add(rect);
                SetSelectedShape(rect, true);
            });

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Line)
        {
            var rawWorldPos = ScreenToWorld(e.GetPosition(this));
            var snappedWorldPos = SnapToGrid(rawWorldPos);

            if (!_isDrawingShape || !_drawingStartPoint.HasValue)
            {
                _drawingStartPoint = snappedWorldPos;
                _isDrawingShape = true;
            }
            else
            {
                var startPoint = _drawingStartPoint.Value;
                var newLine = new LineShape
                {
                    X = startPoint.X,
                    Y = startPoint.Y,
                    X2 = snappedWorldPos.X,
                    Y2 = snappedWorldPos.Y
                };
                _shapes.Add(newLine);
                SetSelectedShape(newLine, true);
                _isDrawingShape = false;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Circle)
        {
            var rawWorldPos = ScreenToWorld(e.GetPosition(this));
            var snappedWorldPos = SnapToGrid(rawWorldPos);

            if (!_isDrawingShape || !_drawingStartPoint.HasValue)
            {
                _drawingStartPoint = snappedWorldPos;
                _isDrawingShape = true;
            }
            else
            {
                var startPoint = _drawingStartPoint.Value;
                double dx = snappedWorldPos.X - startPoint.X;
                double dy = snappedWorldPos.Y - startPoint.Y;
                double radius = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));

                var newCircle = new CircleShape
                {
                    X = startPoint.X,
                    Y = startPoint.Y,
                    Radius = radius
                };
                _shapes.Add(newCircle);
                SetSelectedShape(newCircle, true);
                _isDrawingShape = false;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            m_isPanning = true;
            m_lastMousePos = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (m_isPanning)
        {
            Point currentPos = e.GetPosition(this);
            Vector delta = currentPos - m_lastMousePos;
            m_panOffset += delta;
            m_lastMousePos = currentPos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _currentMouseWorldPos = ScreenToWorld(e.GetPosition(this));

        if (_currentTool == ToolMode.Rectangle && _rectangleTool.IsDrawing)
        {
            var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
            var previewRect = RectangleTool.CreateRectangle(_rectangleTool.FirstCorner, snappedCurrentPos);
            SelectionChanged?.Invoke(previewRect, false);
            InvalidateVisual();
        }
        else if ((_currentTool == ToolMode.Line || _currentTool == ToolMode.Circle) && _isDrawingShape)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (m_isPanning)
        {
            m_isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        double zoomFactorDelta = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = m_zoomFactor * zoomFactorDelta;

        if (newZoom < 0.05 || newZoom > 50.0)
            return;

        m_zoomFactor = newZoom;
        CenterWorkArea();

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedShape != null)
        {
            _shapes.Remove(_selectedShape);
            SetSelectedShape(null, false);
            InvalidateVisual();
            e.Handled = true;
        }
    }
}