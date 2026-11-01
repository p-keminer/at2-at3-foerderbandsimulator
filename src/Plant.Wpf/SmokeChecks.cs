using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Plant.Core;

namespace Plant.Wpf
{
    internal static class SmokeChecks
    {
        public static bool Failed { get; private set; }
        public static async void Start(MainWindow window, string directory)
        {
            var checks = new List<string>(); Directory.CreateDirectory(directory);
            void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
            void Save(string name)
            {
                window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(directory, name + ".png")); png.Save(file);
            }
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            IEnumerable<DependencyObject> Descendants(DependencyObject root)
            {
                yield return root;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                    foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
            }
            T Find<T>(string name) where T : DependencyObject => Descendants(window).OfType<T>().First(x => AutomationProperties.GetName(x) == name);
            try
            {
                Check(window.IsVisible && window.IsLoaded, "Echtes WPF-Fenster geladen und sichtbar");
                Check(window.Tabs.Items.Count == 7, "Sieben Diagnose- und Einstellbereiche vorhanden");
                var permanentPlant = Descendants(window).OfType<PlantView>().Single();
                for (int i = 0; i < window.Tabs.Items.Count; i++)
                {
                    window.Tabs.SelectedIndex = i; window.UpdateLayout();
                    Check(permanentPlant.IsVisible && permanentPlant.ActualHeight >= 210, "Anlage bleibt im Detailbereich " + i + " sichtbar");
                }
                window.Tabs.SelectedIndex = 0;
                Check(window.Host.Read(s => s.Paused), "Start ist offline und pausiert");
                Check(window.RunButton.Focus(), "Startbutton kann Tastaturfokus erhalten");
                Check(AutomationProperties.GetName(window.RunButton).Length > 0, "Startbutton besitzt Automation-Namen");
                Save("01-offline-start");
                Click(window.StepButton); Check(window.Host.Read(s => s.Snapshot().Plant.TimeUs) == 1000, "Einzelschritt über echten Button");
                Click(window.RunButton); await Task.Delay(140); Click(window.PauseButton);
                long paused = window.Host.Read(s => s.Snapshot().Plant.TimeUs);
                Check(paused > 1000, "Hintergrundscheduler schreitet ohne Modellaufruf aus der UI fort");
                await Task.Delay(80); Check(window.Host.Read(s => s.Snapshot().Plant.TimeUs) == paused, "Pause friert Scheduler ein");
                Find<CheckBox>("Motor rechts").IsChecked = true;
                window.Host.Change(s => { s.Resume(); s.Advance(1000000); s.Pause(); });
                window.Refresh(); Check(window.Host.Read(s => s.Snapshot().Plant.LeftEdgeMm) > 90, "Manuelle Bewegung mit Sensor-/Hallreaktion");
                Save("02-manual-motion");
                window.Tabs.SelectedIndex = 1; window.Refresh(); window.UpdateLayout(); Save("03-signals");
                // Audit the earlier screenshot discrepancy through REAL checkbox events.
                // Never force UI state to match a host command injected directly by the smoke test.
                window.Reset(new PlantConfiguration()); window.Tabs.SelectedIndex = 0; window.UpdateLayout();
                var right = Find<CheckBox>("Motor rechts");
                right.IsChecked = true; Click(window.StepButton); window.Refresh();
                var displayedBefore = Descendants(window).OfType<PlantView>().Single().Snapshot;
                Check(window.Host.Read(s => s.Manual.MotorRight && s.Output.MotorRight), "Checkbox EIN wird als Bedienwunsch und nach Schritt als Befehl übernommen");
                right.IsChecked = false; window.Refresh();
                var pendingOff = window.Host.Read(s => new { requested = s.Manual, adopted = s.Output, snapshot = s.Snapshot() });
                Check(!pendingOff.requested.MotorRight && pendingOff.adopted.MotorRight && pendingOff.snapshot.Plant.Actuators.MotorRight,
                    "Bei Simulatorpause bleibt alter übernommener Befehl sichtbar, während Bedienwunsch bereits AUS ist");
                Click(window.StepButton); window.Refresh();
                Check(!window.Host.Read(s => s.Output.MotorRight), "Nächster Modellschritt übernimmt AUS ohne hängendes HIGH");
                Check(displayedBefore!.Plant.Actuators.MotorRight, "Zuvor angezeigter unveränderlicher Snapshot behält seinen historischen Befehl");
                window.Tabs.SelectedIndex = 1; window.Refresh(); window.UpdateLayout();
                var rows = Descendants(window).OfType<DataGrid>().SelectMany(g => g.Items.OfType<SignalRow>()).ToArray();
                Check(rows.Single(r => r.Signal == "MotorRight").Value == "LOW / 0", "Signalansicht zeigt nach Übernahme und Refresh MotorRight LOW");
                Json.SaveAtomic(Path.Combine(directory, "motor-command-stages.json"), Json.Write(new
                {
                    before = displayedBefore, pendingOff, after = window.Host.Read(s => new { requested = s.Manual, adopted = s.Output, snapshot = s.Snapshot() }),
                    conclusion = "Kein nachgewiesener Anzeigefehler. Bedienwunsch, letzter übernommener Befehl und historischer Snapshot sind verschiedene Zustände. Kein UI-Produktcode geändert."
                }));
                window.SourceSelect.SelectedIndex = 1; Click(window.StepButton); window.Refresh();
                Check(right.IsChecked == false && !right.IsEnabled && window.Host.Read(s => s.Output.MotorRight),
                    "Im Referenzmodus ist die inaktive manuelle Checkbox unabhängig vom Controllerbefehl");
                var config = new PlantConfiguration { CalibrationMmPerPulse = 1 };
                window.Reset(config); window.SourceSelect.SelectedIndex = 1;
                window.Host.Change(s => { s.Controller.Exercise = Exercise.AT3; s.Resume(); s.Advance(8500000); s.Pause(); });
                window.Tabs.SelectedIndex = 0; window.Refresh();
                Check(window.Host.Read(s => s.Controller.Telemetry.MeasurementActive), "AT-3-Messfenster aktiv");
                Click(window.StopButton); window.Host.Change(s => { s.Resume(); s.Advance(200000); s.Pause(); });
                Check(window.Host.Read(s => s.Controller.Stopped && !s.Output.MotorRight), "AT-3-Stoppbutton wirkt auf Controller");
                window.Refresh();
                Save("04-at3-stopped");
                Click(window.StopButton); window.Host.Change(s => { s.Resume(); s.Advance(10000000); s.Pause(); });
                Check(window.Host.Read(s => s.Controller.Telemetry.MeasuredLengthMm) == 120, "Fortsetzen liefert 120-mm-Messung");
                window.Refresh(); Save("05-at3-complete");
                for (int i = 2; i < 7; i++)
                { window.Tabs.SelectedIndex = i; window.Refresh(); window.UpdateLayout(); Save("tab-" + i); }
                window.SourceSelect.SelectedIndex = 0;
                window.Host.Change(s => s.Remove()); window.Tabs.SelectedIndex = 2; window.UpdateLayout();
                Click(Find<Button>("Markierung / Zählung beginnen"));
                Click(Find<Button>("Fahrt in Kalibrierliste übernehmen"));
                Check(window.Host.Read(s => s.Calibration.Runs.Count) == 0, "Kalibrierfahrt mit null Impulsen wird in der UI abgelehnt");
                foreach (int distance in new[] { 100, 120, 80 })
                {
                    Click(Find<Button>("Markierung / Zählung beginnen"));
                    window.Host.Change(s => { s.SetManual(new(true, SpeedCommandRaw: Analog.OutputRaw(5))); s.Resume(); s.Advance(distance * 20000L); s.Pause(); });
                    Find<TextBox>("Abgelesene Strecke [mm] (Betrag)").Text = distance.ToString();
                    Click(Find<Button>("Fahrt in Kalibrierliste übernehmen"));
                }
                Check(window.Host.Read(s => s.Calibration.Runs.Count) == 3, "Drei Fahrten über Kalibrierbedienelemente erfasst");
                Click(Find<Button>("Mittelwert übernehmen"));
                Check(Math.Abs(window.Host.Read(s => s.Controller.Calibration)!.Value - 1) < .01, "Kalibriermittelwert über echten Button übernommen");
                window.Refresh(); Save("08-calibration");
                window.Tabs.SelectedIndex = 3; window.UpdateLayout();
                var fault = Find<ComboBox>("Optischer Sensor"); fault.SelectedIndex = 2;
                Check(window.Host.Read(s => s.Plant.Sensors.OpticalHigh), "StuckHigh über Fehlerbedienelement wirksam");
                window.Reset(config); window.Refresh();
                Check(fault.SelectedIndex == 0 && !window.Host.Read(s => s.Plant.Sensors.OpticalHigh), "Gesamtreset synchronisiert sichtbare Fehlerauswahl und Anlagenzustand");
                window.SourceSelect.SelectedIndex = 2; window.Refresh();
                Check(!window.RunButton.IsEnabled && window.Host.Read(s => s.Quality) == LinkQuality.Unavailable, "ExternalPlc bleibt unverbunden und gesperrt");
                Save("06-external-unavailable");
                window.SourceSelect.IsDropDownOpen = true; await Task.Delay(30);
                Check(window.SourceSelect.IsDropDownOpen, "Native Quellen-Auswahlliste öffnet"); window.SourceSelect.IsDropDownOpen = false;
                window.Width = 1000; window.Height = 720; window.Tabs.SelectedIndex = 0; window.Refresh(); Save("07-narrow-window");
                await RenderingChecks.Run(window, directory, Check);
                await DefaultRecoverySmokeChecks.Run(directory, Check);
                await LiveSmokeChecks.Run(window, directory, Check);
                window.Tabs.SelectedIndex = 5; window.Refresh(); Save("09-live-fixture-fault");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Siemens", StringComparison.OrdinalIgnoreCase)), "Kein Siemens-Assembly beim Start oder Üben geladen");
                window.Host.Read(s => s.CaptureExport()).Save(Path.Combine(directory, "export"));
                Check(File.Exists(Path.Combine(directory, "export", "hall-edges.csv")), "Export aus laufender WPF-Sitzung erfolgreich");
                string oldFailure = Path.Combine(directory, "failure.txt");
                if (File.Exists(oldFailure)) File.Delete(oldFailure);
                Json.SaveAtomic(Path.Combine(directory, "result.json"), Json.Write(new { passed = true, checks, note = "Automatisierter WPF-Dispatcher-/Render-Smokecheck. Keine manuelle Maus-/Tastaturbedienung und kein Siemens-Livetest." }));
            }
            catch (Exception e)
            {
                Failed = true; File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString());
                Json.SaveAtomic(Path.Combine(directory, "result.json"), Json.Write(new { passed = false, checks, error = e.ToString() }));
            }
            finally { window.Close(); }
        }
    }
}
