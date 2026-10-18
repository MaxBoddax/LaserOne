using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
    private ShapeModel? _selectedShape = null;

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

    // --- GESTIONE MENU ESCI ---

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // --- GESTIONE PATTERN CIRCOLARE (v0.9) ---

    private async void CircularPatternMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedShape == null)
        {
            AppendToConsole("⚠️ Seleziona prima un oggetto sul canvas per applicare il pattern circolare!\n");
            return;
        }

        // Passa la forma selezionata e l'intera lista delle forme del canvas al dialogo avanzato
        var dialog = new PatternDialog(_selectedShape, MyLaserCanvas.Shapes);
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            int copies = dialog.Copies;
            double angle = dialog.TotalAngle;
            double cx = dialog.CenterX;
            double cy = dialog.CenterY;

            // Genera le copie ruotate con orientamento radiale professionale
            double angleStep = angle / copies;
            int generatedCount = 0;

            for (int i = 1; i < copies; i++)
            {
                double currentAngle = i * angleStep;
                GenerateAndAddRotatedShapeToCanvas(_selectedShape, currentAngle, cx, cy);
                generatedCount++;
            }

            MyLaserCanvas.InvalidateVisual();
            AppendToConsole($"⚙️ Pattern Circolare completato: generate {generatedCount} copie radiali attorno a ({cx:F1}, {cy:F1}).\n");
        }
    }

    private void GenerateAndAddRotatedShapeToCanvas(ShapeModel original, double angleDeg, double cx, double cy)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        if (original is RectangleShape rect)
        {
            // Centro originale del rettangolo
            double origRectCx = rect.X + rect.Width / 2.0;
            double origRectCy = rect.Y + rect.Height / 2.0;

            // Ruota il centro del rettangolo attorno al centro del pattern (cx, cy)
            double distCx = origRectCx - cx;
            double distCy = origRectCy - cy;
            double newCx = cx + distCx * cos - distCy * sin;
            double newCy = cy + distCx * sin + distCy * cos;

            // Semidimensioni
            double hw = rect.Width / 2.0;
            double hh = rect.Height / 2.0;

            // Offset locali dei 4 angoli
            double[] dxs = { -hw, hw, hw, -hw };
            double[] dys = { -hh, -hh, hh, hh };

            double[] wx = new double[4];
            double[] wy = new double[4];

            // Ruota i 4 angoli attorno al nuovo centro del rettangolo per ottenere l'orientamento radiale
            for (int j = 0; j < 4; j++)
            {
                double rotDx = dxs[j] * cos - dys[j] * sin;
                double rotDy = dxs[j] * sin + dys[j] * cos;
                wx[j] = newCx + rotDx;
                wy[j] = newCy + rotDy;
            }

            // Aggiunge le 4 linee perimetrali del rettangolo ruotato radialmente
            MyLaserCanvas.AddShape(new LineShape { X = wx[0], Y = wy[0], X2 = wx[1], Y2 = wy[1] }); // Top
            MyLaserCanvas.AddShape(new LineShape { X = wx[1], Y = wy[1], X2 = wx[2], Y2 = wy[2] }); // Right
            MyLaserCanvas.AddShape(new LineShape { X = wx[2], Y = wy[2], X2 = wx[3], Y2 = wy[3] }); // Bottom
            MyLaserCanvas.AddShape(new LineShape { X = wx[3], Y = wy[3], X2 = wx[0], Y2 = wy[0] }); // Left
        }
        else if (original is CircleShape circle)
        {
            double newX = cx + (circle.X - cx) * cos - (circle.Y - cy) * sin;
            double newY = cy + (circle.X - cx) * sin + (circle.Y - cy) * cos;

            MyLaserCanvas.AddShape(new CircleShape
            {
                X = newX,
                Y = newY,
                Radius = circle.Radius
            });
        }
        else if (original is LineShape line)
        {
            double newX1 = cx + (line.X - cx) * cos - (line.Y - cy) * sin;
            double newY1 = cy + (line.X - cx) * sin + (line.Y - cy) * cos;

            double newX2 = cx + (line.X2 - cx) * cos - (line.Y2 - cy) * sin;
            double newY2 = cy + (line.X2 - cx) * sin + (line.Y2 - cy) * cos;

            MyLaserCanvas.AddShape(new LineShape
            {
                X = newX1,
                Y = newY1,
                X2 = newX2,
                Y2 = newY2
            });
        }
    }

    // --- GESTIONE PATTERN RETTANGOLARE (v0.9) ---

    private async void RectangularPatternMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedShape == null)
        {
            AppendToConsole("⚠️ Seleziona prima un oggetto sul canvas per applicare il pattern rettangolare!\n");
            return;
        }

        var dialog = new RectangularPatternDialog();
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            int cols = dialog.Columns;
            int rows = dialog.Rows;
            double spacingX = dialog.SpacingX;
            double spacingY = dialog.SpacingY;

            int generatedCount = 0;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Salta la posizione iniziale (0,0) che coincide con l'oggetto originale
                    if (c == 0 && r == 0) continue;

                    double offsetX = c * spacingX;
                    double offsetY = r * spacingY;

                    ShapeModel clone = CloneAndTranslateShape(_selectedShape, offsetX, offsetY);
                    MyLaserCanvas.AddShape(clone);
                    generatedCount++;
                }
            }

            MyLaserCanvas.InvalidateVisual();
            AppendToConsole($"⚙️ Pattern Rettangolare completato: generate {generatedCount} copie ({cols}x{rows}) con passo X:{spacingX}mm, Y:{spacingY}mm.\n");
        }
    }

    private ShapeModel CloneAndTranslateShape(ShapeModel original, double dx, double dy)
    {
        if (original is RectangleShape rect)
        {
            return new RectangleShape
            {
                X = rect.X + dx,
                Y = rect.Y + dy,
                Width = rect.Width,
                Height = rect.Height
            };
        }
        else if (original is CircleShape circle)
        {
            return new CircleShape
            {
                X = circle.X + dx,
                Y = circle.Y + dy,
                Radius = circle.Radius
            };
        }
        else if (original is LineShape line)
        {
            return new LineShape
            {
                X = line.X + dx,
                Y = line.Y + dy,
                X2 = line.X2 + dx,
                Y2 = line.Y2 + dy
            };
        }

        return original;
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

    // --- IMPORTAZIONE DXF ---

    public void OpenDxfMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        _ = OpenDxfFileTaskAsync();
    }

    private async Task OpenDxfFileTaskAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Apri File Vettoriale DXF",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("File DXF")
                {
                    Patterns = new[] { "*.dxf" }
                },
                new Avalonia.Platform.Storage.FilePickerFileType("Tutti i file")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            string path = file.Path.LocalPath;

            var importedShapes = global::LaserOne.Tools.DxfImporter.Import(path);
            if (importedShapes.Count > 0)
            {
                foreach (var shape in importedShapes)
                {
                    MyLaserCanvas.AddShape(shape);
                }
                AppendToConsole($"📁 Importati con successo {importedShapes.Count} elementi da: {file.Name}\n");
            }
            else
            {
                AppendToConsole($"⚠️ Nessuna forma compatibile trovata nel file DXF: {file.Name}\n");
            }
        }
    }

    // --- GESTIONE SELEZIONE E PROPRIETÀ ---

    private void OnCanvasSelectionChanged(ShapeModel? selectedShape, bool isEnabled)
    {
        _selectedShape = selectedShape; // <-- Memorizza la forma selezionata
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