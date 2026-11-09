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
        internal Button LiveInsertWorkpiece = null!, LiveRemoveWorkpiece = null!;
        internal Button LiveZeroBandMarking = null!;
        private TextBlock liveWorkpieceStatus = null!;
        private bool? liveDraftPresent;
        private bool livePreviewEdited, workpieceEditing;
        private ComboBox liveRunKind = null!;
        internal ComboBox LiveRunKind => liveRunKind;
        private TextBlock liveRunHint = null!, liveRunOutcome = null!;
        private StackPanel liveLimitFields = null!;
        private bool InteractiveConnection => liveRunKind.SelectedIndex == 2;
        private TextBox liveSpeed = null!, liveInitial = null!, liveLength = null!, liveHallPitch = null!;
        private TextBox liveSteps = null!, liveVirtualSeconds = null!, liveWallSeconds = null!;
        private PlantConfiguration? liveConfiguration;
        private DigitalCycleProfile? liveProfile;
        internal Slider LivePotentiometer = null!;
        private StackPanel livePotentiometerPanel = null!;
        private TextBlock potentiometerReadback = null!;
        private bool potentiometerSending;
        private double requestedPotentiometer;
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
            var workpiecePanel = Ui.Section(layout, "Prüfling · nur bei Stillstand");
            livePotentiometerPanel = Ui.Section(layout, "Potentiometer – SPS-Board, Channel 5");
            LivePotentiometer = new Slider { Minimum = 0, Maximum = 10, SmallChange = 0.1, LargeChange = 1, TickFrequency = 1, Margin = new Thickness(4, 10, 4, 10) };
            System.Windows.Automation.AutomationProperties.SetName(LivePotentiometer, "Potentiometer – SPS-Board, Channel 5");
            livePotentiometerPanel.Children.Add(LivePotentiometer);
            potentiometerReadback = Ui.Text("0,00 V · Rohwert 0", 12); livePotentiometerPanel.Children.Add(potentiometerReadback);
            livePotentiometerPanel.Children.Add(Ui.Text("Motoransteuerungsmodul: eigenes Potentiometer fest auf 0 Ω · linker Anschlag", 12));
            LivePotentiometer.ValueChanged += async (_, _) =>
            {
                requestedPotentiometer = LivePotentiometer.Value;
                if (potentiometerSending || !liveHost.Snapshot.IsActive) return;
                potentiometerSending = true;
                try
                {
                    double sent;
                    do { sent = requestedPotentiometer; await liveHost.SetPotentiometerAsync(sent); }
                    while (sent != requestedPotentiometer && liveHost.Snapshot.IsActive);
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is TimeoutException)
                { SetStatus("Potentiometer nicht übertragen: " + e.Message, true); }
                finally { potentiometerSending = false; }
            };
            liveSettings = Ui.Section(layout, "SPS-Verbindung");
            liveSettings.Children.Add(Ui.Text("Lokale virtuelle SPS · PLCSIM-Softbus", 12));
            var testModes = Ui.Fold(liveSettings, "Erweiterte Testarten");
            liveRunKind = Ui.Select(testModes, "Betriebsart", "Stationärer E/A-Test", "Förderband-Abnahmetest", "Interaktive Verbindung");
            liveRunHint = Ui.Text(LiveStatusText.Hint(false), 12); testModes.Children.Add(liveRunHint);
            var dimensions = new Grid(); dimensions.ColumnDefinitions.Add(new ColumnDefinition()); dimensions.ColumnDefinitions.Add(new ColumnDefinition());
            var startField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; var lengthField = new StackPanel();
            dimensions.Children.Add(startField); Grid.SetColumn(lengthField, 1); dimensions.Children.Add(lengthField); liveSettings.Children.Add(dimensions);
            liveInitial = Ui.Number(startField, "Links [mm]", 50);
            liveInitial.IsReadOnly = true;
            liveLength = Ui.Number(lengthField, "Prüflingslänge [mm]", 120);
            RuntimeConsent = Ui.Check("Siemens-Zugriff freigeben");
            CpuConsent = Ui.Check("CPU-Synchronisierung freigeben; beim Trennen zurück zu Default");
            TiaConsent = Ui.Check("Passendes Programm geladen; kein TIA-Ladevorgang aktiv");
            liveSettings.Children.Add(RuntimeConsent); liveSettings.Children.Add(CpuConsent); liveSettings.Children.Add(TiaConsent);
            liveRunOutcome = Ui.Text("Noch kein SPS-Test gestartet.", 12, true); layout.Children.Add(liveRunOutcome);
            foreach (var box in new[] { RuntimeConsent, CpuConsent, TiaConsent })
            { box.Checked += (_, _) => RefreshLiveControls(); box.Unchecked += (_, _) => RefreshLiveControls(); }
            var advanced = Ui.Fold(liveSettings, "Testgrenzen und Modellwerte");
            advanced.Children.Add(Ui.Text("OB1-Prozessabbild automatisch. Stationärer Austausch verlangt ausgeschaltete Motorbefehle. Im Förderbanddurchlauf steuern die SPS-Ausgänge das Modell.", 12));
            liveSpeed = Ui.Number(advanced, "C#-Testgeschwindigkeit [mm/s, max. 100]", 100);
            liveHallPitch = Ui.Number(advanced, "Hallteilung [mm/Impuls] · Testfaktor", 1.2);
            liveLimitFields = new StackPanel(); advanced.Children.Add(liveLimitFields);
            liveSteps = Ui.Number(liveLimitFields, "Maximale CPU-Fortschaltungen [1…50000]", 12);
            liveVirtualSeconds = Ui.Number(liveLimitFields, "Maximale virtuelle Laufzeit [s, max. 60]", 1);
            liveWallSeconds = Ui.Number(liveLimitFields, "Timeout [s, 5…300; Prozessfrist +10 s]", 15);
            liveRunKind.SelectionChanged += (_, _) =>
            {
                if (liveSteps == null) return;
                bool transport = liveRunKind.SelectedIndex == 1;
                bool interactive = InteractiveConnection;
                liveSteps.Text = transport ? "20000" : "12";
                liveVirtualSeconds.Text = transport ? "15" : "1";
                liveWallSeconds.Text = transport ? "120" : "15";
                liveRunHint.Text = LiveStatusText.Hint(transport, interactive);
                liveLimitFields.Visibility = interactive ? Visibility.Collapsed : Visibility.Visible;
                if (interactive) { liveDraftPresent = false; livePreviewEdited = true; previewConfiguration = null; }
                if (LiveConnect != null)
                {
                    LiveConnect.Content = LiveStatusText.StartButton(transport, interactive);
                    System.Windows.Automation.AutomationProperties.SetName(LiveConnect, LiveStatusText.StartButton(transport, interactive));
                }
                if (TiaConsent != null) TiaConsent.IsChecked = false;
            };
            advanced.Children.Add(Ui.Text("Geschwindigkeit und Hallteilung sind Modell-Testwerte, keine analoge SPS-Vorgabe oder Laborkalibrierung. TIA-Testfaktor und Hallteilung müssen übereinstimmen.", 12));
            LiveConnect = Ui.Button(LiveStatusText.StartButton(false), ConnectLive);
            LiveDisconnect = Ui.Button("SPS-Verbindung trennen", DisconnectLive);
            var workpieceRow = Ui.Row(workpiecePanel);
            LiveInsertWorkpiece = Ui.Button("Prüfling einsetzen (links)", async () => await ChangeLiveWorkpieceAsync(true));
            LiveRemoveWorkpiece = Ui.Button("Prüfling entfernen", async () => await ChangeLiveWorkpieceAsync(false));
            workpieceRow.Children.Add(LiveInsertWorkpiece); workpieceRow.Children.Add(LiveRemoveWorkpiece);
            liveWorkpieceStatus = Ui.Text("", 12); workpiecePanel.Children.Add(liveWorkpieceStatus);
            LiveZeroBandMarking = Ui.Button("Bandmarkierung auf 0", ZeroBandMarking);
            LiveZeroBandMarking.ToolTip = "Nur bei frisch bestätigtem Stillstand und ausgeschalteten Motorbefehlen. Setzt die sichtbare Markierung auf 0 mm; Bandweg und SPS-Zähler bleiben unverändert.";
            workpiecePanel.Children.Add(LiveZeroBandMarking);
            layout.Children.Add(BuildDefaultRecoveryPanel());

            liveRunKind.SelectedIndex = 2;
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
                var profile = selectedProfile.DigitalSettings(); profile.TestSpeedMmPerSecond = Ui.Value(liveSpeed);
                if (profile.Analog != null) profile.Analog.InitialPotentiometerVolts = LivePotentiometer.Value;
                var map = Json.Read<SignalMap>(Json.Write(selectedProfile.Map)); profile.RequireMap(map);
                var config = selectedProfile.Plant.Copy();
                config.ProfileId = "WPF DIGITALER TEST · Beispielgeometrie, Testfaktor, keine Laborkalibrierung";
                config.InitialLeftMm = Ui.Value(liveInitial); config.WorkpieceLengthMm = Ui.Value(liveLength);
                if (liveDraftPresent.HasValue) config.InitialPresent = liveDraftPresent.Value;
                if (InteractiveConnection) config.InitialPresent = false;
                config.HallPitchMm = Ui.Value(liveHallPitch); config.CalibrationMmPerPulse = null;
                config = profile.ConfigurePlant(config);
                bool transport = liveRunKind.SelectedIndex == 1;
                var limits = new DigitalRunLimits { MaxAdvances = Integer(liveSteps), MaxVirtualSeconds = Ui.Value(liveVirtualSeconds),
                    WallTimeoutSeconds = Integer(liveWallSeconds), RequireStationary = !transport && !InteractiveConnection,
                    RequireCompletedTransport = transport, InteractiveSession = InteractiveConnection };
                limits.Validate();
                string run = Path.Combine(root, "artifacts", "wpf-live", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string mapPath = Path.Combine(run, "io-map.confirmed.json"), profilePath = Path.Combine(run, "digital-profile.json"), plantPath = Path.Combine(run, "plant.json"), reportPath = Path.Combine(run, "report.json");
                Json.SaveAtomic(mapPath, Json.Write(map)); Json.SaveAtomic(profilePath, Json.Write(profile)); Json.SaveAtomic(plantPath, Json.Write(config));
                Host.Change(s => { s.Pause(); s.SetMode(OperationMode.ExternalPlc); });
                liveConfiguration = config; liveConfigurationHash = config.Hash(); liveProfile = profile; liveMap = map;
                liveSamples.Clear(); liveEvents.Clear(); lastLiveDetail = ""; lastLiveView = null; plantView.Marking.Reset();
                livePreviewEdited = false; liveDraftPresent = null;
                SetStatus(InteractiveConnection ? "Verbinde leere Anlage. Motorbefehle kommen ausschließlich aus der SPS." : "Begrenzter SPS-Test startet.");
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
            liveRunOutcome.Text = LiveStatusText.Outcome(state);
            liveRunOutcome.Foreground = state.State == LiveProcessState.Faulted ? Ui.Warning : Ui.Ink;
            liveSettings.IsEnabled = !state.IsActive && !RecoveryBusy && !liveWorkflowBusy;
            if (profileControls != null) profileControls.IsEnabled = CanEditProfiles;
            var profileProblems = selectedProfile?.ConnectionProblems() ?? new[] { "Profil wählen." };
            LiveConnect.ToolTip = profileProblems.Length == 0 ? "Leere Anlage verbinden. Anschließend Prüfling einsetzen." : string.Join("\n", profileProblems);
            LiveConnect.IsEnabled = profileProblems.Length == 0 && !state.IsActive && !RecoveryBusy && !liveWorkflowBusy && Host.Read(s => s.Paused) && RuntimeConsent.IsChecked == true && CpuConsent.IsChecked == true && TiaConsent.IsChecked == true;
            LiveDisconnect.IsEnabled = state.IsActive && !state.StopRequested && state.State != LiveProcessState.Stopping;
            liveStopButton.IsEnabled = LiveDisconnect.IsEnabled;
            RefreshDefaultRecoveryControls();
            RefreshWorkpieceControls(state);
            LiveZeroBandMarking.IsEnabled = !RecoveryBusy && !workpieceEditing && liveHost.TryGetStoppedBeltDistance(out _);
            adapterStatus.Text = $"Status: {StateLabel(state.State)}\n{state.Detail}" +
                (state.ReportPath.Length == 0 ? "" : $"\nBericht: {state.ReportPath}\nCleanup: {(state.CleanupConfirmed ? "bestätigt" : "noch nicht bestätigt")} · CPU vor Trennen: {state.CpuBeforeDisconnect}") +
                (!state.IsActive && state.ReportPath.Length > 0 ? state.InteractiveSession ? "\n" + LiveStatusText.Outcome(state)
                    : $"\nLaufprüfung: {(state.Passed ? "BESTANDEN" : "nicht bestanden / abgebrochen")} · vollständiger Transport: {(state.TransportCompleted ? "im Bericht bestätigt" : "nicht bestätigt")}" : "");
        }
        private void ZeroBandMarking() => Safe(() =>
        {
            Numbers.Require(!RecoveryBusy && !workpieceEditing && liveHost.TryGetStoppedBeltDistance(out _),
                "Bandmarkierung nur bei frisch bestätigtem Stillstand und ausgeschalteten Motorbefehlen auf 0 setzen.");
            Numbers.Require(liveHost.TryGetStoppedBeltDistance(out double distance), "Stillstand nicht mehr bestätigt.");
            plantView.Marking.SetZero(distance); plantView.InvalidateVisual();
            SetStatus("Bandmarkierung auf 0 mm gesetzt. SPS-Impulszähler für die nächste Kalibrierfahrt in TIA zurücksetzen.");
        });
        private static string StateLabel(LiveProcessState state) => state switch
        {
            LiveProcessState.Connecting => "Verbindungsaufbau", LiveProcessState.Streaming => "Verbunden · SPS steuert die Anlage",
            LiveProcessState.Finalizing => "Austausch beendet · Abschlussbericht wird geschrieben",
            LiveProcessState.Stopping => "Trennen läuft", LiveProcessState.Completed => "Getrennt · Sitzung beendet",
            LiveProcessState.Faulted => "Fehler · Daten ungültig", _ => "Nicht verbunden"
        };

        private bool CanPrepareWorkpiece(LiveProcessSnapshot state) =>
            !InteractiveConnection && !state.IsActive && !RecoveryBusy && !liveWorkflowBusy && !workpieceEditing &&
            (state.State == LiveProcessState.Disconnected ||
             (state.State == LiveProcessState.Completed && state.CleanupConfirmed && state.Latest != null &&
              WorkpieceHandling.IsStopped(state.Latest.MotorRight, state.Latest.MotorLeft, state.Latest.MotorVelocity, state.Latest.ObjectVelocity)));
        private bool DisplayedWorkpiecePresent(LiveProcessSnapshot state) =>
            !state.IsActive && liveDraftPresent.HasValue ? liveDraftPresent.Value :
            state.Latest?.Present ?? (liveConfiguration ?? activeConfig).InitialPresent;
        private void RefreshWorkpieceControls(LiveProcessSnapshot state)
        {
            if (LiveInsertWorkpiece == null) return;
            bool allowed = !workpieceEditing && (state.IsActive ? liveHost.CanChangeWorkpiece : CanPrepareWorkpiece(state));
            bool present = DisplayedWorkpiecePresent(state);
            LiveInsertWorkpiece.IsEnabled = allowed && !present;
            LiveRemoveWorkpiece.IsEnabled = allowed && present;
            liveWorkpieceStatus.Text = workpieceEditing ? "Warte auf Übernahme am SPS-Synchronisationspunkt…" :
                InteractiveConnection && !state.IsActive ? "Zuerst mit der SPS verbinden. Die neue Verbindung beginnt ohne Prüfling." :
                allowed ? (present ? "Prüfling auf dem Band. Entnehmen ist an jeder Position möglich." : "Band leer. Einsetzen erfolgt links am Sensor 2.") +
                    (state.IsActive ? "" : "\nVorbereitung für den nächsten Lauf; keine SPS-Werte werden verändert.") :
                state.IsActive && state.Latest?.WorkpieceCommandsSupported == false ? "LiveProbe unterstützt die Bedienung noch nicht. V5-Compilerbuild aktualisieren." :
                "Gesperrt: Stillstand und ausgeschaltete Motorbefehle müssen bestätigt sein.";
        }
        internal async Task ChangeLiveWorkpieceAsync(bool insert)
        {
            if (workpieceEditing) return;
            try
            {
                var state = liveHost.Snapshot;
                Numbers.Require(IsExternal, "Prüflingsbedienung gehört zum ExternalPlc-Modus.");
                Numbers.Require(insert != DisplayedWorkpiecePresent(state), insert ? "Zuerst vorhandenen Prüfling entfernen." : "Kein Prüfling auf dem Förderband.");
                double length = insert ? Ui.Value(liveLength) : 0;
                if (state.IsActive)
                {
                    Numbers.Require(liveHost.CanChangeWorkpiece, "Prüfling nur bei frisch bestätigtem Stillstand ändern.");
                    workpieceEditing = true; RefreshLiveControls();
                    var answer = await liveHost.ChangeWorkpieceAsync(insert ? "InsertLeft" : "Remove", length);
                    SetStatus(answer.Detail, !answer.Applied);
                }
                else
                {
                    Numbers.Require(CanPrepareWorkpiece(state), "Stillstand nicht bestätigt oder Laufabschluss noch aktiv.");
                    if (insert)
                    {
                        double position = WorkpieceHandling.LeftPosition(liveConfiguration ?? activeConfig, length);
                        liveInitial.Text = position.ToString(CultureInfo.CurrentCulture);
                    }
                    liveDraftPresent = insert; livePreviewEdited = true; previewConfiguration = null;
                    SetStatus(insert ? "Prüfling für den nächsten SPS-Lauf links eingesetzt. Keine API gestartet." :
                        "Prüfling für den nächsten SPS-Lauf entfernt. Keine API gestartet.");
                }
            }
            catch (Exception e) { SetStatus("Prüfling nicht geändert bzw. Übernahme unbestätigt: " + e.Message, true); }
            finally { workpieceEditing = false; Refresh(); }
        }

        private void RefreshLive(bool details)
        {
            var connection = liveHost.Snapshot;
            foreach (var sample in liveHost.DrainSamples())
            {
                long us = sample.TimeNs / 1000;
                liveSamples.Add(new InputSample(us, sample.Sequence, OperationMode.ExternalPlc,
                    new SensorFrame(sample.Left, sample.Optical, sample.Right, sample.Hall, sample.AnalogMode ? sample.PotiRaw : (short)0, true, sample.Sequence, us),
                    sample.ToActuatorFrame(true), UnavailablePlcTelemetry));
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
            if (connection.IsActive) { livePreviewEdited = false; liveDraftPresent = null; }
            var latest = !connection.IsActive && livePreviewEdited ? null : connection.Latest;
            lastLiveView = latest == null ? null : CreateLiveView(latest, valid);
            var config = liveConfiguration ?? activeConfig;
            if (latest == null)
            {
                // A static preview is separate from live data; it never supplies sensor values.
                string key = config.GetHashCode() + "|" + liveInitial.Text + "|" + liveLength.Text + "|" + liveHallPitch.Text + "|" + liveDraftPresent;
                if (previewConfiguration == null || (details && key != previewKey))
                {
                    var preview = config.Copy();
                    if (liveDraftPresent.HasValue) preview.InitialPresent = liveDraftPresent.Value;
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
            bool analog = (liveProfile?.Analog ?? selectedProfile.Analog) != null;
            LivePotentiometer.IsEnabled = analog && (!connection.IsActive || valid);
            if (analog)
            {
                var settings = liveProfile?.Analog ?? selectedProfile.Analog!;
                potentiometerReadback.Text = $"Eingestellt: {LivePotentiometer.Value:F2} V · AI-Sollrohwert {settings.InputRaw(LivePotentiometer.Value)}\n" +
                    (valid && latest?.AnalogMode == true ? $"AI zuletzt übertragen: {latest.PotiRaw}\nAQ aus SPS: {latest.SpeedCommandRaw} · {Analog.OutputVoltage(latest.SpeedCommandRaw):F2} V\nBand: {latest.ObjectVelocity:F2} mm/s · {(latest.MotorRight ? "rechts" : latest.MotorLeft ? "links" : "aus")}" : "Analoge SPS-Werte: nicht verfügbar / nicht verbunden");
            }
            if (Tabs.SelectedIndex == 0) { trace.Samples = liveSamples.ToArray(); trace.InvalidateVisual(); }
            Title = "AT-2 / AT-3 · SPS " + (liveMap?.InstanceName ?? selectedProfile.Map.InstanceName) + " · " + StateLabel(connection.State);
            profileSubtitle.Text = "Lokale virtuelle Siemens-SPS · PLCSIM Advanced · " + (valid ? "verbunden" : "getrennt");
            time.Text = latest == null ? "Nicht verbunden\nVirtuelle SPS" : $"{latest.TimeNs / 1e9:F6} s · #{latest.Sequence}\n{(valid ? "LIVE · SingleStep_C" : "LETZTER WERT · ungültig")}";
            motion.Text = latest == null ? "Profil einrichten, Freigaben setzen und „Mit SPS verbinden“ wählen." :
                $"SPS-Befehle: Motor rechts {(latest.MotorRight ? "EIN" : "AUS")} · Motor links {(latest.MotorLeft ? "EIN" : "AUS")}\n" +
                $"Lampen: Rot {Bit(latest.LampRed)} · Gelb {Bit(latest.LampYellow)} · Grün {Bit(latest.LampGreen)}\n" +
                $"Modell: Motor {latest.MotorVelocity:F2} mm/s · Prüfling {latest.ObjectVelocity:F2} mm/s · linke Kante {latest.Position:F2} mm" +
                (valid ? "" : connection.State == LiveProcessState.Finalizing ? "\nAustausch beendet: Abschlussbericht wird geschrieben. Angezeigte Werte sind historisch und kein Erfolgsnachweis." : "\nVerbindung nicht aktiv: angezeigte Werte sind historisch, das Modell schreitet nicht fort.");
            diagnostic.Text = (analog ? "Drehzahl aus analogem SPS-Ausgang · keine feste Testgeschwindigkeit\n" : $"Temporäre C#-Testgeschwindigkeit: {(liveProfile?.TestSpeedMmPerSecond ?? 100):F2} mm/s · keine analoge SPS-Vorgabe\n") +
                $"Hallteilung im Modell: {config.HallPitchMm:F3} mm/Impuls · Testannahme\n" +
                (latest == null ? "Keine Liveimpulse empfangen." : $"Erzeugte steigende Modellflanken: {latest.GeneratedRisingEdges}\n" +
                    $"Erkannt / elektrischer Pegel: links {Bit(latest.LeftDetected)}/{Bit(latest.Left)} · Optik {Bit(latest.OpticalDetected)}/{Bit(latest.Optical)} · rechts {Bit(latest.RightDetected)}/{Bit(latest.Right)}\n") +
                $"CPU vor Trennen: {connection.CpuBeforeDisconnect} · Cleanup {(connection.CleanupConfirmed ? "bestätigt" : "unbestätigt")}";
            if (connection.Detail != lastLiveDetail)
            {
                lastLiveDetail = connection.Detail;
                liveEvents.Add(new PlantEvent(liveEvents.Count + 1, latest?.TimeNs / 1000 ?? 0, connection.State.ToString(), connection.Detail));
                if (liveEvents.Count > 1000) liveEvents.RemoveAt(0);
                SetStatus(connection.Detail + (connection.Error == null ? "" : "\n" + connection.Error), connection.State == LiveProcessState.Faulted);
            }
            if (Tabs.SelectedIndex == 1) UpdateSignalRows(MakeLiveSignals(latest, valid));
            if (Tabs.SelectedIndex == 3) events.ItemsSource = liveEvents.AsEnumerable().Reverse().ToArray();
        }
        private static string Bit(bool value) => value ? "1" : "0";
        private SessionSnapshot CreateLiveView(DigitalProgress sample, bool valid)
        {
            var config = liveConfiguration ?? activeConfig;
            long us = sample.TimeNs / 1000;
            var sensors = new SensorFrame(sample.Left, sample.Optical, sample.Right, sample.Hall, sample.AnalogMode ? sample.PotiRaw : (short)0, valid, sample.Sequence, us);
            // Analogue words are available only in an explicitly configured analogue profile.
            var actuators = sample.ToActuatorFrame(valid);
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
                ["MotorRight"] = sample.MotorRight, ["MotorLeft"] = sample.MotorLeft, ["LampGreen"] = sample.LampGreen,
                ["LampYellow"] = sample.LampYellow, ["LampRed"] = sample.LampRed
            };
            return SignalContract.All.Where(d => d.Direction != "plc_to_diagnostics").Select(d =>
            {
                bool bound = (liveProfile ?? selectedProfile.DigitalSettings()).RequiredSignalIds.Contains(d.Id);
                string value = values.TryGetValue(d.Id, out bool bit) ? (bit ? "HIGH / 1" : "LOW / 0") : "—";
                if (sample?.AnalogMode == true && d.Id == "PotiRaw") value = sample.PotiRaw.ToString(CultureInfo.CurrentCulture);
                if (sample?.AnalogMode == true && d.Id == "SpeedCommandRaw") value = sample.SpeedCommandRaw.ToString(CultureInfo.CurrentCulture);
                var binding = liveMap?.Signals.SingleOrDefault(s => s.Id == d.Id && s.Confirmed && s.ByteOffset.HasValue);
                string address = binding == null ? "keine bestätigte Bindung geladen" : SignalAddress.Format(binding);
                return new SignalRow(d.Id, d.Direction == "plant_to_plc" ? "C#-Anlage → SPS" : d.Direction == "plc_to_plant" ? "SPS → C#-Anlage" : "SPS → Diagnose",
                    bound ? d.Direction == "plant_to_plc" ? "Anlagenmodell im LiveProbe" : "SPS " + (liveMap?.InstanceName ?? selectedProfile.Map.InstanceName) : "nicht angebunden", value,
                    !bound ? "Unavailable" : sample == null ? "nicht verbunden" : valid ? "gültig / " + (sample.AnalogMode && (d.Id == "PotiRaw" || d.Id == "SpeedCommandRaw") ? "analog live" : "digital live") : "veraltet / ungültig",
                    !bound ? "ungebunden" : $"{sample?.TimeNs ?? 0} ns · {address}");
            }).ToArray();
        }
        private void ExportLive() => Safe(() =>
        {
            var state = liveHost.Snapshot;
            Numbers.Require(!state.IsActive, "Den begrenzten Test zuerst beenden; Abschlussbericht wird danach exportiert.");
            Numbers.Require(File.Exists(state.ReportPath), "Noch kein fachlicher Livebericht vorhanden. Status und Prozesslogs im Bereich Siemens prüfen.");
            var dialog = new SaveFileDialog { Filter = "JSON-Bericht|*.json", FileName = "SPS-Sitzung.json" };
            if (dialog.ShowDialog(this) == true) { Json.SaveAtomic(dialog.FileName, File.ReadAllText(state.ReportPath)); SetStatus("Tatsächlicher LiveProbe-Bericht exportiert: " + dialog.FileName); }
        });
    }
}
