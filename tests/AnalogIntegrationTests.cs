using System;
using System.Linq;
using System.Threading;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static SignalMap AnalogMap()
    {
        var map = DigitalMap();
        foreach (var id in new[] { "PotiRaw", "SpeedCommandRaw" })
        {
            var b = map.Signals.Single(s => s.Id == id); b.ByteOffset = id == "PotiRaw" ? 18 : 30;
            b.AreaSizeBytes = 64; b.Confirmed = true;
        }
        return map;
    }
    private static DigitalCycleProfile AnalogProfile() { var p = DigitalProfile(); p.Analog = new(); p.TestSpeedMmPerSecond = 17; return p; }
    // An explicit byte-memory PLC test double. Not a Siemens live test or shipped control logic.
    private sealed class AnalogEndpointDouble : IPlcEndpointFactory, IPlcEndpoint, IEndpointInspection
    {
        public string InstanceName => "AT2_Kopplung";
        public EndpointEvidence Evidence => EndpointEvidence.TestDouble;
        public bool UsesDigitalCycleProfile => true;
        public event Action<SyncNotification>? SyncPoint;
        public event Action<string>? Invalidated;
        public bool Right = true, Left;
        public int Opens, Advances;
        public int? OverrideRaw;
        public short Ai, Aq;
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization approval, CancellationToken c) { Opens++; return this; }
        public void BeginSingleStepInputs() => SyncPoint?.Invoke(new(0, 1000000, 0, 1));
        public PlcReadback ReadOutputs(ClockGrant grant) => new(new(Right, Left, S7Bytes.Int16(S7Bytes.Int16(Aq)),
            LampRed: true, LampYellow: true, LampGreen: true, Sequence: grant.Sequence, TimeUs: grant.TimeUs));
        public void WriteInputs(SensorFrame input) => Ai = S7Bytes.Int16(S7Bytes.Int16(input.PotiRaw));
        public void ContinueToNextSyncPoint() { Advances++; Aq = (short)(OverrideRaw ?? Ai); SyncPoint?.Invoke(new(0, 1000000, 1000000, 1)); }
        public EndpointInspection Inspect() => new(InstanceName, "Freeze", "SingleStep_C", Array.Empty<string>());
        public void Break() => Invalidated?.Invoke("test failure");
        public void Dispose() { }
    }
    private static void RunAnalogTests()
    {
        Test("AN01", "AI ±10 V, Vorzeichen und S7-Bytefolge einschließlich −27648", () =>
        {
            var a = new AnalogIoSettings(); a.Validate();
            foreach (var v in new[] { -10d, -5, 0, 5, 10 })
            {
                short expected = (short)(v * Analog.Nominal / 10);
                Assert(a.InputRaw(v) == expected, "AI-Skalierung");
                var bytes = S7Bytes.Int16(expected); Assert(S7Bytes.Int16(bytes) == expected, "Vorzeichenverlust");
            }
            Assert(S7Bytes.Int16((short)27648).SequenceEqual(new byte[] { 0x6c, 0 }), "S7-Highbyte fehlt");
            Assert(S7Bytes.Int16((short)-27648).SequenceEqual(new byte[] { 0x94, 0 }), "Negative S7-Codierung");
            Reject(() => a.InputRaw(10.01)); Reject(() => a.InputRaw(double.NaN));
            Reject(() => a.RequirePotentiometer(-1));
        });
        Test("AN02", "Analoges C-Profil benötigt beide Wörter; digitale und vollständige Validierung bleiben streng", () =>
        {
            var p = AnalogProfile(); p.RequireMap(AnalogMap());
            Reject(() => p.RequireMap(DigitalMap())); Reject(() => DigitalProfile().RequireMap(AnalogMap()));
            Reject(() => DigitalMap().RequireLiveValid());
            foreach (var id in new[] { "PotiRaw", "SpeedCommandRaw" })
            {
                var m = AnalogMap(); m.Signals.Single(s => s.Id == id).Confirmed = false; Reject(() => p.RequireMap(m));
                m = AnalogMap(); m.Signals.Single(s => s.Id == id).AreaSizeBytes = 1; Reject(() => p.RequireMap(m));
                m = AnalogMap(); m.Signals.Single(s => s.Id == id).ByteOffset = 0; Reject(() => p.RequireMap(m));
            }
            Assert(Json.Read<DigitalCycleProfile>(Json.Write(p)).UsesAnalog, "Analogprofil nicht serialisierbar");
            Equal(p.ConfigurePlant(new PlantConfiguration { SpeedAt10V = 100 }).SpeedAt10V, 100);
            Equal(DigitalProfile().ConfigurePlant(new PlantConfiguration { SpeedAt10V = 73 }).SpeedAt10V, 100);
        });
        Test("AN03", "Modulbereiche, Anfangswert und ungültige Ausgangsrohwerte", () =>
        {
            foreach (var v in new[] { -0.1, 10.1, double.NaN, double.PositiveInfinity })
                Reject(() => new AnalogIoSettings { InitialPotentiometerVolts = v }.Validate());
            Reject(() => new AnalogIoSettings { OutputMaximumVolts = 5 }.Validate());
            Reject(() => new AnalogIoSettings { InputMinimumVolts = 0 }.Validate());
            Assert(!Analog.OutputInNominalRange(-1) && !Analog.OutputInNominalRange(27649), "AQ-Fehler wurde geklemmt");
            var controls = new PotentiometerControl(new());
            using var session = new InteractiveControlSession(null, false, potentiometer: controls);
            session.Accept("POTI 5"); Assert(controls.RequestedRaw == 13824, "Bedienkanal skaliert falsch");
            Reject(() => session.Accept("POTI -5")); Reject(() => session.Accept("POTI NaN"));
            using var digital = new InteractiveControlSession(null, false); Reject(() => digital.Accept("POTI 5"));
        });
        AsyncTest("AN04", "0/5/10 V während derselben Verbindung; SPS-MOVE-Testdouble bestimmt Bewegung, Lampen und Richtung", async () =>
        {
            var endpoint = new AnalogEndpointDouble(); var profile = AnalogProfile(); var control = new PotentiometerControl(profile.Analog!);
            var plant = new ExternalPlantSimulation(profile.ConfigurePlant(new PlantConfiguration()), potentiometer: control);
            var controller = new PlcCouplingController(endpoint, plant, new CouplingOptions { DigitalCycle = profile, InputAdoptionReviewed = true, ConfirmedInputPartitions = new[] { 0u } });
            try
            {
                await controller.ConnectAsync(AnalogMap(), FakeApproval);
                foreach (double volts in new[] { 0d, 5, 10 })
                {
                    double before = plant.Snapshot.ObjectVelocity; control.SetVoltage(volts);
                    Equal(plant.Snapshot.ObjectVelocity, before); // UI input cannot move the motor directly.
                    for (int i = 0; i < 100; i++) await controller.StepAsync();
                    Assert(endpoint.Ai == Analog.InputRaw(volts) && endpoint.Aq == Analog.InputRaw(volts), "MOVE-Wortübertragung");
                    Equal(plant.Snapshot.ObjectVelocity, volts * 10);
                    Assert(controller.Snapshot.Applied.LampRed && controller.Snapshot.Applied.LampYellow && controller.Snapshot.Applied.LampGreen, "Lampenregression");
                }
                long edges = plant.Snapshot.GeneratedRisingEdges; control.SetVoltage(0);
                for (int i = 0; i < 5; i++) await controller.StepAsync();
                double stopped = plant.Snapshot.MotorMm; edges = plant.Snapshot.GeneratedRisingEdges;
                for (int i = 0; i < 50; i++) await controller.StepAsync();
                Equal(plant.Snapshot.MotorMm, stopped); Assert(plant.Snapshot.GeneratedRisingEdges == edges, "Hallimpulse bei Stillstand");
                endpoint.Right = false; endpoint.Left = true; control.SetVoltage(5);
                for (int i = 0; i < 100; i++) await controller.StepAsync();
                Equal(plant.Snapshot.ObjectVelocity, -50);
            }
            finally { await controller.DisconnectAsync(); }
            Assert(endpoint.Opens == 1, "Potentiometer benötigte Neuverbindung");
        });
        AsyncTest("AN05", "35/60-Prozent-AQ-Wörter und Fehler werden über denselben Kopplungscontroller verarbeitet", async () =>
        {
            var endpoint = new AnalogEndpointDouble(); var profile = AnalogProfile(); var plant = new ExternalPlantSimulation(new PlantConfiguration());
            var controller = new PlcCouplingController(endpoint, plant, new CouplingOptions { DigitalCycle = profile, InputAdoptionReviewed = true, ConfirmedInputPartitions = new[] { 0u } });
            try
            {
                await controller.ConnectAsync(AnalogMap(), FakeApproval);
                foreach (double volts in new[] { 3.5, 6 })
                {
                    endpoint.OverrideRaw = Analog.OutputRaw(volts);
                    for (int i = 0; i < 100; i++) await controller.StepAsync();
                    Equal(plant.Snapshot.ObjectVelocity, volts * 10, 0.002);
                }
                endpoint.OverrideRaw = -1; await controller.StepAsync();
                bool rejected = false; try { await controller.StepAsync(); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected, "Ungültiger AQ-Ausgang wurde nicht angehalten");
            }
            finally { await controller.DisconnectAsync(); }
        });
    }
}
