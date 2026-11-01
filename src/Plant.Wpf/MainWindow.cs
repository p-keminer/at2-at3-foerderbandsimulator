using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed record SignalRow(string Signal, string Direction, string Source, string Value, string Quality, string Time);
    public sealed partial class MainWindow : Window
    {
        internal readonly SessionHost Host;
        private readonly DispatcherTimer renderTimer;
        internal readonly TabControl Tabs = new();
        internal ComboBox SourceSelect = null!;
        internal Button RunButton = null!, PauseButton = null!, StepButton = null!, StopButton = null!;
        private StackPanel manualPanel = null!, referencePanel = null!;
        private readonly PlantView plantView = new();
        private readonly TraceView trace = new();
        private readonly TextBlock status = Ui.Text("Bereit. Beispielprofil geladen. Simulator ist angehalten."), time = Ui.Text("", 18, true), motion = Ui.Text(""), controllerInfo = Ui.Text(""), measurement = Ui.Text("", 19, true), diagnostic = Ui.Text(""), calibrationInfo = Ui.Text(""), adapterStatus = Ui.Text(""), voltageLabel = Ui.Text("5,0 V");
        private TextBox leftInput = null!, lengthInput = null!, distanceInput = null!, constantInput = null!, profileEditor = null!;
        private CheckBox metal = null!, reflects = null!, motorLeft = null!, motorRight = null!, red = null!, yellow = null!, green = null!;
        private Slider voltage = null!;
        private ComboBox exercise = null!, machineMode = null!;
        private readonly DataGrid signals = GridTable(), events = GridTable(), calibrationTable = GridTable();
        private long calibrationStart;
        private bool calibrating, syncing;
        private double calibrationMarkerMm;
        private readonly System.Collections.Generic.List<ComboBox> faultSelectors = new();
        private CheckBox lockedFault = null!, blockedFault = null!, speedSelectionControl = null!;
        private TextBox referenceSpeedControl = null!, slipControl = null!;
        private Button topStopButton = null!, resetButton = null!, at3Button = null!, liveStopButton = null!;
        private readonly TextBlock profileSubtitle = Ui.Text("BEISPIELPROFIL · keine Laborkalibrierung · eigene Offline-Nachbildung", 12);
        private readonly System.Collections.Generic.List<UIElement> offlineTools = new();
        private UIElement offlineSidebar = null!, liveSidebar = null!;
        private readonly System.Diagnostics.Stopwatch presentationClock = System.Diagnostics.Stopwatch.StartNew();
        private long lastDetailRefresh;
        private int lastDetailTab = -1;
        private PlantConfiguration activeConfig;
        public MainWindow(PlantConfiguration config)
        {
            activeConfig = config.Copy(); Host = new(config);
            Title = "AT-2 / AT-3 · Virtueller Förderbandteststand · OFFLINE";
            Width = Math.Min(1480, SystemParameters.WorkArea.Width); Height = Math.Min(900, SystemParameters.WorkArea.Height); MinWidth = 1000; MinHeight = 720;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Foreground = Ui.Ink; Background = Ui.Background;
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel(); heading.Children.Add(Ui.Text("AT-2 / AT-3", 12, true));
            var title = Ui.Text("Virtueller Förderbandteststand", 23, true); title.FontFamily = new FontFamily("Bahnschrift"); heading.Children.Add(title);
            heading.Children.Add(profileSubtitle);
            header.Children.Add(heading); Grid.SetColumn(time, 1); header.Children.Add(time); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            RunButton = Ui.Button("Simulator starten", () => Safe(() => Host.Change(s => s.Resume())));
            PauseButton = Ui.Button("Simulator pausieren", () => Safe(() => Host.Change(s => s.Pause())));
            StepButton = Ui.Button("Einzelschritt", () => Safe(() => Host.Change(s => { Numbers.Require(s.Paused, "Einzelschritt benötigt Simulatorpause."); s.Step(true); })));
            bar.Children.Add(RunButton); bar.Children.Add(PauseButton); bar.Children.Add(StepButton);
            resetButton = Ui.Button("Gesamtreset", () => Safe(() => Reset(activeConfig))); bar.Children.Add(resetButton);
            topStopButton = Ui.Button("Betrieb stoppen", () => Safe(() => Host.Change(s => { s.Controller.ToggleStop(); s.Log("MachineStopToggle", s.Controller.Stopped.ToString()); })));
            bar.Children.Add(topStopButton);
            liveStopButton = Ui.Button("SPS-Test trennen", DisconnectLive); liveStopButton.IsEnabled = false; bar.Children.Add(liveStopButton);
            bar.Children.Add(Ui.Button("Aufzeichnung exportieren…", Export));
            bar.Children.Add(Ui.Button("Snapshot speichern…", SaveSnapshot));
            DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
            var statusBox = new Border { Child = new ScrollViewer { Content = status, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 66 }, Padding = new Thickness(8), Background = Ui.Surface, MinHeight = 38, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetName(status, "Statusmeldung"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            DockPanel.SetDock(statusBox, Dock.Bottom); root.Children.Add(statusBox);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar = new DockPanel(); var sourceTools = new StackPanel(); DockPanel.SetDock(sourceTools, Dock.Top); sidebar.Children.Add(sourceTools);
            var tools = new StackPanel(); var offline = new StackPanel(); BuildTools(offline, sourceTools); offlineSidebar = offline; tools.Children.Add(offline);
            liveSidebar = BuildLivePanel(); tools.Children.Add(liveSidebar); sidebar.Children.Add(Ui.Scroll(tools)); body.Children.Add(sidebar);
            LiveConnect.Background = Ui.Accent; LiveConnect.Foreground = Ui.Surface; bar.Children.Insert(0, LiveConnect);
            BuildTabs();
            var observation = new Grid(); observation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); observation.RowDefinitions.Add(new RowDefinition());
            observation.Children.Add(plantView); Grid.SetRow(Tabs, 1); observation.Children.Add(Tabs);
            Grid.SetColumn(observation, 2); body.Children.Add(observation); root.Children.Add(body);
            renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            renderTimer.Tick += (_, _) => Refresh(false); renderTimer.Start();
            Closing += OnClosingLive;
            Closed += (_, _) => { renderTimer.Stop(); Host.Dispose(); };
            Deactivated += (_, _) => { if (!IsExternal) Host.Change(s => { s.Controller.JogLeft = s.Controller.JogRight = false; }); };
            Refresh();
        }
        private static DataGrid GridTable()
        {
            var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = Ui.Background, RowHeaderWidth = 0, FontSize = 13, MinHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            var names = new System.Collections.Generic.Dictionary<string, string> { ["Signal"] = "Signal", ["Direction"] = "Richtung", ["Source"] = "Quelle", ["Value"] = "Wert", ["Quality"] = "Qualität", ["Time"] = "Zeit / Bindung", ["DistanceMm"] = "Strecke [mm]", ["SampledPulses"] = "Erfasste Impulse", ["MmPerPulse"] = "mm/Impuls", ["Sequence"] = "Sequenz", ["TimeUs"] = "Zeit [µs]", ["Kind"] = "Ereignis", ["Detail"] = "Details" };
            table.AutoGeneratingColumn += (_, e) => { if (names.TryGetValue(e.PropertyName, out var name)) e.Column.Header = name; };
            return table;
        }
        internal void Safe(Action action)
        {
            try { SetStatus("Aktion ausgeführt."); action(); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            { SetStatus(e.Message, true); }
            Refresh();
        }
        internal void SetStatus(string value, bool error = false) { status.Text = value; status.Foreground = error ? Ui.Warning : Ui.Ink; }
        private void BuildTools(Panel tools, Panel sourceTools)
        {
            var p = Ui.Section(sourceTools, "Bedienung");
            SourceSelect = Ui.Select(p, "Steuerung", "OfflineManual · direkte Aktoren", "OfflineReference · Demo/Test", "ExternalPlc · manuell verbinden");
            SourceSelect.SelectionChanged += (_, _) =>
            {
                if (!syncing) Safe(() =>
                {
                    Numbers.Require(!liveHost.Snapshot.IsActive && !RecoveryBusy && !liveWorkflowBusy, "Erst den laufenden SPS-Test und die CPU-Freigabe abwarten.");
                    Host.Change(s => s.SetMode((OperationMode)SourceSelect.SelectedIndex));
                    syncing = true; motorLeft.IsChecked = motorRight.IsChecked = red.IsChecked = yellow.IsChecked = green.IsChecked = false; syncing = false;
                });
            };
            at3Button = Ui.Button("AT-3 direkt üben (Beispiel)", () => Safe(() =>
            {
                Host.Change(s => s.Pause()); var c = activeConfig.Copy(); c.CalibrationMmPerPulse = 1;
                Reset(c); Host.Change(s => { s.SetMode(OperationMode.OfflineReference); s.Controller.Exercise = Exercise.AT3; });
                exercise.SelectedIndex = 1; constantInput.Text = "1";
                SetStatus("AT-3-Einstiegsstand: eigene Nachbildung, BEISPIEL-Kalibrierung 1 mm/Impuls. Simulator starten.");
            })); p.Children.Add(at3Button);
            p = Ui.Section(tools, "Prüfling P1");
            offlineTools.Add(p);
            leftInput = Ui.Number(p, "Linke Kante [mm]", activeConfig.InitialLeftMm);
            lengthInput = Ui.Number(p, "Geometrische Referenzlänge [mm]", activeConfig.WorkpieceLengthMm);
            metal = Ui.Check("Induktiv erkennbar (Metall)", activeConfig.Metallic); reflects = Ui.Check("Optisch erkennbar", activeConfig.Reflective);
            p.Children.Add(metal); p.Children.Add(reflects);
            var row = Ui.Row(p);
            row.Children.Add(Ui.Button("Einsetzen / versetzen", () => Safe(() => Host.Change(s => s.Insert(Ui.Value(leftInput), Ui.Value(lengthInput), metal.IsChecked == true, reflects.IsChecked == true)))));
            row.Children.Add(Ui.Button("Entfernen", () => Safe(() => Host.Change(s => s.Remove()))));
            manualPanel = Ui.Section(tools, "Direkte Aktoren · OfflineManual");
            motorRight = Ui.Check("Motor rechts"); motorLeft = Ui.Check("Motor links");
            manualPanel.Children.Add(motorRight); manualPanel.Children.Add(motorLeft);
            voltage = new Slider { Minimum = 0, Maximum = 10, Value = 5, TickFrequency = .5, SmallChange = .1, LargeChange = 1, Margin = new Thickness(0, 4, 0, 4) };
            AutomationProperties.SetName(voltage, "Analoge Geschwindigkeitsvorgabe in Volt");
            manualPanel.Children.Add(voltageLabel); manualPanel.Children.Add(voltage);
            red = Ui.Check("Lampe Rot"); yellow = Ui.Check("Lampe Gelb"); green = Ui.Check("Lampe Grün");
            manualPanel.Children.Add(red); manualPanel.Children.Add(yellow); manualPanel.Children.Add(green);
            foreach (var cb in new[] { motorRight, motorLeft, red, yellow, green }) { cb.Checked += (_, _) => UpdateManual(); cb.Unchecked += (_, _) => UpdateManual(); }
            voltage.ValueChanged += (_, _) => { voltageLabel.Text = $"Geschwindigkeitsvorgabe {voltage.Value:F1} V"; UpdateManual(); };
            manualPanel.Children.Add(Ui.Button("Motor aus", () => { motorLeft.IsChecked = motorRight.IsChecked = false; UpdateManual(); }));
            referencePanel = Ui.Section(tools, "Referenzcontroller · Nachbildung");
            exercise = Ui.Select(referencePanel, "Versuchsprofil", "AT-2 · Rot = Messzyklus", "AT-3 · Rot = Fahrbefehl");
            exercise.SelectionChanged += (_, _) => { if (!syncing) Safe(() => Host.Change(s => { s.Controller.Exercise = (Exercise)exercise.SelectedIndex; s.Log("Exercise", exercise.SelectedItem.ToString()!); })); };
            machineMode = Ui.Select(referencePanel, "Betriebsart", "Automatik", "Hand (Tippbetrieb)");
            machineMode.SelectionChanged += (_, _) => { if (!syncing) Safe(() => Host.Change(s => { s.Controller.ChangeMachineMode((MachineMode)machineMode.SelectedIndex); s.Log("MachineMode", machineMode.SelectedItem.ToString()!); })); };
            StopButton = Ui.Button("Betriebsstopp / Fortsetzen", () => Safe(() => Host.Change(s => { s.Controller.ToggleStop(); s.Log("MachineStopToggle", s.Controller.Stopped.ToString()); })));
            referencePanel.Children.Add(StopButton);
            row = Ui.Row(referencePanel); row.Children.Add(Jog("Hand links halten", false)); row.Children.Add(Jog("Hand rechts halten", true));
            var speedSelection = Ui.Check("Vorwahl 60 % / 35 % (Zusatz)"); referencePanel.Children.Add(speedSelection);
            speedSelectionControl = speedSelection;
            speedSelection.Checked += (_, _) => Host.Change(s => s.Controller.UseSpeedSelection = true);
            speedSelection.Unchecked += (_, _) => Host.Change(s => s.Controller.UseSpeedSelection = false);
            var referenceSpeed = Ui.Number(referencePanel, "Referenzgeschwindigkeit [V]", 5);
            referenceSpeedControl = referenceSpeed;
            referencePanel.Children.Add(Ui.Button("Geschwindigkeit übernehmen", () => Safe(() =>
            { double v = Ui.Value(referenceSpeed); Numbers.Require(v >= 0 && v <= 10, "Geschwindigkeit benötigt 0…10 V."); Host.Change(s => s.Controller.SpeedVoltage = v); })));
            referencePanel.Children.Add(Ui.Button("Messung zurücksetzen", () => Safe(() => Host.Change(s => { s.Controller.ResetMeasurement(); s.Log("MeasurementReset", "Encoderphase und letztes Ergebnis bleiben erhalten."); }))));
            referencePanel.Children.Add(controllerInfo);
        }
        private Button Jog(string title, bool right)
        {
            var b = Ui.Button(title, () => { });
            void Set(bool on) => Host.Change(s => { if (right) s.Controller.JogRight = on; else s.Controller.JogLeft = on; });
            b.PreviewMouseLeftButtonDown += (_, _) => { Set(true); b.CaptureMouse(); };
            b.PreviewMouseLeftButtonUp += (_, _) => { Set(false); b.ReleaseMouseCapture(); };
            b.LostMouseCapture += (_, _) => Set(false); b.LostKeyboardFocus += (_, _) => Set(false);
            b.PreviewKeyDown += (_, e) => { if (e.Key == Key.Space && !e.IsRepeat) Set(true); };
            b.PreviewKeyUp += (_, e) => { if (e.Key == Key.Space) Set(false); };
            return b;
        }
        private void UpdateManual()
        {
            if (syncing || Host.Read(s => s.Mode) != OperationMode.OfflineManual) return;
            Safe(() => Host.Change(s => s.SetManual(new(motorRight.IsChecked == true, motorLeft.IsChecked == true,
                Analog.OutputRaw(voltage.Value), red.IsChecked == true, yellow.IsChecked == true, green.IsChecked == true))));
        }
        private void BuildTabs()
        {
            var layout = new StackPanel();
            var p = Ui.Section(layout, "Bewegung und Messung"); p.Children.Add(motion); p.Children.Add(measurement); p.Children.Add(diagnostic);
            p = Ui.Section(layout, "Signalverlauf · letzte 1000 ms"); p.Children.Add(trace);
            p.Children.Add(Ui.Text("Gezeichnet werden Controller-Eingangssamples. CSV trennt diese Samples von den exakten Modellflanken.", 12));
            AddTab("Messung", Ui.Scroll(layout));
            AddTab("Signale", signals);
            layout = new StackPanel(); p = Ui.Section(layout, "AT-2 · Kalibrierung in drei Fahrten");
            p.Children.Add(Ui.Text("OfflineManual verwenden. Eine Markierung starten, das Band fahren und die abgelesene Strecke eingeben. Gezählt werden abgetastete Hall-Flanken. Die Encoderphase bleibt erhalten."));
            p.Children.Add(Ui.Button("Markierung / Zählung beginnen", () => Safe(() =>
            { Numbers.Require(Host.Read(s => s.Mode) == OperationMode.OfflineManual, "Kalibrierfahrten im Modus OfflineManual durchführen."); calibrationStart = Host.Read(s => s.Counter.Rising); calibrationMarkerMm = Host.Read(s => s.Snapshot().Plant.BeltMm); calibrating = true; SetStatus("Kalibrierfahrt begonnen. Motor fahren, stoppen, Strecke am Maßstab ablesen."); })));
            distanceInput = Ui.Number(p, "Abgelesene Strecke [mm] (Betrag)", 100);
            p.Children.Add(Ui.Button("Fahrt in Kalibrierliste übernehmen", () => Safe(() =>
            {
                Numbers.Require(calibrating, "Zuerst Markierung beginnen.");
                double d = Ui.Value(distanceInput);
                Host.Change(s => { s.Calibration.Add(d, s.Counter.Rising - calibrationStart); s.Log("CalibrationRun", "Strecke manuell eingetragen; Impulse aus Samples."); }); calibrating = false;
            })));
            calibrationTable.Height = 165; p.Children.Add(calibrationTable); p.Children.Add(calibrationInfo);
            var row = Ui.Row(p);
            row.Children.Add(Ui.Button("Mittelwert übernehmen", () => Safe(() => Host.Change(s =>
            {
                Numbers.Require(s.Calibration.Mean.HasValue, "Drei gültige Fahrten erforderlich."); s.Controller.Calibration = s.Calibration.Mean;
                constantInput.Text = s.Calibration.Mean!.Value.ToString("R", CultureInfo.CurrentCulture);
                s.Log("CalibrationApplied", constantInput.Text);
            }))));
            row.Children.Add(Ui.Button("Liste leeren", () => Safe(() => Host.Change(s => s.Calibration.Clear()))));
            constantInput = Ui.Number(p, "Eigene Kalibrierkonstante [mm/Impuls]", activeConfig.CalibrationMmPerPulse ?? 1);
            p.Children.Add(Ui.Button("Konstante bewusst übernehmen", () => Safe(() => Host.Change(s => { s.Controller.Calibration = Ui.Value(constantInput); s.Log("CalibrationConstant", constantInput.Text); }))));
            p.Children.Add(Ui.Button("Testzähler zurücksetzen", () => Safe(() => Host.Change(s => { s.Counter.ResetCount(); s.Controller.ResetTestCounter(); s.Log("CounterReset", "Zählerreset ohne Hallphasenänderung."); }))));
            var poti = Ui.Number(p, "Testpotentiometer Kanal 5 [V, −10…+10]", 5);
            p.Children.Add(Ui.Button("Poti setzen / Motorvorgabe spiegeln", () => Safe(() => Host.Change(s =>
            {
                short raw = Analog.InputRaw(Ui.Value(poti)); s.Plant.PotiRaw = raw;
                if (s.Mode == OperationMode.OfflineManual)
                {
                    s.SetManual(s.Manual with { SpeedCommandRaw = Math.Max(0, (int)raw) });
                    syncing = true; voltage.Value = Math.Max(0, Analog.InputVoltage(raw)); syncing = false;
                }
                s.Log("Poti", raw.ToString(CultureInfo.InvariantCulture));
            }))));
            AddTab("Kalibrierung", Ui.Scroll(layout));
            layout = new StackPanel(); p = Ui.Section(layout, "Reproduzierbare Störungen");
            foreach (string id in new[] { "left", "optical", "right" })
            {
                string sensorId = id;
                var pick = Ui.Select(p, id == "left" ? "Sensor 2 links" : id == "right" ? "Sensor 1 rechts" : "Optischer Sensor", "Kein Fehler", "Dauerhaft LOW", "Dauerhaft HIGH", "Verzögert (+100 ms)", "Flackern (Seed)");
                faultSelectors.Add(pick);
                pick.SelectionChanged += (_, _) => { if (!syncing) Safe(() => Host.Change(s => s.SetSensorFault(sensorId, (SensorFault)pick.SelectedIndex))); };
            }
            var locked = Ui.Check("Motorwelle blockiert (MotorLocked)"); var blocked = Ui.Check("Prüfling blockiert (ObjectBlocked)"); p.Children.Add(locked); p.Children.Add(blocked);
            lockedFault = locked; blockedFault = blocked;
            locked.Checked += (_, _) => Host.Change(s => { s.Plant.MotorLocked = true; s.Log("MotorLocked", "true"); }); locked.Unchecked += (_, _) => Host.Change(s => { s.Plant.MotorLocked = false; s.Log("MotorLocked", "false"); });
            blocked.Checked += (_, _) => Host.Change(s => { s.Plant.ObjectBlocked = true; s.Log("ObjectBlocked", "true"); }); blocked.Unchecked += (_, _) => Host.Change(s => { s.Plant.ObjectBlocked = false; s.Log("ObjectBlocked", "false"); });
            var slip = Ui.Number(p, "Transportfaktor [0 = voller Schlupf, 1 = haftend]", 1);
            slipControl = slip;
            p.Children.Add(Ui.Button("Transportfaktor übernehmen", () => Safe(() => Host.Change(s => { s.Plant.TransportFactor = Ui.Value(slip); s.Log("TransportFactor", slip.Text); }))));
            p.Children.Add(Ui.Text("Delay ergänzt 100 ms. Flicker ist seedgesteuert. Motorstillstand hält den Hall-Pegel; Schlupf kann Hall ohne entsprechenden Prüflingsweg erzeugen."));
            AddTab("Störungen", Ui.Scroll(layout));
            layout = new StackPanel(); p = Ui.Section(layout, "Beispielprofil · vollständig konfigurierbar");
            p.Children.Add(Ui.Text("JSON-Schema 1. Übernahme nur bei Simulatorpause und nach Validierung. Sie setzt Anlage, Controller und Aufzeichnung zurück. Beispielmaße sind keine vermessenen Laborwerte."));
            profileEditor = new TextBox { Text = Json.Write(activeConfig), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(8) };
            AutomationProperties.SetName(profileEditor, "JSON-Anlagenprofil"); p.Children.Add(profileEditor);
            row = Ui.Row(p); row.Children.Add(Ui.Button("JSON validieren / übernehmen", () => Safe(() =>
            {
                Numbers.Require(Host.Read(s => s.Paused), "Profil nur bei Simulatorpause ändern.");
                var config = Json.Read<PlantConfiguration>(profileEditor.Text); config.Validate(); Reset(config);
            })));
            row.Children.Add(Ui.Button("Profil laden…", LoadProfile)); row.Children.Add(Ui.Button("Profil speichern…", SaveProfile));
            AddTab("Profil", Ui.Scroll(layout));
            layout = new StackPanel(); var connectionDetails = Ui.Section(layout, "Verbindung und Laufbericht");
            connectionDetails.Children.Add(adapterStatus);
            connectionDetails.Children.Add(Ui.Text("SPS-Verbindung, Prüfling und Testfreigaben werden links neben der dauerhaft sichtbaren Anlage eingestellt. Starten und Trennen bleiben oben erreichbar."));
            connectionDetails.Children.Add(Ui.Button("SPS-Bedienung anzeigen", () => Safe(() => SourceSelect.SelectedIndex = 2)));
            AddTab("Verbindung", Ui.Scroll(layout));
            AddTab("Ereignisse", events);
        }
        private void AddTab(string label, UIElement child) => Tabs.Items.Add(new TabItem { Header = label, Content = child, Padding = new Thickness(12, 7, 12, 7) });
        internal void Reset(PlantConfiguration config)
        {
            Numbers.Require(!IsExternal && !liveHost.Snapshot.IsActive, "Für einen Gesamtreset zuerst ausdrücklich zur Offlinequelle wechseln.");
            Host.Reset(config); activeConfig = config.Copy(); plantView.Configuration = activeConfig.Copy(); calibrating = false;
            syncing = true; motorLeft.IsChecked = motorRight.IsChecked = red.IsChecked = yellow.IsChecked = green.IsChecked = false;
            SourceSelect.SelectedIndex = 0; exercise.SelectedIndex = 0; machineMode.SelectedIndex = 0;
            foreach (var pick in faultSelectors) pick.SelectedIndex = 0;
            lockedFault.IsChecked = blockedFault.IsChecked = speedSelectionControl.IsChecked = false;
            metal.IsChecked = config.Metallic; reflects.IsChecked = config.Reflective; referenceSpeedControl.Text = "5"; slipControl.Text = "1";
            syncing = false;
            profileEditor.Text = Json.Write(activeConfig);
            leftInput.Text = config.InitialLeftMm.ToString(CultureInfo.CurrentCulture); lengthInput.Text = config.WorkpieceLengthMm.ToString(CultureInfo.CurrentCulture);
        }
        internal void Refresh(bool forceDetails = true)
        {
            bool details = forceDetails || presentationClock.ElapsedMilliseconds - lastDetailRefresh >= 150 || lastDetailTab != Tabs.SelectedIndex;
            if (details) { lastDetailRefresh = presentationClock.ElapsedMilliseconds; lastDetailTab = Tabs.SelectedIndex; RefreshLiveControls(); }
            if (IsExternal) { RefreshLive(details); return; }
            var s = Host.Read(x => x.Snapshot()); var p = s.Plant;
            plantView.Configuration = activeConfig; plantView.Snapshot = s; plantView.InvalidateVisual();
            if (!details) return;
            Title = "AT-2 / AT-3 · Virtueller Förderbandteststand · OFFLINE";
            profileSubtitle.Text = "BEISPIELPROFIL · keine Laborkalibrierung · eigene Offline-Nachbildung";
            time.Text = $"{p.TimeUs / 1000000.0:F3} s\n{(s.Paused ? "PAUSE" : "LÄUFT")} · {s.Mode}";
            RunButton.IsEnabled = s.Paused && s.Mode != OperationMode.ExternalPlc && s.Quality == LinkQuality.Offline;
            PauseButton.IsEnabled = !s.Paused; StepButton.IsEnabled = s.Paused && s.Mode != OperationMode.ExternalPlc && s.Quality == LinkQuality.Offline;
            SourceSelect.IsEnabled = s.Paused && !RecoveryBusy;
            syncing = true; SourceSelect.SelectedIndex = (int)s.Mode; syncing = false;
            manualPanel.IsEnabled = s.Mode == OperationMode.OfflineManual;
            referencePanel.IsEnabled = s.Mode == OperationMode.OfflineReference;
            StopButton.Content = s.Controller.Stopped ? "Betrieb fortsetzen (angehalten)" : "Betrieb stoppen (freigegeben)";
            topStopButton.Content = s.Controller.Stopped ? "Betrieb fortsetzen" : "Betrieb stoppen";
            topStopButton.IsEnabled = s.Mode == OperationMode.OfflineReference;
            motion.Text = $"Befehl: rechts {(p.Actuators.MotorRight ? "EIN" : "AUS")} / links {(p.Actuators.MotorLeft ? "EIN" : "AUS")} · {Analog.OutputVoltage(p.Actuators.SpeedCommandRaw):F2} V ({p.Actuators.SpeedCommandRaw})\nIst: Motor {p.MotorVelocity:F2} mm/s · Prüfling {p.ObjectVelocity:F2} mm/s · linke Kante {p.LeftEdgeMm:F2} mm";
            measurement.Text = $"Controller-Messwert: {(s.Controller.MeasuredLengthMm.HasValue ? s.Controller.MeasuredLengthMm.Value.ToString("F2") + " mm" : "— noch kein gültiger Messwert")}\nGeometrische Referenz: {p.TrueLengthMm:F2} mm";
            diagnostic.Text = $"Modelltakt {activeConfig.ModelStepUs} µs · Abtastung {activeConfig.ControllerSampleUs} µs · Hall {(p.Sensors.HallHigh ? "HIGH" : "LOW")}\nErzeugte steigende Flanken: {p.GeneratedRisingEdges} · erfasst (Teststand): {Host.Read(x => x.Counter.Rising)} · Vor-/Rückwärtszähler: {Host.Read(x => x.Counter.Signed)}\n" +
                $"Erkennung ideal / elektrischer Pegel: Sensor 2 {(p.Left.Ideal ? 1 : 0)}/{(p.Left.High ? 1 : 0)} · Optik {(p.Optical.Ideal ? 1 : 0)}/{(p.Optical.High ? 1 : 0)} · Sensor 1 {(p.Right.Ideal ? 1 : 0)}/{(p.Right.High ? 1 : 0)}\n" +
                (p.AliasingRisk ? "ALIASINGWARNUNG: Abtastabstand verletzt p/(4·|v|). Impulse können fehlen. " : "Abtastreserve im Beispielprofil eingehalten. ") +
                (p.DirectionConflict ? "DirectionConflict: beide Richtungen befohlen; Antrieb neutral. " : "") + (p.ObjectOffBelt ? "ObjectOffBelt. " : "") +
                (p.MotorLocked ? "Motorwelle blockiert. " : "") + (p.ObjectBlocked ? "Prüfling blockiert. " : "") + (s.Paused ? "\n" + s.PauseReason : "");
            controllerInfo.Text = $"{s.Controller.State} · {s.Controller.MachineMode}\nMessimpulse: {s.Controller.MeasurementPulses}\n{s.Controller.Message}";
            if (Tabs.SelectedIndex == 0) { trace.Samples = Host.Read(x => x.Samples.Tail(1100).Where(a => a.TimeUs >= p.TimeUs - 1050000).ToArray()); trace.InvalidateVisual(); }
            if (Tabs.SelectedIndex == 2) calibrationTable.ItemsSource = Host.Read(x => x.Calibration.Runs.ToArray());
            calibrationInfo.Text = Host.Read(x => $"Fahrten: {x.Calibration.Runs.Count}/3 · Mittelwert: {(x.Calibration.Mean.HasValue ? x.Calibration.Mean.Value.ToString("F6") : "—")} mm/Impuls\nÜbernommene Konstante: {(x.Controller.Calibration.HasValue ? x.Controller.Calibration.Value.ToString("F6") : "fehlt")} mm/Impuls");
            if (calibrating) calibrationInfo.Text += $"\nMarkierung am Bandweg {calibrationMarkerMm:F2} mm · seither {p.BeltMm - calibrationMarkerMm:F2} mm / {Host.Read(x => x.Counter.Rising) - calibrationStart} erfasste Impulse. Strecke bewusst eintragen.";
            if (Tabs.SelectedIndex == 1) signals.ItemsSource = MakeSignals(s);
            if (Tabs.SelectedIndex == 6) events.ItemsSource = Host.Read(x => x.Events.Copy().Reverse().ToArray());
        }
        private SignalRow[] MakeSignals(SessionSnapshot s)
        {
            string Bool(bool v) => v ? "HIGH / 1" : "LOW / 0";
            var p = s.Plant; string quality = p.Actuators.Valid && p.Sensors.Valid && s.Quality == LinkQuality.Offline ? "gültig / offline" : "veraltet / nicht verbunden";
            string[] values = { Bool(p.Sensors.SensorLeftHigh), Bool(p.Sensors.OpticalHigh), Bool(p.Sensors.SensorRightHigh), Bool(p.Sensors.HallHigh), p.Sensors.PotiRaw.ToString(),
                Bool(p.Actuators.MotorRight), Bool(p.Actuators.MotorLeft), p.Actuators.SpeedCommandRaw.ToString(), Bool(p.Actuators.LampRed), Bool(p.Actuators.LampYellow), Bool(p.Actuators.LampGreen),
                s.Controller.MeasuredLengthMm?.ToString("F3") ?? "—", s.Controller.MeasurementPulses.ToString(), Bool(s.Controller.MeasurementActive) };
            return SignalContract.All.Select((d, i) => new SignalRow(d.Id + (d.Id == "SensorLeftHigh" ? " · Sensor 2" : d.Id == "SensorRightHigh" ? " · Sensor 1" : ""),
                d.Direction == "plant_to_plc" ? "Anlage → Steuerung" : d.Direction == "plc_to_plant" ? "Steuerung → Anlage" : "Steuerung → Diagnose",
                d.Direction == "plant_to_plc" ? "Anlagenmodell" : d.Direction == "plc_to_diagnostics" ? "Referenzcontroller" : s.Mode == OperationMode.OfflineManual ? "Teststandvorgabe" : "Eigener Referenzcontroller", values[i], quality, $"{p.TimeUs} µs · ungebunden")).ToArray();
        }
        private async void Export()
        {
            if (IsExternal) { ExportLive(); return; }
            var d = new SaveFileDialog { Filter = "Exportordner-Marker|*.export", FileName = "AT2_AT3_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".export" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(d.FileName)!, Path.GetFileNameWithoutExtension(d.FileName));
                var data = Host.Read(s => s.CaptureExport()); SetStatus("Aufzeichnung wird gespeichert…");
                await Task.Run(() => data.Save(path)); SetStatus("Aufzeichnung gespeichert: " + path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { SetStatus("Export fehlgeschlagen: " + e.Message, true); }
        }
        private void SaveSnapshot() => Safe(() => { var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "snapshot.json" }; if (d.ShowDialog(this) == true) Json.SaveAtomic(d.FileName, IsExternal ? Json.Write(new { source = "LiveProbe", connection = liveHost.Snapshot, plcMeasurement = "Unavailable", analogOutputs = "Unavailable" }) : Json.Write(Host.Read(s => s.Snapshot()))); });
        private void LoadProfile() => Safe(() =>
        {
            Numbers.Require(Host.Read(s => s.Paused), "Profil nur bei Simulatorpause laden.");
            var d = new OpenFileDialog { Filter = "JSON|*.json" }; if (d.ShowDialog(this) == true) Reset(Json.LoadProfile(d.FileName));
        });
        private void SaveProfile() => Safe(() =>
        {
            var c = Json.Read<PlantConfiguration>(profileEditor.Text); c.Validate();
            var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "plant.example.json" }; if (d.ShowDialog(this) == true) Json.SaveAtomic(d.FileName, Json.Write(c));
        });
        private void CheckMap() => Safe(() =>
        {
            var d = new OpenFileDialog { Filter = "JSON|*.json" }; if (d.ShowDialog(this) != true) return;
            var map = Json.Read<SignalMap>(File.ReadAllText(d.FileName)); var problems = map.Validate(true);
            SetStatus(problems.Count == 0 ? "Vollständige Signalmap formal gültig; damit ist keine Liveverifikation verbunden." : string.Join("; ", problems), problems.Count != 0);
        });
    }
}
