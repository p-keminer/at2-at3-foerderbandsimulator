using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    // Own dispatcher and visual tree only; no external UI Automation or Siemens connection.
    internal static class AnalogSmokeChecks
    {
        internal static void Run(MainWindow window, string directory, Action<bool, string> check)
        {
            var previous = (StudyProfile)window.ProfileSelect.SelectedItem;
            var profile = previous.Copy(); profile.Id = "analog-smoke"; profile.Name = "AT-2 Analogprüfung";
            profile.Analog = new AnalogIoSettings();
            window.ApplyStudyProfile(profile);
            check(window.LivePotentiometer.IsVisible && window.LivePotentiometer.Minimum == 0 && window.LivePotentiometer.Maximum == 10,
                "Channel-5-Regler ist im Analogprofil sichtbar und auf 0…10 V begrenzt");
            window.LivePotentiometer.Value = 5;
            check(!window.LiveProcess.Snapshot.IsActive && window.Host.Read(s => s.Paused && s.Snapshot().Plant.MotorVelocity == 0),
                "Potentiometerbedienung im getrennten Zustand startet keine SPS und keinen Motor");
            var sample = new DigitalProgress { AnalogMode = true, PotiRaw = 13824, SpeedCommandRaw = 9677,
                TimeNs = 1000000, Sequence = 2, Position = 50, Length = 120, Present = true,
                MotorRight = true, MotorVelocity = 35.00072, ObjectVelocity = 35.00072 };
            window.DisplayLive(new LiveProcessSnapshot(LiveProcessState.Streaming, "Analogdarstellung · Siemensfreie Fixture", sample, IsActive: true));
            var signals = window.MakeLiveSignals(sample, true);
            check(signals.Single(s => s.Signal == "PotiRaw").Value == "13824" &&
                signals.Single(s => s.Signal == "SpeedCommandRaw").Value == "9677" &&
                signals.Single(s => s.Signal == "SpeedCommandRaw").Quality.Contains("analog live") &&
                sample.ToActuatorFrame(true).SpeedCommandRaw == 9677,
                "Analoger SPS-Rohwert bleibt im Darstellungsframe erhalten");
            window.UpdateLayout(); StudyProfileSmokeChecks.Save(window, Path.Combine(directory, "analog-01-potentiometer.png"));
            window.DisplayLive(new LiveProcessSnapshot(LiveProcessState.Faulted, "Fixture getrennt", sample));
            window.ApplyStudyProfile(previous);
            check(!window.LivePotentiometer.IsVisible, "Digitales Profil bietet keine wirksame analoge Bedienung");
            foreach (var b in profile.Map.Signals.Where(b => b.Id == "PotiRaw" || b.Id == "SpeedCommandRaw"))
            { b.ByteOffset = b.Id == "PotiRaw" ? 14 : 4; b.BitOffset = null; b.Confirmed = true; b.AreaSizeBytes = b.Id == "PotiRaw" ? 20 : 12; }
            profile.Map.InstanceName = "Analog_Fixture"; profile.Map.LocalVirtualInstanceConfirmed = true;
            var editor = new ProfileEditorWindow(profile, true, p => new StudyProfileStore(Path.Combine(directory, "analog-profiles")).Save(p)) { Owner = window };
            Exception? failure = null;
            editor.ContentRendered += (_, _) => {
                try {
                    foreach (var fold in StudyProfileSmokeChecks.All(editor).OfType<Expander>().ToArray()) fold.IsExpanded = true;
                    editor.UpdateLayout();
                    T Find<T>(string name) where T : DependencyObject => StudyProfileSmokeChecks.All(editor).OfType<T>().First(x => AutomationProperties.GetName(x) == name);
                    Find<TextBox>("Eingangsbereich [Bytes, aus TIA]").Text = "20";
                    Find<TextBox>("Ausgangsbereich [Bytes, aus TIA]").Text = "12";
                    Find<TextBox>("PotiRaw Adresse").Text = "IW0";
                    Find<Button>("Profil speichern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(editor.IsVisible && editor.Result == null, "Überlappende AI-/Sensoradresse hält den Konfigurationsdialog offen");
                    Find<TextBox>("PotiRaw Adresse").Text = "IW14";
                    Find<TextBox>("Potentiometer-Anfangsstellung [V]").Text = "5";
                    Find<Button>("Profil speichern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                } catch (Exception e) { failure = e; editor.Close(); }
            };
            editor.ShowDialog(); if (failure != null) throw failure;
            var errors = new System.Collections.Generic.List<string>();
            var saved = new StudyProfileStore(Path.Combine(directory, "analog-profiles")).Load(errors).Single();
            check(errors.Count == 0 && saved.Analog?.InitialPotentiometerVolts == 5 && saved.Map.Signals.Single(s => s.Id == "PotiRaw").ByteOffset == 14 && saved.Map.Signals.Single(s => s.Id == "SpeedCommandRaw").ByteOffset == 4,
                "Analogadressen und Anfangsstellung lassen sich vollständig über das Formular speichern und laden");
            window.ApplyStudyProfile(saved);
            check(window.LivePotentiometer.Value == 5 && !window.LiveProcess.Snapshot.IsActive, "Gespeicherte Analogkonfiguration wird ohne Siemens-Zugriff übernommen");
            window.ApplyStudyProfile(previous);
        }
    }
}
