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
        public static async void StartProfiles(MainWindow window, string directory)
        {
            var checks = new List<string>(); Directory.CreateDirectory(directory);
            try
            {
                LayoutStabilityChecks.Run(window, directory, (ok, name) => { if (!ok) throw new Exception(name); checks.Add(name); });
                await StudyProfileSmokeChecks.Run(window, directory, (ok, name) => { if (!ok) throw new Exception(name); checks.Add(name); });
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Siemens"))) throw new Exception("Siemens-Assembly unerwartet geladen.");
                Json.SaveAtomic(Path.Combine(directory, "result.json"), Json.Write(new { passed = true, checks, siemensLoaded = false }));
            }
            catch (Exception e) { Failed = true; Json.SaveAtomic(Path.Combine(directory, "result.json"), Json.Write(new { passed = false, checks, error = e.ToString() })); }
            finally { window.Close(); }
        }
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
                await StudyProfileSmokeChecks.Run(window, directory, Check);
                AnalogSmokeChecks.Run(window, directory, Check);
                await BeltMarkingSmokeChecks.Run(window, directory, Check);
                Find<CheckBox>("Verbindungsdetails und Ereignisse").IsChecked = true;
                Check(window.Tabs.Items.Count == 4, "Nur vier relevante Diagnosebereiche; keine manuellen oder Demo-Werkzeuge");
                var plant = Descendants(window).OfType<PlantView>().Single();
                for (int i = 0; i < window.Tabs.Items.Count; i++)
                {
                    window.Tabs.SelectedIndex = i; window.UpdateLayout();
                    Check(plant.IsVisible && plant.ActualHeight >= 210, "Anlage bleibt im Detailbereich " + i + " sichtbar");
                }
                window.Tabs.SelectedIndex = 0; window.Refresh();
                Check(window.Host.Read(s => s.Paused && s.Mode == OperationMode.ExternalPlc && s.Snapshot().Plant.TimeUs == 0), "Start ist getrennt und ohne Modellfortschritt im SPS-Modus");
                Check(!Descendants(window).OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Steuerung"), "Kein Umschalter für Manuell oder Demo vorhanden");
                Check(!window.LiveConnect.IsEnabled, "Start benötigt eine bewusste Freigabe");
                Save("01-virtual-disconnected");
                window.Tabs.SelectedIndex = 1; window.Refresh(); Save("02-signals");
                LayoutStabilityChecks.Run(window, directory, Check);
                window.Width = 1000; window.Height = 720; window.Tabs.SelectedIndex = 0; window.Refresh(); Save("03-narrow-window");
                await RenderingChecks.Run(window, directory, Check);
                await DefaultRecoverySmokeChecks.Run(directory, Check);
                await LiveSmokeChecks.Run(window, directory, Check);
                window.Tabs.SelectedIndex = 2; window.Refresh(); Save("09-live-fixture-fault");
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
