using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Plant.Adapter;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static StudyProfile EditableProfile()
    {
        var p = new StudyProfile { Id = "test", Name = "AT-3 eigene CPU" };
        p.Map.InstanceName = "Eigene_Uebungs_CPU"; p.Map.LocalVirtualInstanceConfirmed = true;
        foreach (var id in DigitalCycleProfile.SignalIds)
        {
            var b = p.Map.Signals.Single(s => s.Id == id);
            int index = Array.IndexOf(DigitalCycleProfile.SignalIds.ToArray(), id);
            SignalAddress.Apply(b, (b.Area == "Input" ? "I12." + index : "Q24." + (index - 4)), 32);
        }
        return p;
    }
    private static void RunStudyProfileTests()
    {
        Test("P01", "Eigene CPU und versetzte digitale Adressen; vollständige Map bleibt streng", () =>
        {
            var p = EditableProfile(); p.Validate(); p.DigitalSettings().RequireMap(p.Map);
            Assert(p.ConnectionProblems().Length == 0, "Konfigurierbare Zuordnung verweigert");
            Reject(() => p.Map.RequireLiveValid()); // SpeedCommandRaw remains required in the full contract.
            Reject(() => new DigitalCycleProfile { Ob1AutomaticImageConfirmed = true }.RequireMap(p.Map));
            var wrong = p.DigitalSettings(); wrong.ExpectedInstanceName = "Andere_CPU"; Reject(() => wrong.RequireMap(p.Map));
        });
        Test("P02", "Offene Übung speicherbar; fehlende Bestätigung und Adressen blockieren Verbindung", () =>
        {
            var p = new StudyProfile { Id = "AT2", Name = "AT-2" }; p.Validate(); Assert(p.ConnectionProblems().Length > 0, "Offene Map live zugelassen");
            p = EditableProfile(); p.Map.LocalVirtualInstanceConfirmed = false; Assert(p.ConnectionProblems().Length > 0, "Unbestätigte CPU zugelassen");
            p.Map.LocalVirtualInstanceConfirmed = true; SignalAddress.Apply(p.Map.Signals[0], "", 32); p.Validate(); Assert(p.ConnectionProblems().Length > 0, "Fehlender Sensor zugelassen");
        });
        Test("P03", "Adressformular erkennt Richtung, Bits, Bereich und Überlappungen", () =>
        {
            var p = EditableProfile(); var left = p.Map.Signals[0];
            Reject(() => SignalAddress.Apply(left, "Q0.0", 32)); Reject(() => SignalAddress.Apply(left, "I0.8", 32));
            SignalAddress.Apply(left, "%I12.1", 32); Reject(p.Validate);
            SignalAddress.Apply(left, "I32.0", 32); Reject(p.Validate);
            SignalAddress.Apply(left, "I12.0", 32); p.Validate(); Assert(SignalAddress.Format(left) == "I12.0", "Adresse nicht normalisiert");
            p.Map.Signals.Single(s => s.Id == "MotorRight").Direction = "plant_to_plc"; Reject(p.Validate);
        });
        Test("P04", "Portable Profile speichern, verschieben, erneut laden; Original bleibt isoliert", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-profile-test-" + Guid.NewGuid().ToString("N"));
            var store = new StudyProfileStore(Path.Combine(dir, "portable", "profiles"));
            var original = EditableProfile(); store.Save(original); var copy = original.Copy(); copy.Name = "Eigene Kopie"; copy.Plant.HallPitchMm = 2;
            Assert(original.Name != copy.Name && original.Plant.HallPitchMm == 1, "Vorlage verändert");
            store.Save(copy); var errors = new List<string>(); var read = store.Load(errors).Single(); Assert(read.Name == copy.Name && errors.Count == 0, "Speichern nicht dauerhaft");
            Directory.Move(Path.Combine(dir, "portable"), Path.Combine(dir, "anderer Ort"));
            var moved = new StudyProfileStore(Path.Combine(dir, "anderer Ort", "profiles")).Load(errors).Single();
            Assert(moved.Map.InstanceName == original.Map.InstanceName && moved.Plant.HallPitchMm == 2, "Verschieben verlor Daten");
            string text = File.ReadAllText(Path.Combine(dir, "anderer Ort", "profiles", "test.json")); Assert(!text.Contains(dir), "Absoluter Rechnerpfad im Profil");
            copy.Id = "../outside"; Reject(() => store.Save(copy));
            File.WriteAllText(Path.Combine(dir, "anderer Ort", "profiles", "bad.json"), "{}");
            Assert(new StudyProfileStore(Path.Combine(dir, "anderer Ort", "profiles")).Load(errors).Count == 1 && errors.Count == 1, "Defektes Profil verschluckt oder gute Profile verloren");
        });
        Test("P05", "Freigabebericht muss exakt zur ausgewählten CPU gehören", () =>
        {
            string text = Json.Write(new { passed = true, cleanupConfirmed = true, operation = "ExplicitRestoreDefault", instance = "Eigene_CPU", observedName = "Eigene_CPU", afterMode = "Default", stage = "Completed", sdkProductVersion = "5.0.0.2", beforeState = "Stop", afterState = "Stop", defaultAndStopObserved = true });
            Assert(DefaultRecoveryReport.ConfirmedState(text, "Eigene_CPU") == "Stop", "Richtige CPU nicht bestätigt");
            Assert(DefaultRecoveryReport.ConfirmedState(text) == null && DefaultRecoveryReport.ConfirmedState(text, "Andere_CPU") == null, "Fremde CPU angenommen");
        });
    }
}
