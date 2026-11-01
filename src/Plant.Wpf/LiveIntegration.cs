using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed partial class MainWindow
    {
        private readonly LiveProcessHost liveHost = new();
        internal LiveProcessHost LiveProcess => liveHost;
        private readonly List<InputSample> liveSamples = new();
        private readonly List<PlantEvent> liveEvents = new();
        private StackPanel liveSettings = null!;
        internal CheckBox RuntimeConsent = null!, CpuConsent = null!, TiaConsent = null!;
        internal Button LiveConnect = null!, LiveDisconnect = null!;
        private ComboBox liveRunKind = null!;
        private TextBox liveMapPath = null!, livePlantPath = null!, liveSpeed = null!, liveInitial = null!, liveLength = null!, liveHallPitch = null!;
        private TextBox liveSteps = null!, liveVirtualSeconds = null!, liveWallSeconds = null!;
        private PlantConfiguration? liveConfiguration;
        private DigitalCycleProfile? liveProfile;
        private SignalMap? liveMap;
        private SessionSnapshot? lastLiveView;
        private string lastLiveDetail = "";
        private string liveConfigurationHash = "";
        private PlantConfiguration? previewConfiguration;
        private string previewKey = "";
        private static readonly ControllerTelemetry UnavailablePlcTelemetry = new(CycleState.Ready, false, MachineMode.Automatic, 0, 0, 0, null, false, "Unavailable: keine SPS-Telemetriebindung");
        private bool closingAfterLive;
        private bool liveWorkflowBusy;
        private bool IsExternal => Host.Read(s => s.Mode) == OperationMode.ExternalPlc;

        private UIElement BuildLivePanel()
        {
            var layout = new StackPanel();
            liveSettings = Ui.Section(layout, "SPS-Durchlauf · AT2_Kopplung");
            liveSettings.Children.Add(Ui.Text("Lokal · PLCSIM-Softbus · sieben digitale Signale", 12));
            liveRunKind = Ui.Select(liveSettings, "Testart", "Stationärer Austausch", "Förderbanddurchlauf");
            var dimensions = new Grid(); dimensions.ColumnDefinitions.Add(new ColumnDefinition()); dimensions.ColumnDefinitions.Add(new ColumnDefinition());
            var startField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; var lengthField = new StackPanel();
            dimensions.Children.Add(startField); Grid.SetColumn(lengthField, 1); dimensions.Children.Add(lengthField); liveSettings.Children.Add(dimensions);
            liveInitial = Ui.Number(startField, "Startposition [mm]", 50);
            liveLength = Ui.Number(lengthField, "Prüflingslänge [mm]", 120);
            RuntimeConsent = Ui.Check("Siemens-Zugriff auf AT2_Kopplung freigeben");
            CpuConsent = Ui.Check("Begrenzten Lauf und automatische Rückkehr zu Default freigeben");
            TiaConsent = Ui.Check("TIA-Teststand und Motorfreigaben passen zur Testart");
            liveSettings.Children.Add(RuntimeConsent); liveSettings.Children.Add(CpuConsent); liveSettings.Children.Add(TiaConsent);
            foreach (var box in new[] { RuntimeConsent, CpuConsent, TiaConsent })
            { box.Checked += (_, _) => RefreshLiveControls(); box.Unchecked += (_, _) => RefreshLiveControls(); }
            var advanced = Ui.Fold(liveSettings, "Testparameter und Dateien");
            advanced.Children.Add(Ui.Text("OB1-Prozessabbild automatisch. Stationärer Austausch verlangt ausgeschaltete Motorbefehle. Im Förderbanddurchlauf steuern die SPS-Ausgänge das Modell.", 12));
            liveSpeed = Ui.Number(advanced, "C#-Testgeschwindigkeit [mm/s, max. 100]", 100);
            liveHallPitch = Ui.Number(advanced, "Hallteilung [mm/Impuls] · Testfaktor", 1.2);
            liveSteps = Ui.Number(advanced, "Maximale CPU-Fortschaltungen [1…50000]", 12);
            liveVirtualSeconds = Ui.Number(advanced, "Maximale virtuelle Laufzeit [s, max. 60]", 1);
            liveWallSeconds = Ui.Number(advanced, "Timeout [s, 5…300; Prozessfrist +10 s]", 15);
            string root = FindProjectDirectory();
            liveMapPath = Ui.Number(advanced, "Bestätigte digitale Signalmap (Dateipfad)", 0);
            liveMapPath.Text = Path.Combine(root, "config", "io-map.AT2_Kopplung.partial.json");
            var row = Ui.Row(advanced);
            row.Children.Add(Ui.Button("Digitale Signalmap auswählen…", () => Safe(() =>
            {
                var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
                if (dialog.ShowDialog(this) == true) liveMapPath.Text = dialog.FileName;
            })));
            row.Children.Add(Ui.Button("Sieben digitale Bindungen prüfen", () => Safe(() =>
            {
                new DigitalCycleProfile { Ob1AutomaticImageConfirmed = true }.RequireMap(Json.Read<SignalMap>(File.ReadAllText(liveMapPath.Text)));
                SetStatus("Digitale Bindungen formal geprüft: I0.0…I0.3 und Q0.0…Q0.2. Keine API gestartet; vollständige Signalmap-Validierung unverändert.");
            })));
            row.Children.Add(Ui.Button("Vollständige Signalmap prüfen…", CheckMap));
            livePlantPath = Ui.Number(advanced, "Digitales Anlagen-Testprofil (Dateipfad)", 0);
            livePlantPath.Text = Path.Combine(root, "config", "plant.digital-test.json");
            liveRunKind.SelectionChanged += (_, _) =>
            {
                if (liveSteps == null) return;
                bool transport = liveRunKind.SelectedIndex == 1;
                liveSteps.Text = transport ? "20000" : "12";
                liveVirtualSeconds.Text = transport ? "15" : "1";
                liveWallSeconds.Text = transport ? "120" : "15";
                if (TiaConsent != null) TiaConsent.IsChecked = false;
            };
            advanced.Children.Add(Ui.Text("Geschwindigkeit und Hallteilung sind Modell-Testwerte, keine analoge SPS-Vorgabe oder Laborkalibrierung. TIA-Testfaktor und Hallteilung müssen übereinstimmen. Messwerte_DB ist nicht angebunden.", 12));
            LiveConnect = Ui.Button("SPS-Durchlauf starten", ConnectLive);
            LiveDisconnect = Ui.Button("Trennen / Test beenden", DisconnectLive);
            layout.Children.Add(BuildDefaultRecoveryPanel());
            layout.Children.Add(Ui.Text("Nach dem Lauf bleibt die letzte Anlagenansicht stehen. Die CPU wird nach bestätigtem Abschluss automatisch für den nächsten Lauf freigegeben. Kein automatischer Folgelauf.", 12));
            return layout;
        }

        private static string FindProjectDirectory()
        {
            for (var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); folder != null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "AT2AT3.sln"))) return folder.FullName;
            return AppDomain.CurrentDomain.BaseDirectory;
        }
        private static string FindProbe(string root)
        {
            var candidates = new[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "live-probe", "AT2AT3.V5.LiveProbe.exe"),
                Path.Combine(root, "tools", "LiveProbe", "bin", "Release", "net48", "AT2AT3.V5.LiveProbe.exe") };
            return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("LiveProbe fehlt. Optionalen V5.0-Build tools/LiveProbe/LiveProbe.csproj kompilieren oder dessen Ausgabedateien in live-probe neben die WPF-Anwendung legen.");
        }
        private static int Integer(TextBox box)
        { double value = Ui.Value(box); Numbers.Require(value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue, "Eine ganze Zahl ist erforderlich."); return (int)value; }

        private async void ConnectLive()
        {
            if (liveWorkflowBusy) return;
            liveWorkflowBusy = true;
            try
            {
                Numbers.Require(!liveHost.Snapshot.IsActive && !RecoveryBusy && Host.Read(s => s.Paused), "Offlinebetrieb zuerst pausieren bzw. laufenden SPS-Test und CPU-Freigabe abwarten.");
                Numbers.Require(RuntimeConsent.IsChecked == true && CpuConsent.IsChecked == true && TiaConsent.IsChecked == true, "Alle drei Freigaben müssen bewusst gesetzt werden.");
                string root = FindProjectDirectory(), probe = FindProbe(root);
                var profile = new DigitalCycleProfile { TestSpeedMmPerSecond = Ui.Value(liveSpeed), Ob1AutomaticImageConfirmed = true };
                var map = Json.Read<SignalMap>(File.ReadAllText(liveMapPath.Text)); profile.RequireMap(map);
                var config = Json.LoadProfile(livePlantPath.Text);
                config.ProfileId = "WPF DIGITALER TEST · Beispielgeometrie, Testfaktor, keine Laborkalibrierung";
                config.InitialLeftMm = Ui.Value(liveInitial); config.WorkpieceLengthMm = Ui.Value(liveLength);
                config.HallPitchMm = Ui.Value(liveHallPitch); config.CalibrationMmPerPulse = null;
                config = profile.ConfigurePlant(config);
                bool transport = liveRunKind.SelectedIndex == 1;
                var limits = new DigitalRunLimits { MaxAdvances = Integer(liveSteps), MaxVirtualSeconds = Ui.Value(liveVirtualSeconds),
                    WallTimeoutSeconds = Integer(liveWallSeconds), RequireStationary = !transport, RequireCompletedTransport = transport };
                limits.Validate();
                string run = Path.Combine(root, "artifacts", "wpf-live", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string mapPath = Path.Combine(run, "io-map.confirmed.json"), profilePath = Path.Combine(run, "digital-profile.json"), plantPath = Path.Combine(run, "plant.json"), reportPath = Path.Combine(run, "report.json");
                Json.SaveAtomic(mapPath, Json.Write(map)); Json.SaveAtomic(profilePath, Json.Write(profile)); Json.SaveAtomic(plantPath, Json.Write(config));
                Host.Change(s => { s.Pause(); s.SetMode(OperationMode.ExternalPlc); });
                liveConfiguration = config; liveConfigurationHash = config.Hash(); liveProfile = profile; liveMap = map;
                liveSamples.Clear(); liveEvents.Clear(); lastLiveDetail = ""; lastLiveView = null;
                SetStatus("Manuell freigegebener digitaler Test startet. Modell wird ausschließlich durch SPS-Ausgänge im LiveProbe-Prozess bewegt.");
                var running = liveHost.RunAsync(new LiveProcessRequest { ProbePath = probe, WorkingDirectory = root,
                    MapPath = mapPath, ProfilePath = profilePath, PlantPath = plantPath, ReportPath = reportPath,
                    Limits = limits, RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true });
                Refresh(); await running;
                var result = liveHost.Snapshot;
                SetStatus(result.Detail + (result.Error == null ? "" : "\n" + result.Error), result.State == LiveProcessState.Faulted);
                await RestoreDefaultAfterRunAsync(result);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            { SetStatus("Verbinden nicht ausgeführt oder beendet: " + e.Message, true); }
            finally
            {
                // Session approvals persist for repeated explicitly started runs. Changing
                // the test kind invalidates the TIA confirmation above; nothing auto-starts.
                liveWorkflowBusy = false; Refresh();
            }
        }
        private async void DisconnectLive()
        { await liveHost.StopAsync(); Refresh(); SetStatus(liveHost.Snapshot.Detail, liveHost.Snapshot.State == LiveProcessState.Faulted); }
        private async void OnClosingLive(object? sender, CancelEventArgs e)
        {
            if (closingAfterLive) return;
            if (RecoveryRunning || (liveWorkflowBusy && !liveHost.Snapshot.IsActive)) { e.Cancel = true; SetStatus("Der Laufabschluss bzw. die begrenzte CPU-Freigabe läuft noch. Bitte deren Abschluss abwarten."); return; }
            if (closingAfterLive || !liveHost.Snapshot.IsActive) return;
            e.Cancel = true; IsEnabled = false;
            await liveHost.StopAsync();
            await RestoreDefaultAfterRunAsync(liveHost.Snapshot);
            if (RecoveryRunning) { IsEnabled = true; return; }
            closingAfterLive = true; Close();
        }

        private void RefreshLiveControls()
        {
            if (LiveConnect == null) return;
            var state = liveHost.Snapshot;
            bool external = IsExternal;
            liveSettings.IsEnabled = !state.IsActive && !RecoveryBusy && !liveWorkflowBusy;
            LiveConnect.IsEnabled = !state.IsActive && !RecoveryBusy && !liveWorkflowBusy && Host.Read(s => s.Paused) && RuntimeConsent.IsChecked == true && CpuConsent.IsChecked == true && TiaConsent.IsChecked == true;
            LiveDisconnect.IsEnabled = state.IsActive && !state.StopRequested && state.State != LiveProcessState.Stopping;
            liveStopButton.IsEnabled = LiveDisconnect.IsEnabled;
            offlineSidebar.Visibility = external ? Visibility.Collapsed : Visibility.Visible;
            liveSidebar.Visibility = external ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in new[] { RunButton, PauseButton, StepButton, resetButton, topStopButton, at3Button }) button.Visibility = external ? Visibility.Collapsed : Visibility.Visible;
            LiveConnect.Visibility = liveStopButton.Visibility = external ? Visibility.Visible : Visibility.Collapsed;
            RefreshDefaultRecoveryControls();
            resetButton.IsEnabled = at3Button.IsEnabled = !external && !state.IsActive;
            foreach (var tool in offlineTools) tool.IsEnabled = !external && !state.IsActive;
            for (int i = 2; i <= 4; i++) ((TabItem)Tabs.Items[i]).IsEnabled = !external && !state.IsActive;
            adapterStatus.Text = $"Status: {StateLabel(state.State)}\n{state.Detail}" +
                (state.ReportPath.Length == 0 ? "" : $"\nBericht: {state.ReportPath}\nCleanup: {(state.CleanupConfirmed ? "bestätigt" : "noch nicht bestätigt")} · CPU vor Trennen: {state.CpuBeforeDisconnect}") +
                (!state.IsActive && state.ReportPath.Length > 0 ? $"\nLaufprüfung: {(state.Passed ? "BESTANDEN" : "nicht bestanden / abgebrochen")} · vollständiger Transport: {(state.TransportCompleted ? "im Bericht bestätigt" : "nicht bestätigt")}" : "") +
                "\nSPS-Längenmessung: Unavailable · keine DB-Werte gelesen.";
        }
        private static string StateLabel(LiveProcessState state) => state switch
        {
            LiveProcessState.Connecting => "Verbindungsaufbau", LiveProcessState.Streaming => "Verbunden · digitaler Test aktiv",
            LiveProcessState.Finalizing => "Austausch beendet · Abschlussbericht wird geschrieben",
            LiveProcessState.Stopping => "Trennen läuft", LiveProcessState.Completed => "Getrennt · begrenzter Lauf beendet",
            LiveProcessState.Faulted => "Fehler · Daten ungültig", _ => "Nicht verbunden"
        };

        private void RefreshLive(bool details)
        {
            var connection = liveHost.Snapshot;
            foreach (var sample in liveHost.DrainSamples())
            {
                long us = sample.TimeNs / 1000;
                liveSamples.Add(new InputSample(us, sample.Sequence, OperationMode.ExternalPlc,
                    new SensorFrame(sample.Left, sample.Optical, sample.Right, sample.Hall, 0, true, sample.Sequence, us),
                    new ActuatorFrame(sample.MotorRight, sample.MotorLeft, 0, false, false, sample.LampGreen, true, sample.Sequence, us), UnavailablePlcTelemetry));
            }
            if (liveSamples.Count > 0)
            {
                long start = liveSamples[liveSamples.Count - 1].TimeUs - 1050000;
                liveSamples.RemoveAll(s => s.TimeUs < start);
                if (liveSamples.Count > 50000) liveSamples.RemoveRange(0, liveSamples.Count - 50000);
            }
            DisplayLive(connection, details);
        }
        // Presentation-only entry point: accepts data, never creates an API connection or advances a model.
        internal void DisplayLive(LiveProcessSnapshot connection, bool details = true)
        {
            bool valid = connection.IsActive && connection.State == LiveProcessState.Streaming;
            var latest = connection.Latest;
            lastLiveView = latest == null ? null : CreateLiveView(latest, valid);
            var config = liveConfiguration ?? activeConfig;
            if (latest == null)
            {
                // A static preview is separate from live data; it never supplies sensor values.
                string key = config.GetHashCode() + "|" + liveInitial.Text + "|" + liveLength.Text + "|" + liveHallPitch.Text;
                if (previewConfiguration == null || (details && key != previewKey))
                {
                    var preview = config.Copy();
                    if (double.TryParse(liveInitial.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double start) && Numbers.Finite(start)) preview.InitialLeftMm = start;
                    if (double.TryParse(liveLength.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double length) && Numbers.Finite(length) && length > 0 && length <= preview.BeltLengthMm) preview.WorkpieceLengthMm = length;
                    if (double.TryParse(liveHallPitch.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double pitch) && Numbers.Finite(pitch) && pitch > 0) preview.HallPitchMm = pitch;
                    try { preview.Validate(); previewConfiguration = preview; }
                    catch (ArgumentException) { previewConfiguration = config; }
                    previewKey = key;
                }
                config = previewConfiguration;
            }
            plantView.Configuration = config;
            plantView.Snapshot = lastLiveView; plantView.InvalidateVisual();
            if (!details) return;
            if (Tabs.SelectedIndex == 0) { trace.Samples = liveSamples.ToArray(); trace.InvalidateVisual(); }
            Title = "AT-2 / AT-3 · SPS AT2_Kopplung · " + StateLabel(connection.State);
            profileSubtitle.Text = "DIGITALER SPS-TEST · Beispielgeometrie · C#-Testgeschwindigkeit · SPS-Messwerte ungebunden";
            time.Text = latest == null ? "KEINE LIVEdaten\nExternalPlc" : $"{latest.TimeNs / 1e9:F6} s · #{latest.Sequence}\n{(valid ? "LIVE · SingleStep_C" : "LETZTER WERT · ungültig")}";
            RunButton.IsEnabled = PauseButton.IsEnabled = StepButton.IsEnabled = topStopButton.IsEnabled = false;
            manualPanel.IsEnabled = referencePanel.IsEnabled = false; SourceSelect.IsEnabled = !connection.IsActive && !RecoveryBusy && !liveWorkflowBusy;
            syncing = true; SourceSelect.SelectedIndex = 2; syncing = false;
            motion.Text = latest == null ? "Noch kein SPS-Snapshot. Links Testart und Freigaben prüfen, dann oben starten." :
                $"SPS-Befehle: Motor rechts {(latest.MotorRight ? "EIN" : "AUS")} · Motor links {(latest.MotorLeft ? "EIN" : "AUS")} · Lampe Grün {(latest.LampGreen ? "EIN" : "AUS")}\n" +
                $"Modell: Motor {latest.MotorVelocity:F2} mm/s · Prüfling {latest.ObjectVelocity:F2} mm/s · linke Kante {latest.Position:F2} mm" +
                (valid ? "" : connection.State == LiveProcessState.Finalizing ? "\nAustausch beendet: Abschlussbericht wird geschrieben. Angezeigte Werte sind historisch und kein Erfolgsnachweis." : "\nVerbindung nicht aktiv: angezeigte Werte sind historisch, das Modell schreitet nicht fort.");
            measurement.Text = "SPS-Messwert: — nicht angebunden (Unavailable)\n" +
                (latest == null ? "" : $"Geometrische Referenz des Modells: {latest.Length:F2} mm · kein SPS-Messergebnis");
            diagnostic.Text = $"Temporäre C#-Testgeschwindigkeit: {(liveProfile?.TestSpeedMmPerSecond ?? 100):F2} mm/s · keine analoge SPS-Vorgabe\n" +
                $"Hallteilung im Modell: {config.HallPitchMm:F3} mm/Impuls · Testannahme\n" +
                (latest == null ? "Keine Liveimpulse empfangen." : $"Erzeugte steigende Modellflanken: {latest.GeneratedRisingEdges} · kein ausgelesener SPS-Zähler\n" +
                    $"Erkannt / elektrischer Pegel: links {Bit(latest.LeftDetected)}/{Bit(latest.Left)} · Optik {Bit(latest.OpticalDetected)}/{Bit(latest.Optical)} · rechts {Bit(latest.RightDetected)}/{Bit(latest.Right)}\n") +
                $"CPU vor Trennen: {connection.CpuBeforeDisconnect} · Cleanup {(connection.CleanupConfirmed ? "bestätigt" : "unbestätigt")}\n" +
                "Offline-Referenzcontroller pausiert. SPS-Länge und SPS-Zähler im TIA-Messwerte_DB gesondert prüfen.";
            controllerInfo.Text = "ExternalPlc · eigener Referenzcontroller pausiert";
            if (connection.Detail != lastLiveDetail)
            {
                lastLiveDetail = connection.Detail;
                liveEvents.Add(new PlantEvent(liveEvents.Count + 1, latest?.TimeNs / 1000 ?? 0, connection.State.ToString(), connection.Detail));
                if (liveEvents.Count > 1000) liveEvents.RemoveAt(0);
                SetStatus(connection.Detail + (connection.Error == null ? "" : "\n" + connection.Error), connection.State == LiveProcessState.Faulted);
            }
            if (Tabs.SelectedIndex == 1) signals.ItemsSource = MakeLiveSignals(latest, valid);
            if (Tabs.SelectedIndex == 6) events.ItemsSource = liveEvents.AsEnumerable().Reverse().ToArray();
        }
        private static string Bit(bool value) => value ? "1" : "0";
        private SessionSnapshot CreateLiveView(DigitalProgress sample, bool valid)
        {
            var config = liveConfiguration ?? activeConfig;
            long us = sample.TimeNs / 1000;
            var sensors = new SensorFrame(sample.Left, sample.Optical, sample.Right, sample.Hall, 0, valid, sample.Sequence, us);
            // SpeedCommandRaw/PotiRaw/red/yellow are intentionally unbound. They are never displayed as SPS values.
            var actuators = new ActuatorFrame(sample.MotorRight, sample.MotorLeft, 0, false, false, sample.LampGreen, valid, sample.Sequence, us);
            var plant = new PlantSnapshot(us, sample.Sequence, sample.MotorMm, sample.BeltMm, sample.MotorVelocity, sample.ObjectVelocity,
                sample.Present, sample.Position, sample.Length, new(sample.LeftIdeal, sample.LeftDetected, sample.Left),
                new(sample.OpticalIdeal, sample.OpticalDetected, sample.Optical), new(sample.RightIdeal, sample.RightDetected, sample.Right),
                sensors, actuators, sample.GeneratedRisingEdges, sample.MotorRight && sample.MotorLeft,
                sample.Present && (sample.Position + sample.Length <= 0 || sample.Position >= config.BeltLengthMm), false, false, false, 1);
            return new SessionSnapshot(plant, UnavailablePlcTelemetry, OperationMode.ExternalPlc, !valid, valid ? LinkQuality.Connected : LinkQuality.Stale,
                valid ? "" : "Historischer Snapshot; Verbindung nicht aktiv.", liveConfigurationHash);
        }
        internal SignalRow[] MakeLiveSignals(DigitalProgress? sample, bool valid)
        {
            var values = sample == null ? new Dictionary<string, bool>() : new Dictionary<string, bool>
            {
                ["SensorLeftHigh"] = sample.Left, ["OpticalHigh"] = sample.Optical, ["SensorRightHigh"] = sample.Right, ["HallHigh"] = sample.Hall,
                ["MotorRight"] = sample.MotorRight, ["MotorLeft"] = sample.MotorLeft, ["LampGreen"] = sample.LampGreen
            };
            return SignalContract.All.Select(d =>
            {
                bool bound = DigitalCycleProfile.SignalIds.Contains(d.Id);
                string value = values.TryGetValue(d.Id, out bool bit) ? (bit ? "HIGH / 1" : "LOW / 0") : "—";
                var binding = liveMap?.Signals.SingleOrDefault(s => s.Id == d.Id && s.Confirmed && s.ByteOffset.HasValue);
                string address = binding == null ? "keine bestätigte Bindung geladen" : (binding.Area == "Input" ? "I" : "Q") + binding.ByteOffset + "." + binding.BitOffset;
                return new SignalRow(d.Id, d.Direction == "plant_to_plc" ? "C#-Anlage → SPS" : d.Direction == "plc_to_plant" ? "SPS → C#-Anlage" : "SPS → Diagnose",
                    bound ? d.Direction == "plant_to_plc" ? "Anlagenmodell im LiveProbe" : "SPS AT2_Kopplung" : "nicht angebunden", value,
                    !bound ? "Unavailable" : sample == null ? "nicht verbunden" : valid ? "gültig / digital live" : "veraltet / ungültig",
                    !bound ? "ungebunden" : $"{sample?.TimeNs ?? 0} ns · {address}");
            }).ToArray();
        }
        private void ExportLive() => Safe(() =>
        {
            var state = liveHost.Snapshot;
            Numbers.Require(!state.IsActive, "Den begrenzten Test zuerst beenden; Abschlussbericht wird danach exportiert.");
            Numbers.Require(File.Exists(state.ReportPath), "Noch kein fachlicher Livebericht vorhanden. Status und Prozesslogs im Bereich Siemens prüfen.");
            var dialog = new SaveFileDialog { Filter = "JSON-Bericht|*.json", FileName = "AT2_Kopplung-live-report.json" };
            if (dialog.ShowDialog(this) == true) { Json.SaveAtomic(dialog.FileName, File.ReadAllText(state.ReportPath)); SetStatus("Tatsächlicher LiveProbe-Bericht exportiert: " + dialog.FileName); }
        });
    }
}
