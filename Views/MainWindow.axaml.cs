using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaserOne.Models;
using LaserOne.Tools;

namespace LaserOne.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingUI = false;
    private SerialPort? _serialPort = null;
    private bool _isConnected = false;

    public MainWindow()
    {
        InitializeComponent();

        // Carica le impostazioni salvate (.ini) all'avvio e le applica al canvas
        CncConfig.Load();
        if (MyLaserCanvas != null)
        {
            MyLaserCanvas.WorkAreaWidth = CncConfig.WorkAreaWidth;
            MyLaserCanvas.WorkAreaHeight = CncConfig.WorkAreaHeight;
        }

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
            string? selectedPort = PortComboBox.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedPort))
            {
                GrblStatusText.Text = "Stato GRBL: Seleziona una porta COM";
                GrblStatusText.Foreground = Brushes.Red;
                return;
            }

            GrblStatusText.Text = "Stato GRBL: Connessione in corso...";
            GrblStatusText.Foreground = Brushes.DarkOrange;

            // Tentativo di connessione con verifica avanzata del dispositivo GRBL
            if (TryConnectAndVerifyGrbl(selectedPort))
            {
                _isConnected = true;
                GrblStatusText.Text = $"Stato GRBL: Connesso ({selectedPort})";
                GrblStatusText.Foreground = Brushes.DarkGreen;
                ConnectButton.Content = "Disconnetti";
                PortComboBox.IsEnabled = false;
                AppendToConsole($"Connesso con successo a {selectedPort}\n");
            }
            else
            {
                GrblStatusText.Text = "Stato GRBL: Nessun laser GRBL rilevato";
                GrblStatusText.Foreground = Brushes.Red;
            }
        }
        else
        {
            DisconnectGrbl();
        }
    }

    private bool TryConnectAndVerifyGrbl(string portName)
    {
        try
        {
            _serialPort = new SerialPort(portName, 115200)
            {
                ReadTimeout = 2000,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n"
            };

            _serialPort.Open();

            // Attende un tempo sufficiente affinché la scheda completi il reset e l'avvio
            Thread.Sleep(1500);

            string response = "";
            try
            {
                response = _serialPort.ReadExisting();
            }
            catch (TimeoutException) { }

            // Se il buffer è vuoto, invia una richiesta di stato rapida '?'
            if (string.IsNullOrEmpty(response) || (!response.Contains("Grbl", StringComparison.OrdinalIgnoreCase) && !response.Contains("<")))
            {
                _serialPort.WriteLine("?");
                Thread.Sleep(400);
                try
                {
                    response += _serialPort.ReadExisting();
                }
                catch (TimeoutException) { }
            }

            // Se ancora non risponde, prova a interrogare il firmware con '$I'
            if (string.IsNullOrEmpty(response) || (!response.Contains("Grbl", StringComparison.OrdinalIgnoreCase) && !response.Contains("[VER")))
            {
                _serialPort.WriteLine("$I");
                Thread.Sleep(400);
                try
                {
                    response += _serialPort.ReadExisting();
                }
                catch (TimeoutException) { }
            }

            // Verifica allargata: accetta se rileva la firma GRBL, le parentesi di stato '<', i dati di versione '[VER' o un 'ok'
            if (!string.IsNullOrEmpty(response) &&
                (response.Contains("Grbl", StringComparison.OrdinalIgnoreCase) ||
                 response.Contains("<") ||
                 response.Contains("[VER") ||
                 response.Contains("ok")))
            {
                return true; // Connessione e autenticazione GRBL riuscite!
            }
            else
            {
                _serialPort.Close();
                _serialPort = null;
                return false;
            }
        }
        catch (Exception)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort = null;
            return false;
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
        AppendToConsole("Disconnesso dalla porta seriale.\n");
    }

    protected override void OnClosed(EventArgs e)
    {
        DisconnectGrbl();
        base.OnClosed(e);
    }

    // --- GESTIONE TERMINALE E STREAMING G-CODE ---

    private void SendCommand_Click(object? sender, RoutedEventArgs e)
    {
        SendManualCommand();
    }

    private void CommandInputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendManualCommand();
            e.Handled = true;
        }
    }

    private void SendManualCommand()
    {
        string? cmd = CommandInputBox.Text?.Trim();
        if (string.IsNullOrEmpty(cmd)) return;

        SendSerialCommand(cmd);
        CommandInputBox.Text = string.Empty;
    }

    public void SendSerialCommand(string command)
    {
        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.WriteLine(command);
            }
            else
            {
                AppendToConsole("[Simulazione] ");
            }

            AppendToConsole($"> {command}\n");
        }
        catch (Exception ex)
        {
            AppendToConsole($"Errore invio: {ex.Message}\n");
        }
    }

    public void AppendToConsole(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ConsoleLogBox != null)
            {
                ConsoleLogBox.Text += text;
                ConsoleLogBox.CaretIndex = ConsoleLogBox.Text?.Length ?? 0;
            }
        });
    }

    private async void StartJob_Click(object? sender, RoutedEventArgs e)
    {
        var shapes = MyLaserCanvas.Shapes;
        if (shapes.Count == 0)
        {
            AppendToConsole("⚠️ Nessuna forma presente sul canvas da lavorare!\n");
            return;
        }

        // Genera il G-Code dalle forme usando global:: per evitare conflitti con la classe LaserOne
        var gcodeLines = global::LaserOne.Tools.GCodeGenerator.Generate(shapes, laserPower: 800, feedRate: 1200);

        AppendToConsole($"--- Inizio Lavoro ({gcodeLines.Count} istruzioni) ---\n");

        foreach (var line in gcodeLines)
        {
            SendSerialCommand(line);
            await Task.Delay(50); // Ritardo simulato per lo streaming
        }

        AppendToConsole("--- Lavoro Completato ---\n");
    }

    // --- GESTIONE SELEZIONE E PROPRIETÀ ---

    private void OnCanvasSelectionChanged(ShapeModel? selectedShape, bool isEnabled)
    {
        _isUpdatingUI = true;

        if (selectedShape == null)
        {
            PropertiesPanel.IsEnabled = false;
            NoSelectionText.IsVisible = true;
            NumX.Value = 0m;
            NumY.Value = 0m;
            NumWidth.Value = 1m;
            NumHeight.Value = 1m;
        }
        else
        {
            PropertiesPanel.IsEnabled = isEnabled;
            NoSelectionText.IsVisible = false;
            NumX.Value = (decimal)selectedShape.X;
            NumY.Value = (decimal)selectedShape.Y;

            if (selectedShape is RectangleShape rect)
            {
                NumWidth.Value = (decimal)rect.Width;
                NumHeight.Value = (decimal)rect.Height;
            }
            else if (selectedShape is CircleShape circle)
            {
                NumWidth.Value = (decimal)(circle.Radius * 2);
                NumHeight.Value = (decimal)(circle.Radius * 2);
            }
            else if (selectedShape is LineShape line)
            {
                NumWidth.Value = (decimal)Math.Abs(line.X2 - line.X);
                NumHeight.Value = (decimal)Math.Abs(line.Y2 - line.Y);
            }
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
        if (ShapeToolText != null) ShapeToolText.Text = "Rettangolo";
        if (ShapeToolIcon != null) ShapeToolIcon.Data = Avalonia.Media.Geometry.Parse("M3,5H21V19H3V5M5,7V17H19V7H5Z");
    }

    private void LineMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        MyLaserCanvas.ActivateLineTool();
        if (ShapeToolText != null) ShapeToolText.Text = "Linea";
        if (ShapeToolIcon != null) ShapeToolIcon.Data = Avalonia.Media.Geometry.Parse("M19,6.41L17.59,5L7,15.59V9H5V19H15V17H8.41L19,6.41Z");
    }

    private void CircleMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        MyLaserCanvas.ActivateCircleTool();
        if (ShapeToolText != null) ShapeToolText.Text = "Cerchio";
        if (ShapeToolIcon != null) ShapeToolIcon.Data = Avalonia.Media.Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4Z");
    }

    private void CncSettingsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new CncSettingsWindow();
        settingsWindow.ShowDialog(this);

        // Ricarica le impostazioni e aggiorna il canvas alla chiusura
        CncConfig.Load();
        if (MyLaserCanvas != null)
        {
            MyLaserCanvas.WorkAreaWidth = CncConfig.WorkAreaWidth;
            MyLaserCanvas.WorkAreaHeight = CncConfig.WorkAreaHeight;
            MyLaserCanvas.InvalidateVisual();
        }
    }

    private void GridSnapCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (GridSnapCheckBox != null && MyLaserCanvas != null)
        {
            MyLaserCanvas.IsGridSnapEnabled = GridSnapCheckBox.IsChecked ?? true;
        }
    }
}