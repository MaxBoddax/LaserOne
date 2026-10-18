using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaserOne.Views;

public partial class RectangularPatternDialog : Window
{
    public int Columns => (int)(NumCols.Value ?? 3);
    public int Rows => (int)(NumRows.Value ?? 3);
    public double SpacingX => (double)(NumSpacingX.Value ?? 50);
    public double SpacingY => (double)(NumSpacingY.Value ?? 50);

    public RectangularPatternDialog()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}