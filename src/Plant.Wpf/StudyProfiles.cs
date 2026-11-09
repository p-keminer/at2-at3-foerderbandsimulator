using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed partial class MainWindow
    {
        private readonly List<StudyProfile> studyProfiles = new();
        private readonly HashSet<string> builtInProfiles = new(StringComparer.Ordinal);
        private StudyProfileStore profileStore = null!;
        internal ComboBox ProfileSelect = null!;
        private StudyProfile selectedProfile = null!;
        private DockPanel profileControls = null!;
        private readonly TextBlock profileSummary = Ui.Text("", 12);
        private bool selectingProfile;
        private readonly string? userProfileDirectory;
        private bool CanEditProfiles => !liveHost.Snapshot.IsActive && !liveWorkflowBusy && !RecoveryBusy && Host.Read(s => s.Paused);

        private void BuildStudyProfilePanel(Panel parent)
        {
            profileControls = new DockPanel { Height = 44, Margin = new Thickness(0, 0, 0, 8) };
            parent.Children.Add(profileControls);
            var label = Ui.Text("Projektprofil", 14, true); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 10, 0); profileControls.Children.Add(label);
            ProfileSelect = new ComboBox { MinHeight = 32, Width = 210, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            System.Windows.Automation.AutomationProperties.SetName(ProfileSelect, "Projektprofil");
            profileControls.Children.Add(ProfileSelect);
            var row = Ui.Row(profileControls);
            row.Children.Add(Ui.Button("Profil einrichten…", () => EditStudyProfile(false)));
            var filesMenu = new Menu { Background = Ui.Surface, VerticalAlignment = VerticalAlignment.Center };
            var more = new MenuItem { Header = "Profile…" }; var files = new StackPanel(); more.Items.Add(files); filesMenu.Items.Add(more); row.Children.Add(filesMenu);
            row = Ui.Row(files);
            row.Children.Add(Ui.Button("Kopieren…", () => EditStudyProfile(true)));
            row.Children.Add(Ui.Button("Importieren…", () => Safe(() =>
            {
                RequireProfileIdle(); var d = new OpenFileDialog { Filter = "Simulatorprofil|*.json" };
                if (d.ShowDialog(this) != true) return;
                var copy = StudyProfileStore.Read(d.FileName); copy.Id = Guid.NewGuid().ToString("N");
                copy.Map.LocalVirtualInstanceConfirmed = true;
                SaveStudyProfile(copy);
            })));
            row.Children.Add(Ui.Button("Exportieren…", () => Safe(() =>
            {
                RequireProfileIdle(); var d = new SaveFileDialog { Filter = "Simulatorprofil|*.json", FileName = "Simulatorprofil.json" };
                if (d.ShowDialog(this) == true) { Json.SaveAtomic(d.FileName, Json.Write(selectedProfile)); SetStatus("Profil exportiert: " + d.FileName); }
            })));
            profileSummary.VerticalAlignment = VerticalAlignment.Center; profileSummary.Margin = new Thickness(8, 0, 0, 0); profileSummary.TextWrapping = TextWrapping.NoWrap; profileSummary.TextTrimming = TextTrimming.CharacterEllipsis; profileControls.Children.Add(profileSummary);
            foreach (UIElement control in profileControls.Children) DockPanel.SetDock(control, Dock.Left);
            ProfileSelect.SelectionChanged += (_, _) =>
            {
                if (selectingProfile || !(ProfileSelect.SelectedItem is StudyProfile p)) return;
                try { ApplyStudyProfile(p); SetStatus("Profil „" + p.Name + "“ geladen. Anlage zurückgesetzt; nicht verbunden."); }
                catch (Exception e) { selectingProfile = true; ProfileSelect.SelectedItem = selectedProfile; selectingProfile = false; SetStatus(e.Message, true); }
            };
        }
        private void InitializeStudyProfiles()
        {
            profileStore = new StudyProfileStore(userProfileDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles"));
            var errors = new List<string>();
            var defaults = new StudyProfileStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "profiles"));
            foreach (var p in defaults.Load(errors).OrderBy(p => p.Id == "Referenzprojekt" ? 0 : p.Id == "AT2_Uebung" ? 1 : 2))
            { studyProfiles.Add(p); builtInProfiles.Add(p.Id); }
            foreach (var p in profileStore.Load(errors))
                if (studyProfiles.All(x => x.Id != p.Id)) studyProfiles.Add(p);
                else errors.Add("Doppelte Profilkennung: " + p.Id);
            if (studyProfiles.Count == 0)
                studyProfiles.Add(new StudyProfile { Id = "empty", Name = "Eigenes Profil", Plant = activeConfig.Copy() });
            selectedProfile = studyProfiles[0]; ReloadProfileChoices();
            ApplyStudyProfile(selectedProfile);
            if (errors.Count > 0) SetStatus("Einige Profile konnten nicht geladen werden: " + string.Join("; ", errors), true);
        }
        private void RequireProfileIdle() => Numbers.Require(CanEditProfiles, "Zuerst die SPS-Verbindung vollständig trennen. Das aktuelle Profil bleibt erhalten.");
        private void ReloadProfileChoices()
        { selectingProfile = true; ProfileSelect.ItemsSource = studyProfiles.ToArray(); ProfileSelect.SelectedItem = selectedProfile; selectingProfile = false; }
        private void UpdateProfileSummary()
        {
            var required = selectedProfile.DigitalSettings().RequiredSignalIds;
            profileSummary.Text = selectedProfile.Name + " · " + selectedProfile.Map.Signals.Count(s => required.Contains(s.Id) && s.Confirmed && s.ByteOffset.HasValue) + "/" + required.Count + " Signale" + (selectedProfile.Analog != null ? " · analog" : " · digital") + (string.IsNullOrWhiteSpace(selectedProfile.Map.InstanceName) ? " · CPU-Name fehlt" : " · " + selectedProfile.Map.InstanceName);
            profileSummary.ToolTip = selectedProfile.Description;
            RuntimeConsent.Content = new TextBlock { Text = "Siemens-Zugriff für „" + (selectedProfile.Map.InstanceName ?? "CPU noch wählen") + "“ freigeben", TextWrapping = TextWrapping.Wrap };
        }
        internal void ApplyStudyProfile(StudyProfile profile)
        {
            RequireProfileIdle(); profile.Validate();
            // This desktop app has exactly one transport: an existing local PLCSIM Softbus instance.
            profile.Map.LocalVirtualInstanceConfirmed = true;
            liveHost.ClearCompletedView(); cpuIndicator.Reset(); liveSamples.Clear(); liveEvents.Clear(); lastLiveDetail = "";
            Reset(profile.Plant);
            selectedProfile = profile;
            liveConfiguration = null; liveMap = null; liveProfile = null; lastLiveView = null;
            liveDraftPresent = false; livePreviewEdited = true; previewConfiguration = null;
            liveSpeed.Text = profile.DigitalSpeedMmPerSecond.ToString(System.Globalization.CultureInfo.CurrentCulture);
            LivePotentiometer.Maximum = Math.Min(10, profile.Analog?.InputMaximumVolts ?? 10);
            LivePotentiometer.Value = profile.Analog?.InitialPotentiometerVolts ?? 0;
            livePotentiometerPanel.Visibility = profile.Analog != null ? Visibility.Visible : Visibility.Collapsed;
            liveSpeed.IsEnabled = profile.Analog == null;
            liveHallPitch.Text = profile.Plant.HallPitchMm.ToString(System.Globalization.CultureInfo.CurrentCulture);
            liveInitial.Text = profile.Plant.InitialLeftMm.ToString(System.Globalization.CultureInfo.CurrentCulture);
            liveLength.Text = profile.Plant.WorkpieceLengthMm.ToString(System.Globalization.CultureInfo.CurrentCulture);
            RuntimeConsent.IsChecked = CpuConsent.IsChecked = TiaConsent.IsChecked = false;
            ReloadProfileChoices(); UpdateProfileSummary(); Refresh();
        }
        private void EditStudyProfile(bool copy) => Safe(() =>
        {
            RequireProfileIdle();
            var editor = new ProfileEditorWindow(selectedProfile, copy || builtInProfiles.Contains(selectedProfile.Id), SaveStudyProfile) { Owner = this };
            editor.ShowDialog();
        });
        private void SaveStudyProfile(StudyProfile profile)
        {
            RequireProfileIdle(); profileStore.Save(profile); // Only change selection after durable save.
            studyProfiles.RemoveAll(p => p.Id == profile.Id); studyProfiles.Add(profile);
            ApplyStudyProfile(profile); SetStatus("Profil „" + profile.Name + "“ gespeichert und geladen.");
        }
    }
}
