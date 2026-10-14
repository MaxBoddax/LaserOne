using System;
using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaserOne.Models;

namespace LaserOne.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingUI = false;
    private SerialPort? _serialPort = null;
    private bool _isConnected = false;

    public MainWindow()
    {
        InitializeComponent();

        // Popola la lista delle porte seriali disponibili all'avvio
        RefreshAvailablePorts();

        // Sottoscrizione all'evento di selezione emesso dal controllo CAD
        MyLaserCanvas.SelectionChanged += OnCanvasSelectionChanged;
    }

    private void RefreshAvailablePorts()
    {
        if (_isConnected) return;

        try
        {
            string[] ports = SerialPort.GetPortNames();
            PortComboBox.ItemsSource = ports;
            if (ports.Length > 0)
            {
                PortComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception)
        {
            PortComboBox.ItemsSource = null;
        }
    }

    private void RefreshPorts_Click(object? sender, RoutedEventArgs e)
    {
        RefreshAvailablePorts();
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            try
            {
                string? selectedPort = PortComboBox.SelectedItem?.ToString();

                if (string.IsNullOrEmpty(selectedPort))
                {
                    GrblStatusText.Text = "Stato GRBL: Seleziona una porta COM";
                    GrblStatusText.Foreground = Brushes.Red;
                    return;
                }

                _serialPort = new SerialPort(selectedPort, 115200)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                _serialPort.Open();
                _isConnected = true;

                GrblStatusText.Text = $"Stato GRBL: Connesso ({selectedPort})";
                GrblStatusText.Foreground = Brushes.DarkGreen;
                ConnectButton.Content = "Disconnetti";
                PortComboBox.IsEnabled = false;
            }
            catch (Exception)
            {
                GrblStatusText.Text = "Stato GRBL: Errore di connessione";
                GrblStatusText.Foreground = Brushes.Red;
            }
        }
        else
        {
            DisconnectGrbl();
        }
    }

    private void DisconnectGrbl()
    {
        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        catch { }

        _serialPort = null;
        _isConnected = false;
        GrblStatusText.Text = "Stato GRBL: Disconnesso";
        GrblStatusText.Foreground = Brushes.DimGray;
        ConnectButton.Content = "Connetti Sculpfun";
        PortComboBox.IsEnabled = true;
        RefreshAvailablePorts();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisconnectGrbl();
        base.OnClosed(e);
    }

    private void OnCanvasSelectionChanged(RectangleShape? selectedRect, bool isEnabled)
    {
        _isUpdatingUI = true;

        if (selectedRect == null)
        {
            PropertiesPanel.IsEnabled = false;
            NoSelectionText.IsVisible = true;
            NumX.Value = 0;
            NumY.Value = 0;
            NumWidth.Value = 1;
            NumHeight.Value = 1;
        }
        else
        {
            PropertiesPanel.IsEnabled = isEnabled;
            NoSelectionText.IsVisible = false;
            NumX.Value = (decimal)selectedRect.X;
            NumY.Value = (decimal)selectedRect.Y;
            NumWidth.Value = (decimal)selectedRect.Width;
            NumHeight.Value = (decimal)selectedRect.Height;
        }

        _isUpdatingUI = false;
    }

    private void Property_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingUI) return;

        double x = (double)(NumX.Value ?? 0);
        double y = (double)(NumY.Value ?? 0);
        double width = (double)(NumWidth.Value ?? 1);
        double height = (double)(NumHeight.Value ?? 1);

        MyLaserCanvas.UpdateSelectedShapeProperties(x, y, width, height);
    }

    private void SelectionMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        MyLaserCanvas.ActivateSelectionTool();
    }

    private void RectangleMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        MyLaserCanvas.ActivateRectangleTool();
    }

    private void GridSnapCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (GridSnapCheckBox != null && MyLaserCanvas != null)
        {
            MyLaserCanvas.IsGridSnapEnabled = GridSnapCheckBox.IsChecked ?? true;
        }
    }
}