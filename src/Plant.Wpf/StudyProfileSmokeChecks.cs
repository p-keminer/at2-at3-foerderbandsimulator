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
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    // In-process WPF events only. No UI Automation client, Computer Use or Siemens API.
    internal static class StudyProfileSmokeChecks
    {
        internal static IEnumerable<DependencyObject> All(DependencyObject node)
        { yield return node; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All(VisualTreeHelper.GetChild(node, i))) yield return child; }
        internal static Task Run(MainWindow window, string directory, Action<bool, string> check)
        {
            var initial = (StudyProfile)window.ProfileSelect.SelectedItem;
            check(window.ProfileSelect.Items.Count == 3, "Drei mitgelieferte Profile in der Auswahl");
            check(window.Tabs.Items.OfType<TabItem>().Count(t => t.Visibility == Visibility.Visible) == 2, "Startansicht zeigt nur Bewegung und Signale");
            window.UpdateLayout();
            Save(window, Path.Combine(directory, "profiles-01-start.png"));
            window.ProfileSelect.SelectedIndex = 1;
            check(((StudyProfile)window.ProfileSelect.SelectedItem).Id == "AT2_Uebung" && !window.LiveConnect.IsEnabled, "AT-2-Auswahl setzt Modell zurück und bleibt ohne Adressen getrennt");
            check(window.Host.Read(s => s.Paused && s.Mode == OperationMode.ExternalPlc), "Profilwechsel bleibt getrennt und im Modus virtuelle SPS");
            window.ApplyStudyProfile(initial);
            int writes = 0;
            var editor = new ProfileEditorWindow(initial, true, p => {
                if (++writes == 1) throw new IOException("Test: Datenträger vorübergehend nicht beschreibbar.");
                new StudyProfileStore(Path.Combine(directory, "dialog-profiles")).Save(p);
            }) { Owner = window };
            Exception? failure = null;
            editor.ContentRendered += (_, _) =>
            {
                try
                {
                    T Find<T>(string name) where T : DependencyObject => All(editor).OfType<T>().First(x => AutomationProperties.GetName(x) == name);
                    Find<TextBox>("Profilname").Text = "Meine mobile Übung";
                    Find<TextBox>("CPU-Instanzname aus PLCSIM Advanced").Text = "Eigene_CPU";
                    check(!All(editor).OfType<CheckBox>().Any(x => AutomationProperties.GetName(x).Contains("lokale virtuelle CPU")), "Lokale virtuelle CPU ist fest vorgegeben; keine missverständliche Checkbox");
                    Find<TextBox>("SensorLeftHigh Adresse").Text = "Q0.0";
                    Find<Button>("Profil speichern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(editor.IsVisible && editor.Result == null && Find<TextBox>("Profilname").Text == "Meine mobile Übung", "Ungültige Richtung hält Dialog und gültige Eingaben offen");
                    Find<TextBox>("SensorLeftHigh Adresse").Text = "I0.7";
                    Find<Button>("Profil speichern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(editor.IsVisible && editor.Result == null && Find<TextBox>("Profilname").Text == "Meine mobile Übung", "Schreibfehler erhält Eingaben; erneutes Speichern ist möglich");
                    Save(editor, Path.Combine(directory, "profiles-02-editor.png"));
                    Find<Button>("Profil speichern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception e) { failure = e; editor.Close(); }
            };
            editor.ShowDialog(); if (failure != null) throw failure;
            var errors = new List<string>(); var saved = new StudyProfileStore(Path.Combine(directory, "dialog-profiles")).Load(errors).Single();
            check(saved.Name == "Meine mobile Übung" && saved.Map.InstanceName == "Eigene_CPU" && saved.Map.Signals[0].BitOffset == 7 && saved.Map.LocalVirtualInstanceConfirmed, "Formularwerte dauerhaft gespeichert und erneut geladen");
            check(initial.Map.Signals[0].BitOffset == 0 && initial.Name == "Referenzprojekt", "Mitgelieferte Vorlage bleibt unverändert");
            window.ApplyStudyProfile(saved); check(window.LiveProcess.Snapshot.State == LiveProcessState.Disconnected, "Gespeichertes Profil löst keine Siemens-Verbindung aus");
            window.ApplyStudyProfile(initial);
            return Task.CompletedTask;
        }
        internal static void Save(Window window, string path)
        {
            window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); png.Save(file);
        }
    }
}
