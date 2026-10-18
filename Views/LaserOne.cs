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

    private enum InteractionMode
    {
        None,
        Move,
        Rotate,
        ScaleTL, ScaleTC, ScaleTR,
        ScaleML, ScaleMR,
        ScaleBL, ScaleBC, ScaleBR
    }

    private ToolMode _currentTool = ToolMode.Selection;
    private InteractionMode _currentInteraction = InteractionMode.None;

    private double m_zoomFactor = 1.0;
    private Point m_panOffset = new Point(0, 0);
    private Point m_lastMousePos;
    private bool m_isPanning = false;
    private bool m_isInitialized = false;

    public double WorkAreaWidth { get; set; } = CncConfig.WorkAreaWidth;
    public double WorkAreaHeight { get; set; } = CncConfig.WorkAreaHeight;

    private const double RulerSize = 22.0;

    private readonly RectangleTool _rectangleTool = new();
    private Point2D? _currentMouseWorldPos = null;

    private Point2D? _drawingStartPoint = null;
    private bool _isDrawingShape = false;

    // Selezione Multipla e Box Selection
    private ShapeModel? _selectedShape = null;
    private readonly List<ShapeModel> _selectedShapes = new();
    public event Action<ShapeModel?, bool>? SelectionChanged;

    private bool _isBoxSelecting = false;
    private Point2D _boxSelectStart;
    private Point2D _boxSelectCurrent;

    private Point2D _dragStartWorldPos;
    private Rect _shapeOriginalBbox;
    private double _origX, _origY, _origWidth, _origHeight, _origRadius, _origX2, _origY2, _origRotation;

    private readonly Dictionary<ShapeModel, double> _shapeRotations = new();

    // Struttura per memorizzare lo stato originale delle forme nella selezione multipla
    private struct ShapeOriginalState
    {
        public double X, Y, X2, Y2, Radius, Width, Height;
    }
    private readonly Dictionary<ShapeModel, ShapeOriginalState> _originalShapeStates = new();

    public bool IsGridSnapEnabled { get; set; } = true;
    private const double GridStep = 1.0;

    private readonly List<ShapeModel> _shapes = new();

    public IReadOnlyList<ShapeModel> Shapes => _shapes.AsReadOnly();

    public void AddShape(ShapeModel shape)
    {
        _shapes.Add(shape);
        InvalidateVisual();
    }

    public LaserOne()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
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
        Cursor = new Cursor(StandardCursorType.Arrow);
        InvalidateVisual();
    }

    public void ActivateRectangleTool()
    {
        _currentTool = ToolMode.Rectangle;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        _selectedShapes.Clear();
        SetSelectedShape(null, false);
        Cursor = new Cursor(StandardCursorType.Cross);
        InvalidateVisual();
    }

    public void ActivateLineTool()
    {
        _currentTool = ToolMode.Line;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        _selectedShapes.Clear();
        SetSelectedShape(null, false);
        Cursor = new Cursor(StandardCursorType.Cross);
        InvalidateVisual();
    }

    public void ActivateCircleTool()
    {
        _currentTool = ToolMode.Circle;
        _rectangleTool.Reset();
        _isDrawingShape = false;
        _selectedShapes.Clear();
        SetSelectedShape(null, false);
        Cursor = new Cursor(StandardCursorType.Cross);
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
            else if (_selectedShape is CircleShape circle)
            {
                circle.Radius = Math.Max(1, width / 2.0);
            }
            InvalidateVisual();
        }
    }

    public void ClearAll()
    {
        _shapes.Clear();
        _shapeRotations.Clear();
        _selectedShapes.Clear();
        SetSelectedShape(null, false);
        _rectangleTool.Reset();
        _isDrawingShape = false;
        _drawingStartPoint = null;
        ActivateSelectionTool();
        InvalidateVisual();
    }

    public void CancelAction()
    {
        bool wasDrawing = false;
        if (_currentTool == ToolMode.Rectangle && _rectangleTool.IsDrawing)
        {
            _rectangleTool.Reset();
            wasDrawing = true;
        }
        if (_isDrawingShape)
        {
            _isDrawingShape = false;
            _drawingStartPoint = null;
            wasDrawing = true;
        }

        if (wasDrawing)
        {
            ActivateSelectionTool();
            InvalidateVisual();
            return;
        }

        if (_selectedShapes.Count > 0)
        {
            _selectedShapes.Clear();
            SetSelectedShape(null, false);
            InvalidateVisual();
        }
    }

    public void DeleteSelected()
    {
        bool deleted = false;

        if (_selectedShapes.Count > 0)
        {
            foreach (var shape in _selectedShapes)
            {
                _shapes.Remove(shape);
                _shapeRotations.Remove(shape);
            }
            _selectedShapes.Clear();
            deleted = true;
        }
        else if (_selectedShape != null)
        {
            _shapes.Remove(_selectedShape);
            _shapeRotations.Remove(_selectedShape);
            deleted = true;
        }

        if (deleted)
        {
            SetSelectedShape(null, false);
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelAction();
            e.Handled = true;
        }
    }

    private Point2D SnapToGrid(Point2D worldPoint)
    {
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

        var clipRect = new Rect(RulerSize, RulerSize, Math.Max(0, Bounds.Width - RulerSize), Math.Max(0, Bounds.Height - RulerSize));

        using (context.PushClip(clipRect))
        {
            var transform = Matrix.CreateScale(m_zoomFactor, m_zoomFactor) * Matrix.CreateTranslation(m_panOffset.X, m_panOffset.Y);

            using (context.PushTransform(transform))
            {
                double minX = -WorkAreaWidth * 0.30;
                double maxX = WorkAreaWidth * 1.30;
                double minY = -WorkAreaHeight * 0.30;
                double maxY = WorkAreaHeight * 1.30;

                var outerBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(242, 242, 242)), new Pen(Brushes.Olive, 1.5 / m_zoomFactor), outerBounds);

                var workBounds = new Rect(0, 0, WorkAreaWidth, WorkAreaHeight);
                context.DrawRectangle(Brushes.White, new Pen(Brushes.Lime, 2.0 / m_zoomFactor), workBounds);

                DrawGrid(context, minX, maxX, minY, maxY);
            }

            foreach (var shape in _shapes)
            {
                bool isSelected = _selectedShapes.Contains(shape);
                var pen = isSelected
                    ? new Pen(Brushes.Blue, 2.0 / m_zoomFactor)
                    : new Pen(Brushes.Red, 1.5 / m_zoomFactor);

                var bbox = GetShapeWorldBounds(shape);
                var center = new Point(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0);
                double rotDeg = _shapeRotations.TryGetValue(shape, out var r) ? r : 0.0;
                double rotRad = rotDeg * (Math.PI / 180.0);

                var shapeTransform = Matrix.CreateTranslation(-center.X, -center.Y) *
                                     Matrix.CreateRotation(rotRad) *
                                     Matrix.CreateTranslation(center.X, center.Y) *
                                     transform;

                using (context.PushTransform(shapeTransform))
                {
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
            }

            using (context.PushTransform(transform))
            {
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

                if (_isBoxSelecting)
                {
                    double xMin = Math.Min(_boxSelectStart.X, _boxSelectCurrent.X);
                    double xMax = Math.Max(_boxSelectStart.X, _boxSelectCurrent.X);
                    double yMin = Math.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
                    double yMax = Math.Max(_boxSelectStart.Y, _boxSelectCurrent.Y);
                    var boxRect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                    var boxFill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 255));
                    var boxPen = new Pen(Brushes.Blue, 1.0 / m_zoomFactor);
                    context.DrawRectangle(boxFill, boxPen, boxRect);
                }
            }

            // Rendering delle maniglie o del riquadro di gruppo
            if (_selectedShapes.Count == 1 && _selectedShape != null)
            {
                DrawRotatedSelectionHandles(context, _selectedShape);
            }
            else if (_selectedShapes.Count > 1)
            {
                DrawGroupSelectionBox(context, _selectedShapes);
            }
        }

        DrawRulers(context, Bounds.Width, Bounds.Height);
    }

    private Rect GetShapeWorldBounds(ShapeModel shape)
    {
        if (shape is RectangleShape rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }
        else if (shape is CircleShape circle)
        {
            return new Rect(circle.X - circle.Radius, circle.Y - circle.Radius, circle.Radius * 2, circle.Radius * 2);
        }
        else if (shape is LineShape line)
        {
            double minX = Math.Min(line.X, line.X2);
            double maxX = Math.Max(line.X, line.X2);
            double minY = Math.Min(line.Y, line.Y2);
            double maxY = Math.Max(line.Y, line.Y2);
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
        return new Rect(0, 0, 0, 0);
    }

    private Rect GetCombinedWorldBounds(IEnumerable<ShapeModel> shapes)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        bool hasAny = false;

        foreach (var shape in shapes)
        {
            var bbox = GetShapeWorldBounds(shape);
            minX = Math.Min(minX, bbox.X);
            minY = Math.Min(minY, bbox.Y);
            maxX = Math.Max(maxX, bbox.X + bbox.Width);
            maxY = Math.Max(maxY, bbox.Y + bbox.Height);
            hasAny = true;
        }

        if (!hasAny) return new Rect(0, 0, 0, 0);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private Point WorldToScreen(double wx, double wy)
    {
        return new Point(wx * m_zoomFactor + m_panOffset.X, wy * m_zoomFactor + m_panOffset.Y);
    }

    private Point RotatePoint(double x, double y, Point center, double rotRad)
    {
        double cos = Math.Cos(rotRad);
        double sin = Math.Sin(rotRad);
        double dx = x - center.X;
        double dy = y - center.Y;
        return new Point(
            center.X + (dx * cos - dy * sin),
            center.Y + (dx * sin + dy * cos)
        );
    }

    private void DrawRotatedSelectionHandles(DrawingContext context, ShapeModel shape)
    {
        var bbox = GetShapeWorldBounds(shape);
        var center = new Point(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0);
        double rotDeg = _shapeRotations.TryGetValue(shape, out var r) ? r : 0.0;
        double rotRad = rotDeg * (Math.PI / 180.0);

        Point wTL = RotatePoint(bbox.X, bbox.Y, center, rotRad);
        Point wTR = RotatePoint(bbox.X + bbox.Width, bbox.Y, center, rotRad);
        Point wBR = RotatePoint(bbox.X + bbox.Width, bbox.Y + bbox.Height, center, rotRad);
        Point wBL = RotatePoint(bbox.X, bbox.Y + bbox.Height, center, rotRad);

        Point sTL = WorldToScreen(wTL.X, wTL.Y);
        Point sTR = WorldToScreen(wTR.X, wTR.Y);
        Point sBR = WorldToScreen(wBR.X, wBR.Y);
        Point sBL = WorldToScreen(wBL.X, wBL.Y);

        var dashPen = new Pen(Brushes.Blue, 1.5);
        context.DrawLine(dashPen, sTL, sTR);
        context.DrawLine(dashPen, sTR, sBR);
        context.DrawLine(dashPen, sBR, sBL);
        context.DrawLine(dashPen, sBL, sTL);

        Point sTC = new Point((sTL.X + sTR.X) / 2.0, (sTL.Y + sTR.Y) / 2.0);
        Point sBC = new Point((sBL.X + sBR.X) / 2.0, (sBL.Y + sBR.Y) / 2.0);
        Point sML = new Point((sTL.X + sBL.X) / 2.0, (sTL.Y + sBL.Y) / 2.0);
        Point sMR = new Point((sTR.X + sBR.X) / 2.0, (sTR.Y + sBR.Y) / 2.0);

        Point[] screenGrips = { sTL, sTC, sTR, sML, sMR, sBL, sBC, sBR };

        var gripPen = new Pen(Brushes.Blue, 1);
        var gripBrush = Brushes.White;
        double gripSize = 6.0;
        double halfGrip = gripSize / 2.0;

        Vector topDir = new Vector(sTR.X - sTL.X, sTR.Y - sTL.Y);
        topDir = topDir.Length > 0 ? Vector.Normalize(topDir) : new Vector(0, -1);
        Vector perpUp = new Vector(-topDir.Y, topDir.X);
        Point rotHandle = sTC + perpUp * 18.0;

        context.DrawLine(new Pen(Brushes.Blue, 1), sTC, rotHandle);
        context.DrawEllipse(gripBrush, gripPen, rotHandle, 4, 4);

        foreach (var g in screenGrips)
        {
            var gripRect = new Rect(g.X - halfGrip, g.Y - halfGrip, gripSize, gripSize);
            context.DrawRectangle(gripBrush, gripPen, gripRect);
        }
    }

    private void DrawGroupSelectionBox(DrawingContext context, List<ShapeModel> shapes)
    {
        var bbox = GetCombinedWorldBounds(shapes);
        if (bbox.Width <= 0 || bbox.Height <= 0) return;

        Point sTL = WorldToScreen(bbox.X, bbox.Y);
        Point sTR = WorldToScreen(bbox.X + bbox.Width, bbox.Y);
        Point sBR = WorldToScreen(bbox.X + bbox.Width, bbox.Y + bbox.Height);
        Point sBL = WorldToScreen(bbox.X, bbox.Y + bbox.Height);

        var dashPen = new Pen(Brushes.Blue, 1.5);
        context.DrawLine(dashPen, sTL, sTR);
        context.DrawLine(dashPen, sTR, sBR);
        context.DrawLine(dashPen, sBR, sBL);
        context.DrawLine(dashPen, sBL, sTL);

        double gripSize = 6.0;
        double halfGrip = gripSize / 2.0;
        var gripPen = new Pen(Brushes.Blue, 1);
        var gripBrush = Brushes.White;

        Point[] grips = {
            sTL,
            new Point((sTL.X + sTR.X)/2, (sTL.Y + sTR.Y)/2),
            sTR,
            new Point((sTR.X + sBR.X)/2, (sTR.Y + sBR.Y)/2),
            sBR,
            new Point((sBL.X + sBR.X)/2, (sBL.Y + sBR.Y)/2),
            sBL,
            new Point((sTL.X + sBL.X)/2, (sTL.Y + sBL.Y)/2)
        };

        foreach (var g in grips)
        {
            var gripRect = new Rect(g.X - halfGrip, g.Y - halfGrip, gripSize, gripSize);
            context.DrawRectangle(gripBrush, gripPen, gripRect);
        }
    }

    private InteractionMode HitTestBox(Point mousePos, Rect bbox, double rotDeg = 0)
    {
        var center = new Point(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0);
        double rotRad = rotDeg * (Math.PI / 180.0);

        Point wTL = RotatePoint(bbox.X, bbox.Y, center, rotRad);
        Point wTR = RotatePoint(bbox.X + bbox.Width, bbox.Y, center, rotRad);
        Point wBR = RotatePoint(bbox.X + bbox.Width, bbox.Y + bbox.Height, center, rotRad);
        Point wBL = RotatePoint(bbox.X, bbox.Y + bbox.Height, center, rotRad);

        Point sTL = WorldToScreen(wTL.X, wTL.Y);
        Point sTR = WorldToScreen(wTR.X, wTR.Y);
        Point sBR = WorldToScreen(wBR.X, wBR.Y);
        Point sBL = WorldToScreen(wBL.X, wBL.Y);

        Point sTC = new Point((sTL.X + sTR.X) / 2.0, (sTL.Y + sTR.Y) / 2.0);
        Point sBC = new Point((sBL.X + sBR.X) / 2.0, (sBL.Y + sBR.Y) / 2.0);
        Point sML = new Point((sTL.X + sBL.X) / 2.0, (sTL.Y + sBL.Y) / 2.0);
        Point sMR = new Point((sTR.X + sBR.X) / 2.0, (sTR.Y + sBR.Y) / 2.0);

        if (HitTestPoint(mousePos, sTL)) return InteractionMode.ScaleTL;
        if (HitTestPoint(mousePos, sTC)) return InteractionMode.ScaleTC;
        if (HitTestPoint(mousePos, sTR)) return InteractionMode.ScaleTR;
        if (HitTestPoint(mousePos, sML)) return InteractionMode.ScaleML;
        if (HitTestPoint(mousePos, sMR)) return InteractionMode.ScaleMR;
        if (HitTestPoint(mousePos, sBL)) return InteractionMode.ScaleBL;
        if (HitTestPoint(mousePos, sBC)) return InteractionMode.ScaleBC;
        if (HitTestPoint(mousePos, sBR)) return InteractionMode.ScaleBR;

        Point2D worldMouse = ScreenToWorld(mousePos);
        Point invWorldMouse = RotatePoint(worldMouse.X, worldMouse.Y, center, -rotRad);
        if (bbox.Contains(new Point(invWorldMouse.X, invWorldMouse.Y)))
            return InteractionMode.Move;

        return InteractionMode.None;
    }

    private InteractionMode HitTestHandles(Point mousePos, ShapeModel shape)
    {
        var bbox = GetShapeWorldBounds(shape);
        double rotDeg = _shapeRotations.TryGetValue(shape, out var r) ? r : 0.0;
        return HitTestBox(mousePos, bbox, rotDeg);
    }

    private bool HitTestPoint(Point p1, Point p2)
    {
        return Math.Abs(p1.X - p2.X) <= 6 && Math.Abs(p1.Y - p2.Y) <= 6;
    }

    private void DrawGrid(DrawingContext context, double minX, double maxX, double minY, double maxY)
    {
        double step = GridStep;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 215, 215, 215)), 0.15);
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(85, 195, 195, 195)), 0.25);
        var outerGridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 180, 180, 180)), 0.15);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 100, 130, 100)), 0.6);

        for (double x = 0; x <= WorkAreaWidth; x += step)
        {
            bool isMajor = (Math.Abs(x % 10) < 0.001);
            bool isAxis = (Math.Abs(x) < 0.001);
            var pen = isAxis ? axisPen : (isMajor ? majorGridPen : gridPen);
            context.DrawLine(pen, new Point(x, 0), new Point(x, WorkAreaHeight));
        }

        for (double y = 0; y <= WorkAreaHeight; y += step)
        {
            bool isMajor = (Math.Abs(y % 10) < 0.001);
            bool isAxis = (Math.Abs(y) < 0.001);
            var pen = isAxis ? axisPen : (isMajor ? majorGridPen : gridPen);
            context.DrawLine(pen, new Point(0, y), new Point(WorkAreaWidth, y));
        }

        double extraStep = 10.0;
        for (double x = minX; x <= maxX; x += extraStep)
        {
            if (x < 0 || x > WorkAreaWidth)
            {
                context.DrawLine(outerGridPen, new Point(x, minY), new Point(x, maxY));
            }
        }

        for (double y = minY; y <= maxY; y += extraStep)
        {
            if (y < 0 || y > WorkAreaHeight)
            {
                context.DrawLine(outerGridPen, new Point(minX, y), new Point(maxX, y));
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

    private void StoreOriginalShapeStates()
    {
        _originalShapeStates.Clear();
        foreach (var shp in _selectedShapes)
        {
            var state = new ShapeOriginalState
            {
                X = shp.X,
                Y = shp.Y
            };
            if (shp is LineShape ln)
            {
                state.X2 = ln.X2;
                state.Y2 = ln.Y2;
            }
            else if (shp is CircleShape circ)
            {
                state.Radius = circ.Radius;
            }
            else if (shp is RectangleShape rect)
            {
                state.Width = rect.Width;
                state.Height = rect.Height;
            }
            _originalShapeStates[shp] = state;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var props = e.GetCurrentPoint(this).Properties;
        Point mousePos = e.GetPosition(this);

        if (props.IsMiddleButtonPressed || e.KeyModifiers == KeyModifiers.Alt)
        {
            m_isPanning = true;
            m_lastMousePos = mousePos;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Selection)
        {
            // 1. Gestione maniglie se c'è una sola forma selezionata
            if (_selectedShape != null && _selectedShapes.Count == 1)
            {
                var interaction = HitTestHandles(mousePos, _selectedShape);
                if (interaction != InteractionMode.None)
                {
                    _currentInteraction = interaction;
                    _dragStartWorldPos = ScreenToWorld(mousePos);
                    _shapeOriginalBbox = GetShapeWorldBounds(_selectedShape);
                    _origX = _shapeOriginalBbox.X;
                    _origY = _shapeOriginalBbox.Y;
                    _origWidth = _shapeOriginalBbox.Width;
                    _origHeight = _shapeOriginalBbox.Height;
                    _origRotation = _shapeRotations.TryGetValue(_selectedShape, out var r) ? r : 0.0;
                    if (_selectedShape is CircleShape circ) _origRadius = circ.Radius;
                    if (_selectedShape is LineShape line) { _origX2 = line.X2; _origY2 = line.Y2; }

                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            var worldPos = ScreenToWorld(mousePos);

            // 2. Se ci sono più forme selezionate (es. ingranaggio), verifica maniglie o movimento del gruppo
            if (_selectedShapes.Count > 1)
            {
                var groupBbox = GetCombinedWorldBounds(_selectedShapes);
                var interaction = HitTestBox(mousePos, groupBbox);
                if (interaction != InteractionMode.None)
                {
                    _currentInteraction = interaction;
                    _dragStartWorldPos = worldPos;
                    _shapeOriginalBbox = groupBbox;
                    _origX = groupBbox.X;
                    _origY = groupBbox.Y;
                    _origWidth = groupBbox.Width;
                    _origHeight = groupBbox.Height;
                    StoreOriginalShapeStates();
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            // 3. Ricerca della forma cliccata singolarmente
            ShapeModel? clickedShape = null;

            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                var shape = _shapes[i];
                var bbox = GetShapeWorldBounds(shape);
                var center = new Point(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0);
                double rotDeg = _shapeRotations.TryGetValue(shape, out var r) ? r : 0.0;
                Point invWorldMouse = RotatePoint(worldPos.X, worldPos.Y, center, -rotDeg * (Math.PI / 180.0));

                if (shape is RectangleShape rect)
                {
                    if (invWorldMouse.X >= rect.X && invWorldMouse.X <= rect.X + rect.Width &&
                        invWorldMouse.Y >= rect.Y && invWorldMouse.Y <= rect.Y + rect.Height)
                    {
                        clickedShape = shape;
                        break;
                    }
                }
                else if (shape is CircleShape circle)
                {
                    double dx = invWorldMouse.X - circle.X;
                    double dy = invWorldMouse.Y - circle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= circle.Radius)
                    {
                        clickedShape = shape;
                        break;
                    }
                }
                else if (shape is LineShape line)
                {
                    double minX = Math.Min(line.X, line.X2);
                    double maxX = Math.Max(line.X, line.X2);
                    double minY = Math.Min(line.Y, line.Y2);
                    double maxY = Math.Max(line.Y, line.Y2);
                    if (invWorldMouse.X >= minX - 3 && invWorldMouse.X <= maxX + 3 &&
                        invWorldMouse.Y >= minY - 3 && invWorldMouse.Y <= maxY + 3)
                    {
                        clickedShape = shape;
                        break;
                    }
                }
            }

            if (clickedShape != null)
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (!_selectedShapes.Contains(clickedShape))
                    {
                        _selectedShapes.Clear();
                        _selectedShapes.Add(clickedShape);
                    }
                }
                else
                {
                    if (_selectedShapes.Contains(clickedShape))
                        _selectedShapes.Remove(clickedShape);
                    else
                        _selectedShapes.Add(clickedShape);
                }

                _selectedShape = _selectedShapes.Count > 0 ? _selectedShapes[^1] : null;
                SetSelectedShape(_selectedShape, _selectedShape != null);

                _currentInteraction = InteractionMode.Move;
                _dragStartWorldPos = worldPos;
                StoreOriginalShapeStates();
                e.Pointer.Capture(this);
            }
            else
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    _selectedShapes.Clear();
                    SetSelectedShape(null, false);
                }

                _isBoxSelecting = true;
                _boxSelectStart = worldPos;
                _boxSelectCurrent = worldPos;
                e.Pointer.Capture(this);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Rectangle)
        {
            var rawWorldPos = ScreenToWorld(mousePos);
            var snappedWorldPos = SnapToGrid(rawWorldPos);

            _rectangleTool.OnMouseDown(snappedWorldPos, rect =>
            {
                _shapes.Add(rect);
                _selectedShapes.Clear();
                _selectedShapes.Add(rect);
                SetSelectedShape(rect, true);
                ActivateSelectionTool();
            });

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Line)
        {
            var rawWorldPos = ScreenToWorld(mousePos);
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
                _selectedShapes.Clear();
                _selectedShapes.Add(newLine);
                SetSelectedShape(newLine, true);
                _isDrawingShape = false;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Circle)
        {
            var rawWorldPos = ScreenToWorld(mousePos);
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
                _selectedShapes.Clear();
                _selectedShapes.Add(newCircle);
                SetSelectedShape(newCircle, true);
                _isDrawingShape = false;
                ActivateSelectionTool();
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            m_isPanning = true;
            m_lastMousePos = mousePos;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point currentPos = e.GetPosition(this);

        if (m_isPanning)
        {
            Vector delta = currentPos - m_lastMousePos;
            m_panOffset += delta;
            m_lastMousePos = currentPos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isBoxSelecting)
        {
            _boxSelectCurrent = ScreenToWorld(currentPos);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_currentInteraction != InteractionMode.None)
        {
            var currentWorldPos = ScreenToWorld(currentPos);

            double deltaX = currentWorldPos.X - _dragStartWorldPos.X;
            double deltaY = currentWorldPos.Y - _dragStartWorldPos.Y;

            if (_currentInteraction == InteractionMode.Move)
            {
                foreach (var shp in _selectedShapes)
                {
                    if (_originalShapeStates.TryGetValue(shp, out var orig))
                    {
                        shp.X = orig.X + deltaX;
                        shp.Y = orig.Y + deltaY;
                        if (shp is LineShape line)
                        {
                            line.X2 = orig.X2 + deltaX;
                            line.Y2 = orig.Y2 + deltaY;
                        }
                        else if (shp is CircleShape circle)
                        {
                            circle.X = orig.X + deltaX;
                            circle.Y = orig.Y + deltaY;
                        }
                        else if (shp is RectangleShape rect)
                        {
                            rect.X = orig.X + deltaX;
                            rect.Y = orig.Y + deltaY;
                        }
                    }
                }
            }
            else if (_selectedShapes.Count > 1 && _currentInteraction >= InteractionMode.ScaleTL && _currentInteraction <= InteractionMode.ScaleBR)
            {
                // Ridimensionamento proporzionale di gruppo (es. ingranaggio)
                double newW = Math.Max(1, _origWidth + deltaX);
                double scaleFactor = newW / _origWidth;
                scaleFactor = Math.Max(0.1, scaleFactor);

                double groupCenterX = _shapeOriginalBbox.X + _shapeOriginalBbox.Width / 2.0;
                double groupCenterY = _shapeOriginalBbox.Y + _shapeOriginalBbox.Height / 2.0;

                foreach (var shp in _selectedShapes)
                {
                    if (_originalShapeStates.TryGetValue(shp, out var orig))
                    {
                        shp.X = groupCenterX + (orig.X - groupCenterX) * scaleFactor;
                        shp.Y = groupCenterY + (orig.Y - groupCenterY) * scaleFactor;

                        if (shp is LineShape line)
                        {
                            line.X2 = groupCenterX + (orig.X2 - groupCenterX) * scaleFactor;
                            line.Y2 = groupCenterY + (orig.Y2 - groupCenterY) * scaleFactor;
                        }
                        else if (shp is CircleShape circle)
                        {
                            circle.X = groupCenterX + (orig.X - groupCenterX) * scaleFactor;
                            circle.Y = groupCenterY + (orig.Y - groupCenterY) * scaleFactor;
                            circle.Radius = orig.Radius * scaleFactor;
                        }
                        else if (shp is RectangleShape rect)
                        {
                            rect.X = groupCenterX + (orig.X - groupCenterX) * scaleFactor;
                            rect.Y = groupCenterY + (orig.Y - groupCenterY) * scaleFactor;
                            rect.Width = orig.Width * scaleFactor;
                            rect.Height = orig.Height * scaleFactor;
                        }
                    }
                }
            }
            else if (_currentInteraction == InteractionMode.Rotate && _selectedShapes.Count == 1 && _selectedShape != null)
            {
                var bbox = _shapeOriginalBbox;
                var center = new Point(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0);
                double initialAngle = Math.Atan2(_dragStartWorldPos.Y - center.Y, _dragStartWorldPos.X - center.X);
                double currentAngle = Math.Atan2(currentWorldPos.Y - center.Y, currentWorldPos.X - center.X);
                double deltaAngle = currentAngle - initialAngle;
                double newRotation = _origRotation + deltaAngle * (180.0 / Math.PI);
                _shapeRotations[_selectedShape] = newRotation;
            }
            else if (_selectedShape is RectangleShape rect && _selectedShapes.Count == 1)
            {
                double x = _origX;
                double y = _origY;
                double w = _origWidth;
                double h = _origHeight;

                switch (_currentInteraction)
                {
                    case InteractionMode.ScaleBR:
                        w = Math.Max(1, _origWidth + deltaX);
                        h = Math.Max(1, _origHeight + deltaY);
                        break;
                    case InteractionMode.ScaleBL:
                        double newX = Math.Min(_origX + _origWidth - 1, _origX + deltaX);
                        w = Math.Max(1, (_origX + _origWidth) - newX);
                        x = newX;
                        h = Math.Max(1, _origHeight + deltaY);
                        break;
                    case InteractionMode.ScaleTR:
                        double newY = Math.Min(_origY + _origHeight - 1, _origY + deltaY);
                        h = Math.Max(1, (_origY + _origHeight) - newY);
                        y = newY;
                        w = Math.Max(1, _origWidth + deltaX);
                        break;
                    case InteractionMode.ScaleTL:
                        double nx = Math.Min(_origX + _origWidth - 1, _origX + deltaX);
                        double ny = Math.Min(_origY + _origHeight - 1, _origY + deltaY);
                        w = Math.Max(1, (_origX + _origWidth) - nx);
                        h = Math.Max(1, (_origY + _origHeight) - ny);
                        x = nx;
                        y = ny;
                        break;
                    case InteractionMode.ScaleMR:
                        w = Math.Max(1, _origWidth + deltaX);
                        break;
                    case InteractionMode.ScaleML:
                        double nx2 = Math.Min(_origX + _origWidth - 1, _origX + deltaX);
                        w = Math.Max(1, (_origX + _origWidth) - nx2);
                        x = nx2;
                        break;
                    case InteractionMode.ScaleBC:
                        h = Math.Max(1, _origHeight + deltaY);
                        break;
                    case InteractionMode.ScaleTC:
                        double ny2 = Math.Min(_origY + _origHeight - 1, _origY + deltaY);
                        h = Math.Max(1, (_origY + _origHeight) - ny2);
                        y = ny2;
                        break;
                }

                rect.X = x;
                rect.Y = y;
                rect.Width = w;
                rect.Height = h;
            }
            else if (_selectedShape is CircleShape circ && _selectedShapes.Count == 1)
            {
                double scaleFactor = 1.0 + (deltaX / _origWidth);
                circ.Radius = Math.Max(1, _origRadius * Math.Max(0.1, scaleFactor));
            }

            SetSelectedShape(_selectedShape, true);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _currentMouseWorldPos = ScreenToWorld(currentPos);

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

        if (_isBoxSelecting)
        {
            _isBoxSelecting = false;

            double xMin = Math.Min(_boxSelectStart.X, _boxSelectCurrent.X);
            double xMax = Math.Max(_boxSelectStart.X, _boxSelectCurrent.X);
            double yMin = Math.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
            double yMax = Math.Max(_boxSelectStart.Y, _boxSelectCurrent.Y);
            var selectionBox = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

            if (selectionBox.Width < 2 && selectionBox.Height < 2)
            {
                _selectedShapes.Clear();
                SetSelectedShape(null, false);
            }
            else
            {
                foreach (var shape in _shapes)
                {
                    var bbox = GetShapeWorldBounds(shape);
                    if (selectionBox.Intersects(bbox))
                    {
                        if (!_selectedShapes.Contains(shape))
                            _selectedShapes.Add(shape);
                    }
                }

                _selectedShape = _selectedShapes.Count > 0 ? _selectedShapes[^1] : null;
                SetSelectedShape(_selectedShape, _selectedShape != null);
            }

            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_currentInteraction != InteractionMode.None)
        {
            _currentInteraction = InteractionMode.None;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

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

        Point mousePos = e.GetPosition(this);

        double zoomFactorDelta = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = m_zoomFactor * zoomFactorDelta;

        if (newZoom < 0.05 || newZoom > 50.0)
            return;

        m_panOffset = new Point(
            mousePos.X - (mousePos.X - m_panOffset.X) * (newZoom / m_zoomFactor),
            mousePos.Y - (mousePos.Y - m_panOffset.Y) * (newZoom / m_zoomFactor)
        );

        m_zoomFactor = newZoom;

        InvalidateVisual();
        e.Handled = true;
    }
}