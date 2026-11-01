using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    internal sealed class ProfileEditorWindow : Window
    {
        private readonly StudyProfile draft;
        private readonly TextBox name, instance, inputSize, outputSize, speed;
        private readonly Dictionary<string, TextBox> addresses = new();
        private readonly Dictionary<string, TextBox> dimensions = new();
        private readonly List<Action<PlantConfiguration>> applyModel = new();
        private readonly TextBlock feedback = Ui.Text("");
        internal StudyProfile? Result { get; private set; }
        internal ProfileEditorWindow(StudyProfile source, bool copy, Action<StudyProfile>? save = null)
        {
            draft = source.Copy();
            if (copy) { draft.Id = Guid.NewGuid().ToString("N"); draft.Name += " (eigene Kopie)"; }
            Title = "Profil einrichten"; Width = Math.Min(760, SystemParameters.WorkArea.Width); Height = Math.Min(820, SystemParameters.WorkArea.Height); MinWidth = 650; MinHeight = 550;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.Background; Foreground = Ui.Ink;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
            var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            footer.Children.Add(feedback); var buttons = Ui.Row(footer);
            buttons.Children.Add(Ui.Button("Profil speichern", () => Attempt(() => { var result = ReadDraft(); save?.Invoke(result); Result = result; DialogResult = true; })));
            buttons.Children.Add(Ui.Button("Verbindungsvoraussetzungen prüfen", () => Attempt(() =>
            { var errors = ReadDraft().ConnectionProblems(); feedback.Text = errors.Length == 0 ? "Zuordnung vollständig. Liveverbindung noch nicht geprüft." : string.Join("\n", errors); })));
            var cancel = Ui.Button("Abbrechen", () => { DialogResult = false; }); cancel.IsCancel = true; buttons.Children.Add(cancel);
            var layout = new StackPanel(); root.Children.Add(Ui.Scroll(layout));
            var p = Ui.Section(layout, "Profil und CPU");
            name = TextField(p, "Profilname", draft.Name);
            instance = TextField(p, "CPU-Instanzname aus PLCSIM Advanced", draft.Map.InstanceName ?? "");
            p.Children.Add(Ui.Text("Verbindung: lokale virtuelle CPU · PLCSIM Advanced / Softbus", 12, true));
            p.Children.Add(Ui.Text("Speichern verbindet die CPU nicht. Offene Adressen können später ergänzt werden.", 12));
            p = Ui.Section(layout, "Signale zuordnen");
            p.Children.Add(Ui.Text("Adressen aus deinen TIA-Variablen übernehmen, z. B. I0.0 oder Q0.1. Leer = noch offen.", 12));
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            p.Children.Add(grid);
            int row = 0;
            foreach (var id in DigitalCycleProfile.SignalIds)
            {
                var binding = draft.Map.Signals.Single(s => s.Id == id);
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new Label { Content = SignalLabel(id), ToolTip = id + " · BOOL · " + (binding.Direction == "plant_to_plc" ? "Anlage → SPS" : "SPS → Anlage") };
                var box = new TextBox { Text = SignalAddress.Format(binding), Margin = new Thickness(4), Padding = new Thickness(6), MinHeight = 30 };
                System.Windows.Automation.AutomationProperties.SetName(box, id + " Adresse"); label.Target = box;
                Grid.SetRow(label, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1); grid.Children.Add(label); grid.Children.Add(box); addresses.Add(id, box); row++;
            }
            var areas = Ui.Fold(p, "E/A-Bereiche und digitale Geschwindigkeit");
            inputSize = Ui.Number(areas, "Eingangsbereich [Bytes, aus TIA]", draft.Map.Signals.Where(s => s.Area == "Input").Max(s => s.AreaSizeBytes));
            outputSize = Ui.Number(areas, "Ausgangsbereich [Bytes, aus TIA]", draft.Map.Signals.Where(s => s.Area == "Output").Max(s => s.AreaSizeBytes));
            speed = Ui.Number(areas, "Digitale Modellgeschwindigkeit [mm/s, max. 100]", draft.DigitalSpeedMmPerSecond);
            areas.Children.Add(Ui.Text("Dieses Profil verwendet digitale Ein- und Ausgänge.", 12));
            var model = Ui.Fold(layout, "Anlagenparameter · Beispielwerte");
            Add(model, "Bandlänge [mm]", draft.Plant.BeltLengthMm, (c, v) => c.BeltLengthMm = v);
            Add(model, "Startposition [mm]", draft.Plant.InitialLeftMm, (c, v) => c.InitialLeftMm = v);
            Add(model, "Prüflingslänge [mm]", draft.Plant.WorkpieceLengthMm, (c, v) => c.WorkpieceLengthMm = v);
            Add(model, "Hallteilung [mm/Impuls]", draft.Plant.HallPitchMm, (c, v) => c.HallPitchMm = v);
            Add(model, "Geschwindigkeit bei 10 V [mm/s]", draft.Plant.SpeedAt10V, (c, v) => c.SpeedAt10V = v);
            Add(model, "Beschleunigung [mm/s²]", draft.Plant.Acceleration, (c, v) => c.Acceleration = v);
            Add(model, "Verzögerung [mm/s²]", draft.Plant.Deceleration, (c, v) => c.Deceleration = v);
            Add(model, "Anfahrspannung [V]", draft.Plant.MinimumVoltage, (c, v) => c.MinimumVoltage = v);
            Add(model, "Hall-Startphase [0 bis < 1]", draft.Plant.HallInitialPhase, (c, v) => c.HallInitialPhase = v);
            Add(model, "Modellschritt [µs]", draft.Plant.ModelStepUs, (c, v) => c.ModelStepUs = Whole(v));
            Flag(model, "Prüfling aus Metall", draft.Plant.Metallic, (c, v) => c.Metallic = v);
            Flag(model, "Prüfling optisch erkennbar", draft.Plant.Reflective, (c, v) => c.Reflective = v);
            Flag(model, "Beschleunigung und Verzögerung simulieren", draft.Plant.DynamicMotor, (c, v) => c.DynamicMotor = v);
            foreach (var sensor in draft.Plant.Sensors)
            {
                string id = sensor.Id;
                var section = Ui.Fold(model, id == "left" ? "Sensor links" : id == "right" ? "Sensor rechts" : "Optischer Sensor");
                Add(section, "Position " + id + " [mm]", sensor.PositionMm, (c, v) => c.Sensor(id).PositionMm = v);
                Add(section, "Schaltzone " + id + " [mm]", sensor.ZoneMm, (c, v) => c.Sensor(id).ZoneMm = v);
                Add(section, "Hysterese " + id + " [mm]", sensor.HysteresisMm, (c, v) => c.Sensor(id).HysteresisMm = v);
                Add(section, "Verzögerung " + id + " [µs]", sensor.DelayUs, (c, v) => c.Sensor(id).DelayUs = Whole(v));
                Flag(section, "Aktiv HIGH", sensor.ActiveHigh, (c, v) => c.Sensor(id).ActiveHigh = v);
            }
        }
        internal static string SignalLabel(string id) => id switch {
            "SensorLeftHigh" => "Sensor links · I", "OpticalHigh" => "Optischer Sensor · I", "SensorRightHigh" => "Sensor rechts · I", "HallHigh" => "Hall-Impulse · I",
            "MotorRight" => "Motor rechts · Q", "MotorLeft" => "Motor links · Q", "LampRed" => "Lampe rot · Q", "LampYellow" => "Lampe gelb · Q", _ => "Lampe grün · Q" };
        private static TextBox TextField(Panel p, string label, string text) { var box = Ui.Number(p, label, 0); box.Text = text; return box; }
        private static long Whole(double n) { Numbers.Require(n == Math.Truncate(n) && n >= int.MinValue && n <= int.MaxValue, "Ganze Zahl im gültigen Bereich eingeben."); return (long)n; }
        private void Add(Panel p, string label, double value, Action<PlantConfiguration, double> set)
        { var box = Ui.Number(p, label, value); dimensions.Add(label, box); applyModel.Add(c => set(c, Ui.Value(box))); }
        private void Flag(Panel p, string label, bool value, Action<PlantConfiguration, bool> set)
        { var box = Ui.Check(label, value); p.Children.Add(box); applyModel.Add(c => set(c, box.IsChecked == true)); }
        private void Attempt(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is OverflowException || e is System.IO.IOException || e is UnauthorizedAccessException) { feedback.Text = e.Message; feedback.Foreground = Ui.Warning; } }
        internal StudyProfile ReadDraft()
        {
            var result = draft.Copy(); result.Name = name.Text.Trim(); result.Map.InstanceName = instance.Text.Trim();
            result.Map.LocalVirtualInstanceConfirmed = true; result.DigitalSpeedMmPerSecond = Ui.Value(speed);
            int inputs = (int)Whole(Ui.Value(inputSize)), outputs = (int)Whole(Ui.Value(outputSize));
            Numbers.Require(inputs >= 0 && inputs <= 65536 && outputs >= 0 && outputs <= 65536, "E/A-Bereich: 0 (offen) bis 65536 Bytes.");
            foreach (var binding in result.Map.Signals.Where(b => addresses.ContainsKey(b.Id)))
                SignalAddress.Apply(binding, addresses[binding.Id].Text, binding.Area == "Input" ? inputs : outputs);
            foreach (var apply in applyModel) apply(result.Plant);
            result.Validate(); return result;
        }
    }
}
