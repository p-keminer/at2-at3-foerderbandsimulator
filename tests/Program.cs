using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static readonly List<object> results = new();
    private static readonly List<XElement> junit = new();
    private static int failed, assertions;
    private static void Assert(bool value, string detail) { assertions++; if (!value) throw new Exception(detail); }
    private static void Equal(double actual, double expected, double tolerance = 1e-6) => Assert(Math.Abs(actual - expected) <= tolerance, $"Erwartet {expected}, tatsächlich {actual}, Toleranz {tolerance}");
    private static void Reject(Action a) { bool rejected = false; try { a(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Assert(rejected, "Ungültige Aktion wurde angenommen."); }
    private static void Test(string id, string name, Action action)
    {
        var clock = Stopwatch.StartNew(); string? error = null;
        try { action(); } catch (Exception e) { error = e.ToString(); failed++; }
        results.Add(new { id, name, passed = error == null, milliseconds = clock.ElapsedMilliseconds, error });
        var xml = new XElement("testcase", new XAttribute("name", id + " " + name), new XAttribute("time", (clock.Elapsed.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (error != null) xml.Add(new XElement("failure", error)); junit.Add(xml);
        Console.WriteLine($"{(error == null ? "PASS" : "FAIL")} {id} {name}" + (error == null ? "" : "\n" + error));
    }
    private static ActuatorFrame Drive(bool right = true, double volts = 5) => new(right, !right, Analog.OutputRaw(volts));
    private static void Move(PlantModel p, int steps, ActuatorFrame a) { for (int i = 0; i < steps; i++) p.Step(a); }
    private static SimulationSession Reference(double length = 120, double phase = 0, Exercise exercise = Exercise.AT3)
    {
        var s = new SimulationSession(new() { WorkpieceLengthMm = length, InitialLeftMm = 80, HallInitialPhase = phase, CalibrationMmPerPulse = 1 });
        s.SetMode(OperationMode.OfflineReference); s.Controller.Exercise = exercise; s.Resume(); return s;
    }
    private static void Until(SimulationSession s, Func<bool> predicate, int maxSteps = 40000)
    { for (int i = 0; i < maxSteps && !predicate() && !s.Paused; i++) s.Step(); Assert(predicate(), "Zielzustand nicht erreicht: " + s.Controller.Telemetry); }
    private static SignalMap BoundMap()
    {
        var map = SignalMap.Unbound(); map.InstanceName = "EXAMPLE_LOCAL_ONLY"; map.LocalVirtualInstanceConfirmed = true;
        int input = 0, output = 0, marker = 0;
        foreach (var s in map.Signals)
        {
            s.ByteOffset = s.Area == "Input" ? input : s.Area == "Output" ? output : marker;
            int bytes = s.Type == "BOOL" ? 1 : s.Type == "INT" ? 2 : s.Type == "DINT" ? 4 : 8;
            if (s.Area == "Input") input += bytes; else if (s.Area == "Output") output += bytes; else marker += bytes;
            s.BitOffset = s.Type == "BOOL" ? 0 : null; s.AreaSizeBytes = 128; s.Confirmed = true;
        }
        return map;
    }
    private static int Main(string[] args)
    {
        Test("T01", "Stillstand hält HIGH-Phase ohne Flanken", () =>
        {
            var p = new PlantModel(new() { HallInitialPhase = .75 }); var before = p.Sensors;
            Move(p, 1000, new()); Assert(p.Sensors.HallHigh && before.HallHigh, "HIGH verloren");
            var snap = p.Snapshot(new()); Equal(snap.MotorMm, 0); Assert(snap.GeneratedRisingEdges == 0, "Zusatzflanken");
        });
        Test("T02", "Idealmotor rechts/links 50 mm/s über 2 s", () =>
        {
            var p = new PlantModel(new()); Move(p, 2000, Drive()); Equal(p.Snapshot(Drive()).LeftEdgeMm, 150);
            Move(p, 2000, Drive(false)); Equal(p.Snapshot(Drive()).LeftEdgeMm, 50);
        });
        Test("T03", "Richtungskonflikt bleibt als Originalbefehl sichtbar", () =>
        {
            var s = new SimulationSession(new()); s.SetManual(new(true, true, Analog.Nominal)); s.Resume(); s.Advance(1000000);
            var p = s.Snapshot().Plant; Equal(p.MotorMm, 0); Assert(p.DirectionConflict && p.Actuators.MotorLeft && p.Actuators.MotorRight, "Konflikt versteckt");
            Assert(s.Events.Copy().Any(e => e.Detail.Contains("MotorRight")), "Originalbefehl nicht protokolliert");
        });
        Test("T04", "Optische Bedeckungsdauer L/v in beiden Richtungen", () =>
        {
            foreach (bool right in new[] { true, false })
            {
                var p = new PlantModel(new() { InitialLeftMm = right ? 350 : 510 }); int active = 0;
                for (int i = 0; i < 6000; i++) { if (p.Sensors.OpticalHigh) active++; p.Step(Drive(right)); }
                Equal(active * .001, 2.4, .0011);
            }
        });
        Test("T05", "Links 2 / rechts 1, Materialeigenschaften", () =>
        {
            var p = new PlantModel(new()); Assert(p.Sensors.SensorLeftHigh && !p.Sensors.SensorRightHigh, "Sensornummerierung");
            p.Insert(50, 120, false, true); Assert(!p.Sensors.SensorLeftHigh, "Nichtmetall induktiv erkannt");
            p.Insert(450, 120, true, false); Assert(!p.Sensors.OpticalHigh, "Nichtreflektierend erkannt");
        });
        Test("T06", "Polarität ändert nur den elektrischen Pegel", () =>
        {
            var c = new PlantConfiguration(); c.Sensor("left").ActiveHigh = false;
            var p = new PlantModel(c).Snapshot(new()); Assert(p.Left.Ideal && p.Left.Detected && !p.Left.High, "Polarität/Geometrie vermischt");
            c.CalibrationMmPerPulse = 1; var s = new SimulationSession(c); s.SetMode(OperationMode.OfflineReference); s.Resume(); s.Advance(16000000);
            Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, 120, 1.1);
        });
        Test("T07", "100 mm Motorweg erzeugen 100 steigende Hall-Flanken", () =>
        {
            var p = new PlantModel(new()); var edges = new List<HallEdge>(); p.Edge += edges.Add; Move(p, 2000, Drive());
            Equal(p.Snapshot(Drive()).MotorMm, 100); Assert(edges.Count == 200 && edges.Count(e => e.High) == 100, "Hallzahl falsch");
            Assert(edges.Zip(edges.Skip(1), (a, b) => a.TimeUs <= b.TimeUs && a.High != b.High).All(x => x), "Flankenfolge ungültig");
            var fast = new PlantModel(new() { SpeedAt10V = 5000 }); int n = 0; fast.Edge += _ => n++; fast.Step(Drive(volts: 10)); Assert(n == 10, "Mehrere Flanken pro Modellschritt verloren");
        });
        Test("T08", "Stop/Resume, Rückwärtsfahrt und Zählerreset erhalten Phase", () =>
        {
            var p = new PlantModel(new()); Move(p, 15, Drive()); bool level = p.Sensors.HallHigh; long count = p.Snapshot(new()).GeneratedRisingEdges;
            Move(p, 100, new()); Assert(level == p.Sensors.HallHigh && count == p.Snapshot(new()).GeneratedRisingEdges, "Stop setzte Phase zurück");
            Move(p, 15, Drive(false)); Equal(p.Snapshot(new()).MotorMm, 0); Assert(!p.Sensors.HallHigh, "Rückwärtsphase falsch");
            var counter = new SampledHallCounter(true); counter.ResetCount(); Assert(!counter.Sample(new(false, false, false, true, 0, true, 1, 0), Drive()), "Reset erfand Flanke");
        });
        Test("T09", "Identische Snapshots und Flanken bei 1/17/100-ms-UI-Takten", () =>
        {
            SimulationSession Run(int chunk)
            {
                var s = new SimulationSession(new()); s.SetManual(Drive()); s.Resume();
                for (int t = 0; t < 2000; t += chunk) { s.Advance(Math.Min(chunk, 2000 - t) * 1000L); _ = s.Snapshot(); } return s;
            }
            var a = Run(1); foreach (int chunk in new[] { 17, 100 }) { var b = Run(chunk); Assert(a.Snapshot() == b.Snapshot(), "UI-Takt beeinflusst Anlage"); Assert(a.Edges.Copy().SequenceEqual(b.Edges.Copy()), "UI verlor Flanken"); }
        });
        Test("T10", "Simulatorpause friert Zeit und Controller ein", () =>
        {
            var s = Reference(); s.Advance(1000000); s.Pause(); var before = s.Snapshot(); s.Advance(3000000); Assert(before == s.Snapshot(), "Pause instabil");
            s.Resume(); s.Advance(1000); Assert(s.Snapshot().Plant.TimeUs == before.Plant.TimeUs + 1000, "Wandzeitsprung");
        });
        Test("T11", "Vollreset liefert reproduzierbaren Start", () =>
        {
            var a = new SimulationSession(new()); var b = new SimulationSession(new()); Assert(a.Snapshot() == b.Snapshot(), "Reset nicht reproduzierbar");
            Assert(a.Edges.Copy().Length == 0 && a.Samples.Copy().Length == 0 && a.Paused && !a.Output.MotorRight, "Resetliste unvollständig");
        });
        Test("T12", "Drei Kalibrierfahrten, Mittelwert und Nullimpulsablehnung", () =>
        {
            var s = new SimulationSession(new()); s.SetManual(Drive()); s.Resume();
            foreach (int d in new[] { 100, 120, 80 })
            { long before = s.Counter.Rising; s.Advance(d * 20000L); s.Advance(1000); s.Calibration.Add(d, s.Counter.Rising - before); }
            Equal(s.Calibration.Mean ?? -1, 1, .011); Assert(s.Calibration.Runs.Count == 3, "Drei Fahrten fehlen");
            Reject(() => new CalibrationBook().Add(100, 0)); Reject(() => new CalibrationBook().Add(double.NaN, 10));
        });
        Test("T13", "50/120/200-mm-Messungen, Seedphasen und Quantisierungsschranke", () =>
        {
            var random = new Random(1632026);
            foreach (double length in new[] { 50.0, 120.0, 200.0 })
                for (int trial = 0; trial < 5; trial++)
                {
                    var s = Reference(length, trial == 0 ? 0 : random.NextDouble());
                    Until(s, () => s.Controller.Telemetry.State == CycleState.Complete);
                    double measured = s.Controller.Telemetry.MeasuredLengthMm ?? -1000;
                    // Conservative bound: one pitch plus two sensor sample displacements.
                    Equal(measured, length, 1 + 2 * 50 * .001 + 1e-6);
                    Assert(!s.Output.MotorRight && s.Output.LampGreen, "Ende stoppt nicht");
                }
        });
        Test("T14", "Controllervertrag enthält keine wahre Länge oder Modellzähler", () =>
        {
            Assert(typeof(ReferenceController).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(bool), typeof(bool), typeof(bool), typeof(Exercise) }), "Controller erhält Modellzugriff");
            Assert(typeof(SensorFrame).GetProperties().All(p => !p.Name.Contains("Length") && !p.Name.Contains("Count") && !p.Name.Contains("MotorMm")), "Ground truth im Sensorvertrag");
            Assert(typeof(ReferenceController).GetMethods().Single(m => m.Name == "Tick").GetParameters().Single().ParameterType == typeof(SensorFrame), "Falsche Messschnittstelle");
        });
        Test("T15", "Letztes Ergebnis bleibt bis neuer optischer Messung erhalten", () =>
        {
            var s = Reference(); Until(s, () => s.Controller.Telemetry.State == CycleState.Complete); double? result = s.Controller.Telemetry.MeasuredLengthMm;
            s.Controller.ResetMeasurement(); Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, result ?? -2);
            bool hall = s.Plant.Sensors.HallHigh; s.Controller.ResetTestCounter(); Assert(s.Plant.Sensors.HallHigh == hall, "Messreset veränderte Encoder");
            s.Insert(80, 50, true, true); Until(s, () => s.Controller.Telemetry.State == CycleState.Complete); Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, 50, 1.1);
        });
        Test("T16", "AT-3-Betriebsstopp im Messfenster und Fortsetzen", () =>
        {
            var s = Reference(); Until(s, () => s.Controller.Telemetry.MeasurementPulses >= 30); s.Controller.ToggleStop(); s.Advance(1000);
            var before = s.Snapshot(); s.Advance(500000); var after = s.Snapshot();
            Equal(after.Plant.LeftEdgeMm, before.Plant.LeftEdgeMm); Assert(after.Controller.MeasurementPulses == before.Controller.MeasurementPulses && after.Plant.GeneratedRisingEdges == before.Plant.GeneratedRisingEdges, "Stopp setzte Zählung fort");
            Assert(after.Plant.TimeUs > before.Plant.TimeUs, "Betriebsstopp pausiert Simulator");
            s.Controller.ToggleStop(); Until(s, () => s.Controller.Telemetry.State == CycleState.Complete); Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, 120, 1.1);
        });
        Test("T17", "Hand/Automatik gegenseitig getrennt und Endbegrenzung", () =>
        {
            var s = Reference(); s.Controller.ChangeMachineMode(MachineMode.Hand); s.Controller.ToggleStop(); s.Advance(100000);
            Equal(s.Snapshot().Plant.MotorMm, 0); s.Controller.JogRight = true; s.Advance(200000); Assert(s.Output.MotorRight, "Handtaste ohne Wirkung");
            s.Controller.ChangeMachineMode(MachineMode.Automatic); Assert(s.Controller.Stopped && !s.Controller.JogRight, "Wechsel startet unkontrolliert");
            var c = new ReferenceController(true, true, true, Exercise.AT3) { JogLeft = true };
            var a = c.Tick(new(false, false, false, false, 0, true, 0, 0)); Assert(!a.MotorLeft && !a.MotorRight, "Handtaste steuert Automatik");
        });
        Test("T18", "Stopp-Toggle sichtbar und reversibel", () =>
        {
            var c = new ReferenceController(true, true, true, Exercise.AT3); c.ToggleStop(); Assert(c.Telemetry.Stopped, "Toggle 1"); c.ToggleStop(); Assert(!c.Telemetry.Stopped, "Toggle 2");
        });
        Test("T19", "Sensorfehler beeinflussen Messung, Delay/Flicker reproduzierbar", () =>
        {
            foreach (var fault in new[] { SensorFault.StuckLow, SensorFault.StuckHigh })
            { var s = Reference(); s.SetSensorFault("optical", fault); Until(s, () => s.Controller.Telemetry.State == CycleState.Faulted); Assert(s.Controller.Telemetry.MeasuredLengthMm != 120, "Fehler heimlich kompensiert"); }
            var p = new PlantModel(new()); p.SetFault("optical", SensorFault.Delayed); p.Insert(450, 120, true, true);
            Move(p, 99, new()); Assert(!p.Sensors.OpticalHigh, "Delay fehlt"); p.Step(new()); Assert(p.Sensors.OpticalHigh, "Delay endet nicht");
            bool[] Flicker() { var m = new PlantModel(new()); m.SetFault("optical", SensorFault.Flicker); var values = new List<bool>(); for (int i = 0; i < 100; i++) { m.Step(new()); values.Add(m.Sensors.OpticalHigh); } return values.ToArray(); }
            Assert(Flicker().SequenceEqual(Flicker()), "Seed nicht reproduzierbar"); Assert(Flicker().Distinct().Count() == 2, "Flicker ohne Wirkung");
        });
        Test("T20", "Motorwelle, Transportschlupf und Objektblockade sind getrennt", () =>
        {
            var p = new PlantModel(new()) { MotorLocked = true }; Move(p, 2000, Drive()); Equal(p.Snapshot(Drive()).MotorMm, 0);
            p.MotorLocked = false; p.TransportFactor = 0; Move(p, 2000, Drive()); var a = p.Snapshot(Drive()); Equal(a.LeftEdgeMm, 50); Equal(a.MotorMm, 100); Assert(a.GeneratedRisingEdges == 100, "Schlupf stoppt Hall");
            p.TransportFactor = 1; p.ObjectBlocked = true; Move(p, 1000, Drive()); Equal(p.Snapshot(Drive()).LeftEdgeMm, 50); Equal(p.Snapshot(Drive()).BeltMm, 50);
        });
        Test("T21", "Absichtlich langsame Abtastung verliert Flanken und warnt", () =>
        {
            var s = new SimulationSession(new() { ControllerSampleUs = 20000 }); s.SetManual(Drive()); s.Resume(); s.Advance(2000000);
            Assert(s.Snapshot().Plant.AliasingRisk, "Aliasing nicht gemeldet"); Assert(s.Counter.Rising < s.Snapshot().Plant.GeneratedRisingEdges, "Verpasste Flanken wurden aufgefüllt");
        });
        Test("T22", "Konfiguration, NaN/Infinity, dt und Analoggrenzen", () =>
        {
            foreach (var c in new[] { new PlantConfiguration { WorkpieceLengthMm = -1 }, new() { HallPitchMm = 0 }, new() { InitialLeftMm = double.NaN }, new() { SpeedAt10V = double.PositiveInfinity }, new() { SchemaVersion = 2 }, new() { ModelStepUs = 0 }, new() { ControllerSampleUs = 1500 } }) Reject(c.Validate);
            var s = new SimulationSession(new()); Reject(() => s.Advance(1)); Reject(() => s.Insert(999, 120, true, true));
            Equal(Analog.InputVoltage(-27648), -10); Equal(Analog.InputRaw(10), 27648); Equal(Analog.OutputVoltage(-1), 0); Equal(Analog.OutputVoltage(32767), 10);
            Reject(() => Analog.OutputRaw(double.NaN)); Reject(() => Analog.InputRaw(11)); Reject(() => s.SetManual(new(SpeedCommandRaw: 32767)));
        });
        Test("T23", "Signalmap Typen, Richtungen, LREAL/Bit-Überlappung, Bereich", () =>
        {
            Assert(SignalMap.Unbound().Validate(true).Count > 0, "Ungebunden akzeptiert"); Assert(BoundMap().Validate(true).Count == 0, "Gültige Testmap abgelehnt");
            var map = BoundMap(); map.Signals[0].Type = "INT"; Assert(map.Validate(true).Count > 0, "Typ");
            map = BoundMap(); map.Signals[0].Direction = "plc_to_plant"; Assert(map.Validate(true).Count > 0, "Richtung");
            map = BoundMap(); map.Signals.Single(s => s.Id == "ControllerPulseCount").ByteOffset = 4; Assert(map.Validate(true).Any(e => e.Contains("überlappt")), "LREAL/DINT Überlappung");
            map = BoundMap(); map.Signals[1].ByteOffset = 0; Assert(map.Validate(true).Any(e => e.Contains("überlappt")), "Bitüberlappung");
            map = BoundMap(); map.Signals[0].ByteOffset = 128; Assert(map.Validate(true).Count > 0, "Bereich");
            map = BoundMap(); map.Signals[0].BitOffset = 8; Assert(map.Validate(true).Count > 0, "Bit >7");
            map = BoundMap(); map.Signals[0].Symbol = "unaufgeloest"; Assert(map.Validate(true).Count > 0, "Unbewiesene symbolische Zuordnung");
        });
        Test("T24", "Adapter-Vertrag schreibt ausschließlich SensorFrames", () =>
        {
            Assert(typeof(IPlantIoAdapter).GetMethods().Where(m => m.Name.StartsWith("Publish")).Single().GetParameters().Single().ParameterType == typeof(SensorFrame), "Schreibrechte falsch");
            using var fake = new OfflineFakeAdapter(); fake.PublishSensors(new(false, false, false, false, 0, true, 0, 0));
            Reject(() => fake.PublishSensors(new(false, false, false, false, 0, true, 0, 0)));
            var map = BoundMap(); map.Signals = map.Signals.Concat(new[] { new SignalBinding { Id = "HmiStop" } }).ToArray(); Assert(map.Validate(true).Count > 0, "HMI-Schreibsignal zugelassen");
        });
        Test("T25", "Offlineprozess ohne Siemensassembly und direkte Abhängigkeit", () =>
        {
            foreach (var a in new[] { typeof(SimulationSession).Assembly, typeof(IPlantIoAdapter).Assembly, Assembly.GetExecutingAssembly() })
                Assert(a.GetReferencedAssemblies().All(r => !r.Name.StartsWith("Siemens")), "Offline-Build referenziert Siemens");
            Assert(AppDomain.CurrentDomain.GetAssemblies().All(a => !a.GetName().Name!.StartsWith("Siemens")), "Herstellercode geladen");
        });
        Test("T26", "Fehlendes SDK/ExternalPlc startet niemals vorgetäuscht", () =>
        {
            using var adapter = new PlcsimAvailability(); Assert(adapter.Quality == LinkQuality.Unavailable && !adapter.ReadActuators().Valid, "Falscher Verbindungsstatus");
            var s = new SimulationSession(new()); s.SetMode(OperationMode.ExternalPlc); Reject(s.Resume); Reject(() => s.Step(true)); Reject(() => adapter.PublishSensors(s.Plant.Sensors));
        });
        Test("T27", "Disconnect markiert veraltet, pausiert und sperrt Resume", () =>
        {
            var s = Reference(); s.Advance(100000); s.NotifyDisconnect(); var a = s.Snapshot();
            Assert(a.Paused && !a.Plant.Actuators.Valid && a.Quality == LinkQuality.Stale, "Stale unsichtbar"); Reject(s.Resume);
            using var fake = new OfflineFakeAdapter(); fake.Disconnect(); Assert(!fake.ReadActuators().Valid, "Stale Aktoren gültig");
        });
        Test("T28", "Begrenzte Logs und getrennte CSV-Aufzeichnung", () =>
        {
            var log = new BoundedLog<int>(3); for (int i = 0; i < 10; i++) log.Add(i);
            Assert(log.Copy().SequenceEqual(new[] { 7, 8, 9 }) && log.Dropped == 7, "Speicher unbegrenzt");
            string dir = Path.Combine(Path.GetTempPath(), "AT2AT3-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var s = Reference(); s.Advance(1000000); s.CaptureExport().Save(dir);
                Assert(File.ReadAllText(Path.Combine(dir, "input-samples.csv")).StartsWith("time_us"), "CSV fehlt");
                Assert(File.ReadAllLines(Path.Combine(dir, "hall-edges.csv")).Length > 50, "Exakte Flanken fehlen");
                Assert(File.ReadAllText(Path.Combine(dir, "metadata.json")).Contains(s.ProfileHash), "Profilhash fehlt");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        Test("T29", "AT-2/AT-3 Rotsemantik und gleichzeitiges Rot/Gelb", () =>
        {
            foreach (var ex in new[] { Exercise.AT2, Exercise.AT3 })
            {
                var s = Reference(exercise: ex); Until(s, () => s.Controller.Telemetry.MeasurementActive);
                Assert(s.Output.LampRed && s.Output.LampYellow, "Ampel fälschlich exklusiv");
                s.Controller.ToggleStop(); s.Advance(1000); Assert(s.Output.LampRed == (ex == Exercise.AT2) && s.Output.LampYellow, "AT-2/AT-3 vermischt");
            }
        });
        Test("T30", "Ungetestete Livefunktionen bleiben explizit Unavailable", () =>
        {
            Assert(new PlcsimAvailability().Status.Contains("kein Laufzeitnachweis"), "Berichtsehrlichkeit");
            var config = new PlantConfiguration(); config.Validate(); Assert(new SimulationSession(config).Mode == OperationMode.OfflineManual, "Standard nicht offline");
        });
        Test("X01", "Dynamischer Motor, Mindestspannung und auslaufende Welle", () =>
        {
            var p = new PlantModel(new() { DynamicMotor = true, Acceleration = 100, Deceleration = 200, MinimumVoltage = 2 });
            Move(p, 100, Drive(volts: 1)); Equal(p.Snapshot(new()).MotorMm, 0);
            Move(p, 500, Drive()); Equal(p.Snapshot(new()).MotorVelocity, 50, .001); Equal(p.Snapshot(new()).MotorMm, 12.5, .01);
            Move(p, 250, new()); Equal(p.Snapshot(new()).MotorVelocity, 0, .001); Equal(p.Snapshot(new()).MotorMm, 18.75, .02);
        });
        Test("X02", "Anlagenkern kennt keine unsichtbare Endabschaltung", () =>
        {
            var p = new PlantModel(new() { InitialLeftMm = 800 }); Move(p, 2000, Drive()); Assert(p.Sensors.SensorRightHigh, "Endsensor fehlt");
            Move(p, 3000, Drive()); Assert(p.Snapshot(Drive()).ObjectOffBelt && p.Snapshot(Drive()).MotorVelocity > 0, "Kern übernimmt Controllerlogik");
            var s = new SimulationSession(new() { InitialLeftMm = 800 }); s.SetManual(Drive()); s.Resume(); s.Advance(5000000); Assert(s.Paused && s.PauseReason.Contains("ObjectOffBelt"), "Bandverlust nicht sichtbar pausiert");
        });
        Test("X03", "Langer Betriebsstopp verändert Messablauf nicht", () =>
        {
            var s = Reference(); Until(s, () => s.Controller.Telemetry.MeasurementPulses >= 20);
            s.Controller.ToggleStop(); s.Advance(130000000); Assert(s.Controller.Telemetry.MeasurementActive, "Stoppzeit löst Transporttimeout aus");
            s.Controller.ToggleStop(); Until(s, () => s.Controller.Telemetry.State == CycleState.Complete); Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, 120, 1.1);
        });
        Test("X04", "Konfiguration ist isoliert, JSON weist unbekannte Felder ab", () =>
        {
            var config = new PlantConfiguration(); var s = new SimulationSession(config);
            config.ModelStepUs = 0; s.Configuration.ModelStepUs = 0;
            s.Step(true); Assert(s.Snapshot().Plant.TimeUs == 1000, "Konfiguration wurde von außen verändert");
            Reject(() => Json.Read<PlantConfiguration>("{\"SchemaVersion\":1,\"Unbekannt\":2}"));
            Reject(() => Json.Read<PlantConfiguration>("{}"));
            Reject(() => Json.Read<PlantConfiguration>("null"));
        });
        Test("X05", "S7-Adressbytes, Vorzeichen und LREAL-Konvertierung", () =>
        {
            Assert(S7Bytes.Int16(-27648).SequenceEqual(new byte[] { 0x94, 0x00 }), "INT-Bytefolge falsch");
            Equal(S7Bytes.Int16(new byte[] { 0x94, 0x00 }), -27648);
            Equal(S7Bytes.Int32(new byte[] { 0xff, 0xff, 0xff, 0x9c }), -100);
            Equal(S7Bytes.Double(new byte[] { 0x40, 0x5e, 0, 0, 0, 0, 0, 0 }), 120);
            Reject(() => S7Bytes.Double(new byte[] { 0x7f, 0xf8, 0, 0, 0, 0, 0, 0 }));
        });
        Test("X06", "Vorwahl langsam/schnell entsteht aus dem Sensorfenster", () =>
        {
            var s = Reference(); s.Controller.UseSpeedSelection = true; s.Step(); Equal(Analog.OutputVoltage(s.Output.SpeedCommandRaw), 6, .001);
            Until(s, () => s.Controller.Telemetry.MeasurementActive); Equal(Analog.OutputVoltage(s.Output.SpeedCommandRaw), 3.5, .001);
            Until(s, () => s.Controller.Telemetry.State == CycleState.Complete); Equal(s.Controller.Telemetry.MeasuredLengthMm ?? -1, 120, 1.2);
        });
        Test("X07", "Falsche Kalibrierung skaliert Ergebnis bei identischen Messimpulsen", () =>
        {
            var measurements = new List<object>(); long? expectedPulses = null; long? expectedGenerated = null;
            foreach (double constant in new[] { .5, 1.0, 2.5 })
            {
                var s = Reference(); s.Controller.Calibration = constant;
                Until(s, () => s.Controller.Telemetry.State == CycleState.Complete);
                var t = s.Controller.Telemetry; var p = s.Snapshot().Plant;
                expectedPulses ??= t.MeasurementPulses; expectedGenerated ??= p.GeneratedRisingEdges;
                Assert(t.MeasurementPulses == expectedPulses && p.GeneratedRisingEdges == expectedGenerated, "Kalibrierung beeinflusst Geometrie/Hallzählung");
                Equal(p.TrueLengthMm, 120); Equal(s.Configuration.HallPitchMm, 1);
                Equal(t.MeasuredLengthMm ?? -1, t.MeasurementPulses * constant);
                Equal((t.MeasuredLengthMm ?? -1) / constant, 120, 1.1);
                measurements.Add(new { constant, t.MeasurementPulses, t.MeasuredLengthMm, p.TrueLengthMm, hallPitchMm = s.Configuration.HallPitchMm, p.GeneratedRisingEdges });
            }
            calibrationEvidence = measurements;
        });
        RunCouplingTests();
        RunMinimalProbeTests();
        RunDigitalIntegrationTests();
        string directory = args.Length > 0 ? args[0] : "test-results"; Directory.CreateDirectory(directory);
        Json.SaveAtomic(Path.Combine(directory, "core-tests.json"), Json.Write(new { schemaVersion = 1, passed = failed == 0, tests = results.Count, assertions, failed, cases = results, note = "Nur tatsächlich ausgeführte Offlineprüfungen. Kein Siemens-Livetest; WPF getrennt." }));
        Json.SaveAtomic(Path.Combine(directory, "calibration-scaling.json"), Json.Write(calibrationEvidence));
        new XDocument(new XElement("testsuite", new XAttribute("tests", results.Count), new XAttribute("failures", failed), junit)).Save(Path.Combine(directory, "core-tests.junit.xml"));
        Console.WriteLine($"{results.Count - failed}/{results.Count} bestanden; {assertions} Assertions; {failed} Fehler.");
        return failed == 0 ? 0 : 1;
    }
}
