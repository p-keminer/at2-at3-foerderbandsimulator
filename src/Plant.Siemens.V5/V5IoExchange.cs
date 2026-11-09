using System;
using System.Linq;
using Plant.Adapter;
using Plant.Core;
using Siemens.Simatic.Simulation.Runtime;

namespace Plant.Siemens.V5
{
    // OPTIONAL, compile-checked against the installed V5.0 DLL. Never referenced by WPF.
    // No runtime initialization, instance discovery/creation, CPU state changes or license calls.
    // The prepared connection owner supplies an existing instance and mechanically checked time grants.
    public sealed class V5IoExchange : IPlantIoAdapter
    {
        private readonly IInstance instance;
        private readonly SignalMap map;
        private readonly DigitalCycleProfile? digital;
        private EOperatingMode RequiredMode => digital == null ? EOperatingMode.SingleStep_P : EOperatingMode.SingleStep_C;
        private bool disposed;
        private long grantTime = -1, grantSequence = -1;
        private bool grantAvailable;
        public LinkQuality Quality { get; private set; } = LinkQuality.Unverified;
        public string Status { get; private set; } = "E/A-Schicht vorbereitet; Siemens-Laufzeitverhalten ungeprüft.";
        public V5IoExchange(IInstance instance, SignalMap map, bool liveInitializationExplicitlyAuthorized, DigitalCycleProfile? digital = null)
        {
            if (!liveInitializationExplicitlyAuthorized) throw new InvalidOperationException("Livefreigabe fehlt.");
            if (digital == null) map.RequireLiveValid(); else digital.RequireMap(map);
            this.digital = digital?.Copy();
            this.map = Json.Read<SignalMap>(Json.Write(map)); // Freeze caller's validated mapping.
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            // No SDK method is invoked by the constructor.
        }
        public void BeginFrozenExchange(ClockGrant grant)
        {
            if (disposed) throw new ObjectDisposedException(nameof(V5IoExchange));
            Numbers.Require(instance.OperatingMode == RequiredMode && instance.OperatingState == EOperatingState.Freeze,
                "Austausch erfordert Freeze im gewählten Profil " + RequiredMode);
            Numbers.Require(grant.TimeUs >= 0 && grant.TimeUs >= grantTime && grant.Sequence > grantSequence && !grantAvailable,
                "Zeitfolge ungültig oder alter Grant noch nicht verbraucht.");
            grantTime = grant.TimeUs; grantSequence = grant.Sequence; grantAvailable = true;
            // Deliberately no Connected status: this library cannot attest a live system test.
        }
        private SignalBinding Binding(string id) => map.Signals.Single(s => s.Id == id);
        private bool Bound(string id) => map.Signals.Any(s => s.Id == id && s.ByteOffset.HasValue);
        private void RequireGrant()
        {
            if (disposed) throw new ObjectDisposedException(nameof(V5IoExchange));
            if (!grantAvailable || Quality == LinkQuality.Faulted || Quality == LinkQuality.Disconnected)
                throw new InvalidOperationException("Kein gültiger, extern nachgewiesener Synchronisationspunkt.");
            Numbers.Require(instance.OperatingMode == RequiredMode && instance.OperatingState == EOperatingState.Freeze,
                "Freeze wurde während des E/A-Austauschs aufgehoben.");
            Numbers.Require(instance.Name == map.InstanceName, "Instanzname stimmt nicht mit bestätigter Zuordnung überein.");
            foreach (var s in map.Signals.Where(b => b.ByteOffset.HasValue))
            {
                uint actualSize = s.Area == "Input" ? instance.InputArea.AreaSize : s.Area == "Output" ? instance.OutputArea.AreaSize : instance.MarkerArea.AreaSize;
                Numbers.Require(s.AreaSizeBytes <= actualSize, s.Id + ": bestätigter Bereich ist größer als die tatsächliche SDK-Area.");
            }
        }
        private void WriteInputBit(string id, bool value)
        {
            var s = Binding(id);
            Numbers.Require(s.Area == "Input" && s.Direction == "plant_to_plc" && s.Type == "BOOL", "Nur Sensoreingänge schreiben.");
            instance.InputArea.WriteBit((uint)s.ByteOffset!.Value, (byte)s.BitOffset!.Value, value);
        }
        private bool ReadOutputBit(string id)
        {
            var s = Binding(id); return instance.OutputArea.ReadBit((uint)s.ByteOffset!.Value, (byte)s.BitOffset!.Value);
        }
        public void PublishSensors(SensorFrame frame)
        {
            RequireGrant(); Numbers.Require(frame.Valid && frame.TimeUs == grantTime && frame.Sequence == grantSequence, "Eingangszeit/Sequenz stimmen nicht mit der Freigabe überein.");
            try
            {
                // Individual bit writes preserve unrelated bits. This is NOT an atomic SDK transaction.
                // Future sync owner must keep CPU input adoption frozen across ALL these calls.
                WriteInputBit("SensorLeftHigh", frame.SensorLeftHigh);
                WriteInputBit("OpticalHigh", frame.OpticalHigh);
                WriteInputBit("SensorRightHigh", frame.SensorRightHigh);
                WriteInputBit("HallHigh", frame.HallHigh);
                if ((digital == null || digital.UsesAnalog) && Bound("PotiRaw"))
                {
                    Numbers.Require(frame.PotiRaw >= -Analog.Nominal && frame.PotiRaw <= Analog.Nominal, "AI-Potentiometerrohwert außerhalb des Nennbereichs.");
                    var s = Binding("PotiRaw");
                    uint written = instance.InputArea.WriteBytes((uint)s.ByteOffset!.Value, S7Bytes.Int16(frame.PotiRaw));
                    if (written != 2) { Quality = LinkQuality.Faulted; grantAvailable = false; throw new InvalidOperationException("Potentiometer nicht vollständig geschrieben."); }
                }
                grantAvailable = false;
            }
            catch (SimulationRuntimeException e) { Fail(e); throw; }
        }
        public ActuatorFrame ReadActuators()
        {
            RequireGrant();
            try
            {
                if (digital != null && !digital.UsesAnalog)
                {
                    // Nominal is solely the model's internal scaling. It is NOT a PLC analogue read.
                    return new(ReadOutputBit("MotorRight"), ReadOutputBit("MotorLeft"), Analog.Nominal,
                        LampRed: ReadOutputBit("LampRed"), LampYellow: ReadOutputBit("LampYellow"),
                        LampGreen: ReadOutputBit("LampGreen"), Sequence: grantSequence, TimeUs: grantTime);
                }
                var speed = Binding("SpeedCommandRaw");
                int raw = S7Bytes.Int16(instance.OutputArea.ReadBytes((uint)speed.ByteOffset!.Value, 2));
                Numbers.Require(Analog.OutputInNominalRange(raw), "CPU-Drehzahlvorgabe außerhalb des freigegebenen Nennbereichs.");
                return new(ReadOutputBit("MotorRight"), ReadOutputBit("MotorLeft"), raw,
                    ReadOutputBit("LampRed"), ReadOutputBit("LampYellow"), ReadOutputBit("LampGreen"), true, grantSequence, grantTime);
            }
            catch (SimulationRuntimeException e) { Fail(e); throw; }
        }
        public ControllerReadback ReadTelemetry()
        {
            RequireGrant();
            if (digital != null) return new(null, null, null, grantTime, grantSequence); // No marker/DB access.
            try
            {
                byte[] Bytes(string id, uint length) => instance.MarkerArea.ReadBytes((uint)Binding(id).ByteOffset!.Value, length);
                double? measured = Bound("MeasuredLengthMm") ? S7Bytes.Double(Bytes("MeasuredLengthMm", 8)) : null;
                int? pulses = Bound("ControllerPulseCount") ? S7Bytes.Int32(Bytes("ControllerPulseCount", 4)) : null;
                bool? active = null;
                if (Bound("MeasurementActive"))
                { var b = Binding("MeasurementActive"); active = instance.MarkerArea.ReadBit((uint)b.ByteOffset!.Value, (byte)b.BitOffset!.Value); }
                return new(measured, pulses, active, grantTime, grantSequence);
            }
            catch (SimulationRuntimeException e) { Fail(e); throw; }
        }
        private void Fail(SimulationRuntimeException error)
        {
            Quality = LinkQuality.Faulted; grantAvailable = false;
            Status = "Siemens-E/A-Fehler; CPU-Zustand unbekannt. Originalcode=" + error.RuntimeErrorCode + "; " + error;
        }
        public void Dispose()
        {
            disposed = true; grantAvailable = false; Quality = LinkQuality.Disconnected;
            // Instance ownership belongs to the future approved connection owner. No CPU action here.
        }
    }
    public sealed record ControllerReadback(double? MeasuredLengthMm, int? PulseCount, bool? MeasurementActive, long TimeUs, long Sequence);
}
