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
        Rectangle
    }

    private ToolMode _currentTool = ToolMode.Selection;

    private double m_zoomFactor = 1.0;
    private Point m_panOffset = new Point(0, 0);
    private Point m_lastMousePos;
    private bool m_isPanning = false;
    private bool m_isInitialized = false;

    // Strumento Rettangolo e stato
    private readonly RectangleTool _rectangleTool = new();
    private Point2D? _currentMouseWorldPos = null;

    // Gestione Selezione
    private RectangleShape? _selectedShape = null;
    public event Action<RectangleShape?, bool>? SelectionChanged;

    // Gestione Snap alla Griglia
    public bool IsGridSnapEnabled { get; set; } = true;
    private const double GridStep = 50.0; // Passo della griglia in mm

    // Lista delle forme disegnate sul canvas
    private readonly List<RectangleShape> _shapes = new();

    public LaserOne()
    {
        Focusable = true;
    }

    private void SetSelectedShape(RectangleShape? shape, bool isEnabled)
    {
        _selectedShape = shape;
        SelectionChanged?.Invoke(_selectedShape, isEnabled);
    }

    public void ActivateSelectionTool()
    {
        _currentTool = ToolMode.Selection;
        _rectangleTool.Reset();
        InvalidateVisual();
    }

    public void ActivateRectangleTool()
    {
        _currentTool = ToolMode.Rectangle;
        _rectangleTool.Reset();
        SetSelectedShape(null, false);
        InvalidateVisual();
    }

    public void UpdateSelectedShapeProperties(double x, double y, double width, double height)
    {
        if (_selectedShape != null)
        {
            _selectedShape.X = x;
            _selectedShape.Y = y;
            _selectedShape.Width = Math.Max(1, width);
            _selectedShape.Height = Math.Max(1, height);
            InvalidateVisual();
        }
    }

    private Point2D SnapToGrid(Point2D worldPoint)
    {
        if (!IsGridSnapEnabled)
            return worldPoint;

        double snappedX = Math.Round(worldPoint.X / GridStep, MidpointRounding.AwayFromZero) * GridStep;
        double snappedY = Math.Round(worldPoint.Y / GridStep, MidpointRounding.AwayFromZero) * GridStep;
        return new Point2D(snappedX, snappedY);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!m_isInitialized && Bounds.Width > 0 && Bounds.Height > 0)
        {
            m_panOffset = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
            m_zoomFactor = 1.2;
            m_isInitialized = true;
        }

        // Sfondo dell'area di lavoro
        context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(Bounds.Size));

        // 1. Area CAD con la trasformazione di Pan & Zoom applicata
        var transform = Matrix.CreateScale(m_zoomFactor, m_zoomFactor) * Matrix.CreateTranslation(m_panOffset.X, m_panOffset.Y);

        using (context.PushTransform(transform))
        {
            // Griglia di sfondo dinamica
            DrawGrid(context);

            // Cornice perimetrale area di lavoro (3000x3000mm)
            double width = 3000;
            double height = 3000;
            double startX = -1500;
            double startY = -1500;

            var gridBounds = new Rect(startX, startY, width, height);
            context.DrawRectangle(null, new Pen(Brushes.DarkOliveGreen, 2.0 / m_zoomFactor), gridBounds);

            // Disegna tutte le forme confermate
            foreach (var rect in _shapes)
            {
                var rectGeometry = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                bool isSelected = (rect == _selectedShape);

                var pen = isSelected
                    ? new Pen(Brushes.Blue, 2.0 / m_zoomFactor)
                    : new Pen(Brushes.Red, 1.5 / m_zoomFactor);

                IBrush fill = isSelected
                    ? new SolidColorBrush(Color.FromArgb(40, 0, 120, 255))
                    : Brushes.Transparent;

                context.DrawRectangle(fill, pen, rectGeometry);
            }

            // Disegna l'anteprima (rubber-band) in tempo reale
            if (_currentTool == ToolMode.Rectangle && _rectangleTool.IsDrawing && _currentMouseWorldPos.HasValue)
            {
                var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
                var previewRect = RectangleTool.CreateRectangle(_rectangleTool.FirstCorner, snappedCurrentPos);
                var previewGeom = new Rect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height);
                var dashPen = new Pen(Brushes.Gray, 1.0 / m_zoomFactor);
                context.DrawRectangle(Brushes.Transparent, dashPen, previewGeom);
            }
        }

        // 2. Righelli dinamici fissi sui bordi
        DrawRulers(context, Bounds.Width, Bounds.Height);
    }

    private void DrawGrid(DrawingContext context)
    {
        double step = GridStep;
        var gridPen = new Pen(Brushes.Gainsboro, 0.5);
        var axisPen = new Pen(Brushes.DarkGray, 1);

        double width = 3000;
        double height = 3000;
        double startX = -1500;
        double startY = -1500;

        for (double x = startX; x <= startX + width; x += step)
        {
            var pen = (x == 0) ? axisPen : gridPen;
            context.DrawLine(pen, new Point(x, startY), new Point(x, startY + height));
        }

        for (double y = startY; y <= startY + height; y += step)
        {
            var pen = (y == 0) ? axisPen : gridPen;
            context.DrawLine(pen, new Point(startX, y), new Point(startX + width, y));
        }
    }

    private void DrawRulers(DrawingContext context, double width, double height)
    {
        double rulerSize = 22;

        var rulerBg = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        var borderPen = new Pen(Brushes.LightGray, 1);
        var tickPen = new Pen(Brushes.DimGray, 1);
        var textBrush = Brushes.DarkSlateGray;

        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, width, rulerSize));
        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, rulerSize, height));
        context.DrawRectangle(rulerBg, borderPen, new Rect(0, 0, rulerSize, rulerSize));

        double targetPixelSpacing = 80.0;
        double rawWorldSpacing = targetPixelSpacing / m_zoomFactor;
        double step = GetNiceStep(rawWorldSpacing);

        double worldLeft = (rulerSize - m_panOffset.X) / m_zoomFactor;
        double worldRight = (width - m_panOffset.X) / m_zoomFactor;
        double worldTop = (rulerSize - m_panOffset.Y) / m_zoomFactor;
        double worldBottom = (height - m_panOffset.Y) / m_zoomFactor;

        // --- RIGHELLO ORIZZONTALE (X) ---
        double startX = Math.Floor(worldLeft / step) * step;
        for (double x = startX; x <= worldRight; x += step)
        {
            double screenX = x * m_zoomFactor + m_panOffset.X;
            if (screenX >= rulerSize && screenX <= width)
            {
                context.DrawLine(tickPen, new Point(screenX, rulerSize - 5), new Point(screenX, rulerSize));

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

        // --- RIGHELLO VERTICALE (Y) ---
        double startY = Math.Floor(worldTop / step) * step;
        for (double y = startY; y <= worldBottom; y += step)
        {
            double screenY = y * m_zoomFactor + m_panOffset.Y;
            if (screenY >= rulerSize && screenY <= height)
            {
                context.DrawLine(tickPen, new Point(rulerSize - 5, screenY), new Point(rulerSize, screenY));

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

        // Pan con tasto centrale o Alt
        if (props.IsMiddleButtonPressed || e.KeyModifiers == KeyModifiers.Alt)
        {
            m_isPanning = true;
            m_lastMousePos = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // 1. Modalità Selezione
        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Selection)
        {
            var worldPos = ScreenToWorld(e.GetPosition(this));
            RectangleShape? found = null;

            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                var rect = _shapes[i];
                if (worldPos.X >= rect.X && worldPos.X <= rect.X + rect.Width &&
                    worldPos.Y >= rect.Y && worldPos.Y <= rect.Y + rect.Height)
                {
                    found = rect;
                    break;
                }
            }

            SetSelectedShape(found, found != null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 2. Modalità Disegno Rettangolo
        if (props.IsLeftButtonPressed && _currentTool == ToolMode.Rectangle)
        {
            var rawWorldPos = ScreenToWorld(e.GetPosition(this));
            var snappedWorldPos = SnapToGrid(rawWorldPos);

            _rectangleTool.OnMouseDown(snappedWorldPos, rect =>
            {
                _shapes.Add(rect);
                SetSelectedShape(rect, true); // Abilita i controlli a disegno completato
            });

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Pan con tasto sinistro sullo sfondo
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
            // Calcola l'anteprima e aggiorna i campi numerici in tempo reale ma mantenendoli disabilitati (grigiati)
            var snappedCurrentPos = SnapToGrid(_currentMouseWorldPos.Value);
            var previewRect = RectangleTool.CreateRectangle(_rectangleTool.FirstCorner, snappedCurrentPos);
            SelectionChanged?.Invoke(previewRect, false);

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