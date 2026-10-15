using Avalonia.Controls;
using Avalonia.Interactivity;
using LaserOne.Models;

namespace LaserOne.Views;

public partial class CncSettingsWindow : Window
{
    public CncSettingsWindow()
    {
        InitializeComponent();

        // Carica i valori correnti nei controlli
        TxtWidth.Value = (decimal)CncConfig.WorkAreaWidth;
        TxtHeight.Value = (decimal)CncConfig.WorkAreaHeight;
    }

    private void CncTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CncTreeView.SelectedItem is TreeViewItem item && item.Header != null)
        {
            string headerText = item.Header.ToString() ?? "";

            // Imposta automaticamente una stima in base al modello scelto se contiene le dimensioni
            if (headerText.Contains("410x400")) { TxtWidth.Value = 410; TxtHeight.Value = 400; }
            else if (headerText.Contains("400x400")) { TxtWidth.Value = 400; TxtHeight.Value = 400; }
            else if (headerText.Contains("600x600")) { TxtWidth.Value = 600; TxtHeight.Value = 600; }
            else if (headerText.Contains("400x430")) { TxtWidth.Value = 400; TxtHeight.Value = 430; }
            else if (headerText.Contains("400x850")) { TxtWidth.Value = 400; TxtHeight.Value = 850; }
            else if (headerText.Contains("430x400")) { TxtWidth.Value = 430; TxtHeight.Value = 400; }
            else if (headerText.Contains("498x319")) { TxtWidth.Value = 498; TxtHeight.Value = 319; }

            CncConfig.SelectedModel = headerText;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        CncConfig.WorkAreaWidth = (double)(TxtWidth.Value ?? 400);
        CncConfig.WorkAreaHeight = (double)(TxtHeight.Value ?? 400);
        CncConfig.Save();
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
