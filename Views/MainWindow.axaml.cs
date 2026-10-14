using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaserOne.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RectangleMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        // Attiva lo strumento rettangolo nel motore CAD personalizzato
        MyLaserCanvas.ActivateRectangleTool();
    }
}