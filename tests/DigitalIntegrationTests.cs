using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static DigitalCycleProfile DigitalProfile() => new() { Ob1AutomaticImageConfirmed = true, TestSpeedMmPerSecond = 100 };
    private static SignalMap DigitalMap()
    {
        var map = SignalMap.Unbound(); map.InstanceName = "AT2_Kopplung"; map.LocalVirtualInstanceConfirmed = true;
        for (int i = 0; i < DigitalCycleProfile.SignalIds.Count; i++)
        { var b = map.Signals.Single(s => s.Id == DigitalCycleProfile.SignalIds[i]); b.ByteOffset = 0; b.BitOffset = i < 4 ? i : i - 4; b.AreaSizeBytes = 4; b.Confirmed = true; }
        return map;
    }
    // Deliberately our testdouble, never a Siemens CPU. Decisions use only published sensor bits.
    private sealed class DigitalEndpointDouble : IPlcEndpointFactory, IPlcEndpoint, IEndpointInspection
    {
        public string InstanceName => "AT2_Kopplung";
        public EndpointEvidence Evidence => EndpointEvidence.TestDouble;
        public bool UsesDigitalCycleProfile => true;
        public event Action<SyncNotification>? SyncPoint;
        public event Action<string>? Invalidated;
        public SensorFrame? Published;
        public int Opens, Writes, Continues, Disposals, PlcCounter;
        public bool Automatic = true, Permission = true, Active, Right, Conflict, NoInitialEvent, PreviousGate;
        public bool LampRed, LampYellow;
        public bool FailInspection;
        public bool FailWrite;
        public Action<DigitalEndpointDouble>? OnContinue;
        public long[] Deltas = { 1000000, 1100000, 900000, 1200000 };
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization a, CancellationToken c) { Opens++; return this; }
        public void BeginSingleStepInputs() { if (!NoInitialEvent) SyncPoint?.Invoke(new(0, 1008798, 0, 1)); }
        public PlcReadback ReadOutputs(ClockGrant grant) => new(new(Right, Conflict, Analog.Nominal,
            LampRed: LampRed, LampYellow: LampYellow,
            LampGreen: Published?.SensorLeftHigh ?? false, Sequence: grant.Sequence, TimeUs: grant.TimeUs));
        public void WriteInputs(SensorFrame frame) { if (FailWrite) throw new InvalidOperationException("input write failure double"); Published = frame; Writes++; }
        public void ContinueToNextSyncPoint()
        {
            Continues++; var s = Published!; bool before = Active;
            if (s.SensorLeftHigh) Active = true;
            if (s.SensorRightHigh) Active = false;
            bool gate = Permission && s.HallHigh && s.OpticalHigh && Active;
            if (!before && Active) PlcCounter = 0; else if (gate && !PreviousGate) PlcCounter++;
            PreviousGate = gate;
            Right = Automatic && Permission && Active && !s.SensorRightHigh;
            OnContinue?.Invoke(this);
            long delta = Deltas[(Continues - 1) % Deltas.Length];
            SyncPoint?.Invoke(new(0, delta, delta, 1));
        }
        public void Break(string reason) => Invalidated?.Invoke(reason);
        public EndpointInspection Inspect()
        { if (FailInspection) throw new InvalidOperationException("inspection failure double"); return new(InstanceName, "Freeze", "SingleStep_C", Array.Empty<string>()); }
        public void Dispose() { Disposals++; }
    }
    private static Task<DigitalRunResult> DigitalRun(DigitalEndpointDouble endpoint, double length = 120, DigitalRunLimits? limits = null, CancellationToken cancellation = default)
    {
        var configuration = new PlantConfiguration { WorkpieceLengthMm = length, HallPitchMm = 1.2, CalibrationMmPerPulse = 1.2 };
        return DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), configuration,
            limits ?? new DigitalRunLimits { MaxAdvances = 15000, MaxVirtualSeconds = 15, WallTimeoutSeconds = 30, RequireStationary = false, RequireCompletedTransport = true }, FakeApproval, cancellation);
    }
    private static void RunDigitalIntegrationTests()
    {
        Test("D01", "Neun bestätigte Bits; Vollmap bleibt streng, keine Marker/DB/Analog-Ersatzadressen", () =>
        {
            var profile = DigitalProfile(); profile.RequireMap(DigitalMap()); Reject(() => DigitalMap().RequireLiveValid());
            foreach (string id in DigitalCycleProfile.SignalIds)
            {
                var map = DigitalMap(); map.Signals.Single(s => s.Id == id).Confirmed = false; Reject(() => profile.RequireMap(map));
                map = DigitalMap(); map.Signals.Single(s => s.Id == id).BitOffset = 7; Reject(() => profile.RequireMap(map));
                map = DigitalMap(); map.Signals.Single(s => s.Id == id).ByteOffset = null; Reject(() => profile.RequireMap(map));
            }
            Assert(DigitalMap().Signals.Single(s => s.Id == "LampYellow").BitOffset == 3 &&
                DigitalMap().Signals.Single(s => s.Id == "LampRed").BitOffset == 4, "Bestätigte Lampenadressen vertauscht");
            var extra = DigitalMap(); var b = extra.Signals.Single(s => s.Id == "MeasurementActive"); b.ByteOffset = 0; b.BitOffset = 1; b.AreaSizeBytes = 4; b.Confirmed = true;
            Reject(() => profile.RequireMap(extra));
            var other = DigitalMap(); other.InstanceName = "other"; Reject(() => profile.RequireMap(other));
            foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity, 101 }) { var p = DigitalProfile(); p.TestSpeedMmPerSecond = bad; Reject(p.Validate); }
        });
        AsyncTest("D02", "Digitaler C-Zeitursprung, variable Zyklen, kausale Bewegung und begrenzter Endhalt", async () =>
        {
            var endpoint = new DigitalEndpointDouble(); var r = await DigitalRun(endpoint, limits: new() { RequireStationary = false, MaxAdvances = 5 });
            Assert(r.Passed && r.Samples.Count == 6 && r.ContinueCalls == 5 && endpoint.Continues == 5 && endpoint.Disposals == 1, "Schrittgrenze/Cleanup falsch");
            Assert(r.Samples[0].RawSync.SinceAnyNs == 1008798 && r.Samples[0].Coupling.VirtualTimeNs == 0, "Beobachteter C-Ursprung falsch");
            Equal(r.Samples[0].Plant.LeftEdgeMm, 50); Equal(r.Samples[1].Plant.LeftEdgeMm, 50);
            Equal(r.Samples[2].Plant.LeftEdgeMm, 50.11, 1e-9);
            Assert(!r.Samples[0].Coupling.Applied.MotorRight && r.Samples[1].Coupling.Applied.MotorRight, "SPS-Befehl vor Sensorübernahme erfunden");
            Assert(r.Samples.All(s => s.Published.TimeUs == s.Coupling.VirtualTimeNs / 1000 && s.Coupling.Evidence == EndpointEvidence.TestDouble), "Zeit/Beweisart falsch");
            Assert(r.BeforeDisconnect?.OperatingMode == "SingleStep_C" && !r.PlcMeasurementVerified, "Zustand/Messnachweis falsch");
        });
        AsyncTest("D03", "Drei Werkstücklängen: Modellbewegung, Endabschaltung, abgetastete Hallzählung mit Testdouble", async () =>
        {
            foreach (double length in new[] { 60.0, 120, 200 })
            {
                var endpoint = new DigitalEndpointDouble(); var r = await DigitalRun(endpoint, length);
                Assert(r.Passed && r.TransportCompleted && r.EndReason == "TransportCompleted", "Durchlauf unvollständig: " + r.Error);
                Assert(r.FirstMotorRightNs < r.OpticalEnterNs && r.OpticalEnterNs < r.OpticalLeaveNs && r.OpticalLeaveNs < r.RightSensorNs && r.RightSensorNs <= r.MotorStoppedNs, "Geometrische Ereignisfolge falsch");
                Equal(endpoint.PlcCounter * 1.2, length, 2.4);
                Assert(endpoint.PlcCounter == r.DiagnosticSampledOpticalPulses && !r.PlcMeasurementVerified, "Testdoublezählung falsch als nativer SPS-Nachweis behandelt");
                Assert(r.Samples.Last().Plant.MotorVelocity == 0 && !r.Samples.Last().Coupling.Applied.MotorRight && r.Samples.Last().Plant.Right.Detected, "Endsensor-Stopp fehlt");
            }
        });
        AsyncTest("D04", "Richtungskonflikt und stationärer Motorbefehl stoppen vor weiterer Freigabe", async () =>
        {
            var conflict = new DigitalEndpointDouble { Right = true, Conflict = true }; var r = await DigitalRun(conflict);
            Assert(!r.Passed && conflict.Writes == 0 && conflict.Continues == 0 && r.Error!.Contains("Richtungskonflikt"), "Konflikt durchgelassen");
            var moving = new DigitalEndpointDouble(); r = await DigitalRun(moving, limits: new());
            Assert(!r.Passed && moving.Continues == 1 && r.Error!.Contains("stationären"), "Stationärer Test fährt weiter");
        });
        AsyncTest("D05", "CPU-STOP/Disconnect invalidieren; neue Verbindung benötigt neue Sitzung", async () =>
        {
            foreach (string reason in new[] { "CPU Stop", "Runtime lost" })
            {
                var endpoint = new DigitalEndpointDouble { OnContinue = e => e.Break(reason) }; var r = await DigitalRun(endpoint);
                Assert(!r.Passed && endpoint.Continues == 1 && endpoint.Disposals == 1 && r.Error != null, "Fehler ignoriert/automatisch fortgesetzt");
                int original = endpoint.Continues;
                var fresh = new DigitalEndpointDouble { Automatic = false }; var retry = await DigitalRun(fresh, limits: new());
                Assert(retry.Passed && fresh.Opens == 1 && endpoint.Continues == original, "Neue Verbindung nutzt alte Sitzung");
            }
        });
        AsyncTest("D06", "Nullzeit und tatsächlicher zu großer Hall-Abtastabstand werden abgewiesen", async () =>
        {
            foreach (long delta in new[] { 0L, 4000000 })
            {
                var endpoint = new DigitalEndpointDouble { Deltas = new[] { delta } }; var r = await DigitalRun(endpoint);
                Assert(!r.Passed && endpoint.Continues <= 2 && r.Error != null && !r.PlcMeasurementVerified, "Zeit/Aliasing wurde verborgen");
            }
        });
        AsyncTest("D07", "Virtuelle Begrenzung ohne Fahrt ist kein bestandener Durchlauf; Cleanupprüfung bleibt sichtbar", async () =>
        {
            var endpoint = new DigitalEndpointDouble { Automatic = false }; var r = await DigitalRun(endpoint, limits: new() { RequireStationary = false, RequireCompletedTransport = true, MaxAdvances = 4 });
            Assert(!r.Passed && !r.TransportCompleted && r.EndReason == "AdvanceLimit" && endpoint.Continues == 4, "Fehlende SPS-Freigabe als Durchlauf bestanden");
            endpoint = new DigitalEndpointDouble { Automatic = false, FailInspection = true }; r = await DigitalRun(endpoint, limits: new());
            Assert(!r.Passed && r.InspectionError != null && r.CleanupConfirmed && endpoint.Disposals == 1, "Unbestätigter CPU-Zustand verdeckt");
        });
        AsyncTest("D08", "Abbruch beim ersten Sync-Warten beendet Sitzung ohne Sensor-/Motorvorgabe", async () =>
        {
            var endpoint = new DigitalEndpointDouble { NoInitialEvent = true }; using var cancel = new CancellationTokenSource(30);
            var r = await DigitalRun(endpoint, cancellation: cancel.Token);
            Assert(!r.Passed && endpoint.Writes == 0 && endpoint.Continues == 0 && endpoint.Disposals == 1, "Abbruch ohne Wirkung");
        });
        Test("D09", "Digital-CLI getrennt von Minimal-/Vollprofil; feste Grenzen und explizite Bewegungsfreigabe", () =>
        {
            string[] args = { "--freigabe-runtime", "--freigabe-cpu-schritte", "--digital-plant", "--confirm-tia-digital", "--cycle-control-point", "--confirm-ob1-automatic", "--map", "map.json", "--profile", "digital.json", "--plant", "plant.json", "--report", "out.json" };
            Assert(ProbeArguments.Parse(args).Digital, "Digitalflag fehlt");
            foreach (string required in new[] { "--confirm-tia-digital", "--cycle-control-point", "--digital-plant" }) Reject(() => ProbeArguments.Parse(args.Where(a => a != required).ToArray()));
            Reject(() => ProbeArguments.Parse(args.Concat(new[] { "--require-transport" }).ToArray()));
            Reject(() => ProbeArguments.Parse(args.Concat(new[] { "--partition", "0" }).ToArray()));
            Reject(() => new DigitalRunLimits { MaxAdvances = 50001 }.Validate());
            Reject(() => new DigitalRunLimits { WallTimeoutSeconds = 301 }.Validate());
        });
        AsyncTest("D10", "Drei unabhängige SPS-Lampen über Bericht, Prozessdaten und Darstellungsframe; keine Ersatzmesswerte", async () =>
        {
            var endpoint = new DigitalEndpointDouble { Automatic = false };
            endpoint.OnContinue = e => { e.LampRed = (e.Continues & 1) != 0; e.LampYellow = (e.Continues & 2) != 0; };
            var result = await DigitalRun(endpoint, limits: new() { MaxAdvances = 4 });
            Assert(result.Passed && result.CleanupConfirmed, "Lampentestdouble nicht beendet");
            foreach (var sample in result.Samples)
            {
                var wire = DigitalProgress.From(sample);
                var copy = Json.Read<DigitalProgress>(Json.Write(wire));
                var view = copy.ToActuatorFrame(true);
                Assert(copy.SchemaVersion == 2 && copy.TimeNs == sample.Coupling.VirtualTimeNs && copy.Position == sample.Plant.LeftEdgeMm,
                    "Prozessdaten verändert");
                Assert(view.LampRed == sample.Coupling.Applied.LampRed && view.LampYellow == sample.Coupling.Applied.LampYellow &&
                    view.LampGreen == sample.Coupling.Applied.LampGreen && view.Sequence == sample.Coupling.Sequence,
                    "Lampen auf dem Weg zur Darstellung verloren/vertauscht");
                Assert(!copy.ToActuatorFrame(false).Valid && view.SpeedCommandRaw == 0, "Historischer Wert gültig oder Analogwert erfunden");
            }
            Assert(result.Samples.Skip(1).Select(s => (s.Coupling.Applied.LampRed ? 1 : 0) | (s.Coupling.Applied.LampYellow ? 2 : 0))
                .SequenceEqual(new[] { 1, 2, 3, 0 }), "Rot/Gelb nicht unabhängig und gleichzeitig darstellbar");
            Assert(!typeof(DigitalProgress).GetProperties().Any(p => p.Name.Contains("MeasuredLength")), "SPS-Länge erfunden");
        });
        AsyncTest("D11", "Abbruch oder Invalidierung während Fortschrittsmeldung verhindert jede weitere CPU-Fortschaltung", async () =>
        {
            foreach (bool invalidation in new[] { false, true })
            {
                var endpoint = new DigitalEndpointDouble { Automatic = false };
                using var cancellation = new CancellationTokenSource();
                var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(),
                    new PlantConfiguration { HallPitchMm = 1.2 }, new DigitalRunLimits(), FakeApproval, cancellation.Token,
                    _ => { if (invalidation) endpoint.Break("STOP during progress"); else cancellation.Cancel(); });
                Assert(!result.Passed && result.EndReason == "FaultOrCanceled" && result.Error != null, "Später Abbruch als Erfolg gewertet");
                Assert(endpoint.Writes == 1 && endpoint.Continues == 0 && result.ContinueCalls == 0, "Nach Abbruch/Invalidierung noch CPU-Fortschaltung ausgelöst oder fälschlich gezählt");
                Assert(endpoint.Disposals == 1 && result.CleanupConfirmed, "Abgebrochene Sitzung nicht bereinigt");
            }
        });
        Test("D12", "Richtungsumkehr nahe Hall-Flanke: zwei verborgene Flanken trotz kleiner Strecke abweisen", () =>
        {
            // A 0.075 micrometre margin lets braking cross the boundary and reversal cross it again.
            // The geometric p/(4*v) bound alone passes; an end-level sample loses both edges.
            var configuration = new PlantConfiguration { DynamicMotor = true, Acceleration = 10000, Deceleration = 10000,
                ModelStepUs = 100, HallPitchMm = 1.2, HallInitialPhase = .5 - .000075 / 1.2 };
            var forward = Drive(true, 10); var reverse = Drive(false, 10);
            var witness = new PlantModel(configuration);
            witness.StepNanoseconds(forward, 100000);
            bool initialHall = witness.Sensors.HallHigh;
            int actualEdges = 0; witness.Edge += _ => actualEdges++;
            for (int i = 0; i < 3; i++) witness.StepNanoseconds(reverse, 100000);
            Assert(actualEdges == 2 && witness.Sensors.HallHigh == initialHall, "Regressionsszene enthält keine verborgenen Hall-Flanken");
            Assert(Math.Abs(witness.Snapshot(reverse).MotorVelocity) * .0003 < configuration.HallPitchMm / 4,
                "Test muss die bisherige Geschwindigkeitsgrenze einhalten");
            var external = new ExternalPlantSimulation(configuration);
            external.AdvanceTo(100000, forward, CancellationToken.None);
            string? failure = null;
            try { external.AdvanceTo(400000, reverse, CancellationToken.None); }
            catch (InvalidOperationException e) { failure = e.Message; }
            Assert(failure != null && failure.Contains("Aliasing"), "Zwei Hall-Flanken wurden als ein unverändertes Eingangsbit veröffentlicht");
        });
        AsyncTest("D13", "Großer Laufbericht über 2 MiB bleibt vollständig parsebar; kleines Konfigurationslimit bleibt bestehen", async () =>
        {
            // Repeated snapshots stress the report format only; this is not a 4096-cycle live claim.
            var result = await DigitalRun(new DigitalEndpointDouble { Automatic = false }, limits: new() { MaxAdvances = 1 });
            Assert(result.Passed && result.CleanupConfirmed, "Berichtsvorlage benötigt abgeschlossene Testdouble-Sitzung");
            var template = result.Samples.Last();
            result.Samples.Clear();
            const int count = 4096;
            for (int i = 0; i < count; i++)
                result.Samples.Add(template with { Coupling = template.Coupling with { Sequence = i, VirtualTimeNs = i * 1000000L },
                    Published = template.Published with { Sequence = i, TimeUs = i * 1000L } });
            const string endMarker = "REPORT COMPLETE: äöü \\\"\nEnde";
            var report = new { schemaVersion = 1, serializationFixtureOnly = true, result, endMarker };
            string encoded = Json.WriteReport(report);
            Assert(encoded.Length > 2 * 1024 * 1024 && encoded.Length < Json.MaxReportCharacters, "Testbericht überschreitet das frühere 2-MiB-Limit nicht");
            Reject(() => Json.Write(report));
            Reject(() => Json.Read<object>(encoded));
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AT2AT3-report-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Json.SaveAtomic(path, encoded);
                string persisted = System.IO.File.ReadAllText(path);
                Assert(persisted == encoded, "Großer Bericht nicht vollständig atomar gespeichert");
                var parser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = Json.MaxReportCharacters };
                var root = (System.Collections.Generic.Dictionary<string, object>)parser.DeserializeObject(persisted);
                Assert((int)root["schemaVersion"] == 1 && (bool)root["serializationFixtureOnly"] && (string)root["endMarker"] == endMarker,
                    "Berichtsrahmen oder Endmarker abgeschnitten/verändert");
                var restored = (System.Collections.Generic.Dictionary<string, object>)root["result"];
                var samples = (object[])restored["Samples"];
                Assert(samples.Length == count && (bool)restored["Passed"] && (bool)restored["CleanupConfirmed"] && !(bool)restored["PlcMeasurementVerified"],
                    "Berichtslänge oder Ergebnis-/Cleanup-/Nachweisstatus verändert");
                var last = (System.Collections.Generic.Dictionary<string, object>)samples[count - 1];
                var coupling = (System.Collections.Generic.Dictionary<string, object>)last["Coupling"];
                Assert(Convert.ToInt64(coupling["Sequence"]) == count - 1 && Convert.ToInt64(coupling["VirtualTimeNs"]) == (count - 1) * 1000000L,
                    "Letzter vollständiger Sample fehlt oder Zeitfolge abgeschnitten");
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        });
        AsyncTest("D14", "Später Halt nach Überfahren des rechten Sensors besteht keine Endabschaltung", async () =>
        {
            // Move the right sensor inward so this faulty controller can overshoot without leaving the belt.
            var configuration = new PlantConfiguration { HallPitchMm = 1.2 };
            configuration.Sensor("optical").PositionMm = 300;
            configuration.Sensor("right").PositionMm = 600;
            bool sawRight = false, leftRightAgain = false;
            var endpoint = new DigitalEndpointDouble { Deltas = new[] { 1000000L } };
            endpoint.OnContinue = e =>
            {
                sawRight |= e.Published!.SensorRightHigh;
                leftRightAgain |= sawRight && !e.Published.SensorRightHigh;
                e.Right = !leftRightAgain; // Deliberate bad testdouble: ignore right sensor until already passed.
            };
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), configuration,
                new DigitalRunLimits { RequireStationary = false, RequireCompletedTransport = true, MaxAdvances = 5700, MaxVirtualSeconds = 10 }, FakeApproval);
            var final = result.Samples.Last();
            Assert(sawRight && leftRightAgain && result.RightSensorNs.HasValue && final.Plant.LeftEdgeMm > 600 && !final.Plant.Right.Detected,
                "Testdouble hat den rechten Sensor nicht vollständig überfahren");
            Assert(final.Plant.MotorVelocity == 0 && !final.Coupling.Applied.MotorRight && !final.Plant.ObjectOffBelt,
                "Testfall muss auf dem Band, aber hinter dem Endsensor zum Stillstand kommen");
            Assert(!result.Passed && !result.TransportCompleted && !result.MotorStoppedNs.HasValue && result.EndReason == "AdvanceLimit",
                "Verspäteter Halt außerhalb des rechten Sensors wurde als Endabschaltung bestätigt");
            Assert(result.CleanupConfirmed && result.ContinueCalls == 5700 && endpoint.Disposals == 1,
                "Fehlgeschlagene Abnahme muss begrenzt und vollständig bereinigt enden");
        });
    }
}
