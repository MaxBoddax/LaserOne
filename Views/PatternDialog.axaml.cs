using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LaserOne.Models;

namespace LaserOne.Views;

public class ShapeItemWrapper
{
    public ShapeModel Shape { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

public partial class PatternDialog : Window
{
    public int Copies => (int)(NumCopies.Value ?? 6);
    public double TotalAngle => (double)(NumTotalAngle.Value ?? 360);

    public double CenterX { get; private set; } = 0;
    public double CenterY { get; private set; } = 0;

    private ShapeModel? _selectedShape;
    private List<ShapeModel> _allShapes;

    public event EventHandler? ParametersChanged;

    public PatternDialog()
    {
        InitializeComponent();
        _allShapes = new List<ShapeModel>();
    }

    public PatternDialog(ShapeModel selectedShape, IEnumerable<ShapeModel> allShapes) : this()
    {
        _selectedShape = selectedShape;
        _allShapes = allShapes.ToList();

        // Popola la ComboBox con le altre forme presenti nel progetto
        foreach (var shape in _allShapes)
        {
            if (shape == selectedShape) continue; // Esclude l'oggetto attivo corrente

            string name = shape switch
            {
                RectangleShape r => $"Rettangolo [X:{r.X:F1}, Y:{r.Y:F1}]",
                CircleShape c => $"Cerchio [X:{c.X:F1}, Y:{c.Y:F1}, R:{c.Radius:F1}]",
                LineShape l => $"Linea [da {l.X:F1},{l.Y:F1}]",
                _ => "Forma geometrica"
            };

            CbOtherShapes.Items.Add(new ShapeItemWrapper { Shape = shape, DisplayName = name });
        }

        if (CbOtherShapes.Items.Count > 0)
        {
            CbOtherShapes.SelectedIndex = 0;
        }

        UpdateCenterValues();
    }

    private void CenterMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (ManualCoordsPanel == null || CbOtherShapes == null) return;

        ManualCoordsPanel.IsVisible = RbManualCenter.IsChecked == true;
        CbOtherShapes.IsVisible = RbOtherShapeCenter.IsChecked == true;

        UpdateCenterValues();
        ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CbOtherShapes_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCenterValues();
        ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Parameter_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        UpdateCenterValues();
        ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCenterValues()
    {
        if (RbSelfCenter?.IsChecked == true && _selectedShape != null)
        {
            GetShapeCenter(_selectedShape, out double cx, out double cy);
            CenterX = cx;
            CenterY = cy;
        }
        else if (RbManualCenter?.IsChecked == true)
        {
            CenterX = (double)(NumCenterX?.Value ?? 0);
            CenterY = (double)(NumCenterY?.Value ?? 0);
        }
        else if (RbOtherShapeCenter?.IsChecked == true && CbOtherShapes?.SelectedItem is ShapeItemWrapper wrapper)
        {
            GetShapeCenter(wrapper.Shape, out double cx, out double cy);
            CenterX = cx;
            CenterY = cy;
        }
    }

    private void GetShapeCenter(ShapeModel shape, out double cx, out double cy)
    {
        cx = 0;
        cy = 0;
        if (shape is RectangleShape rect)
        {
            cx = rect.X + rect.Width / 2.0;
            cy = rect.Y + rect.Height / 2.0;
        }
        else if (shape is CircleShape circle)
        {
            cx = circle.X;
            cy = circle.Y;
        }
        else if (shape is LineShape line)
        {
            cx = (line.X + line.X2) / 2.0;
            cy = (line.Y + line.Y2) / 2.0;
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdateCenterValues();
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}