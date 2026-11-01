using System;
using System.Linq;
using Plant.Core;

namespace Plant.Adapter
{
    // Nine confirmed digital bits. The full analogue/telemetry contract remains unchanged.
    public sealed class DigitalCycleProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public double TestSpeedMmPerSecond { get; set; } = 100;
        public bool Ob1AutomaticImageConfirmed { get; set; }
        public bool ConfigurableMapping { get; set; }
        public string? ExpectedInstanceName { get; set; }
        public const string ProfileId = "AT2_Kopplung_9_digital_bits_SingleStep_C";
        public static System.Collections.Generic.IReadOnlyList<string> SignalIds { get; } = Array.AsReadOnly(new[] { "SensorLeftHigh", "OpticalHigh", "SensorRightHigh", "HallHigh", "LampGreen", "MotorRight", "MotorLeft", "LampYellow", "LampRed" });
        public void Validate()
        {
            Numbers.Require(SchemaVersion == 1 && Ob1AutomaticImageConfirmed, "Digitales C-Profil: Schema 1 und bestätigte OB1-E/A-Automatik erforderlich.");
            Numbers.Require(Numbers.Finite(TestSpeedMmPerSecond) && TestSpeedMmPerSecond > 0 && TestSpeedMmPerSecond <= 100,
                "Temporäre C#-Testgeschwindigkeit muss >0 und höchstens 100 mm/s sein; keine analoge SPS-Vorgabe.");
        }
        public DigitalCycleProfile Copy() => new() { SchemaVersion = SchemaVersion, TestSpeedMmPerSecond = TestSpeedMmPerSecond, Ob1AutomaticImageConfirmed = Ob1AutomaticImageConfirmed,
            ConfigurableMapping = ConfigurableMapping, ExpectedInstanceName = ExpectedInstanceName };
        public void RequireMap(SignalMap map)
        {
            Validate();
            string? instance = ConfigurableMapping ? ExpectedInstanceName : "AT2_Kopplung";
            Numbers.Require(!string.IsNullOrWhiteSpace(instance) && map != null && map.InstanceName == instance && map.LocalVirtualInstanceConfirmed,
                "CPU-Instanzname eintragen und lokale virtuelle CPU bestätigen.");
            var errors = map!.Validate(false); Numbers.Require(errors.Count == 0, string.Join("\n", errors));
            for (int i = 0; i < SignalIds.Count; i++)
            {
                string id = SignalIds[i]; var bindings = map.Signals.Where(s => s.Id == id).ToArray();
                Numbers.Require(bindings.Length == 1, "Digitales Pflichtsignal fehlt/mehrfach: " + id);
                var b = bindings[0]; int bit = i < 4 ? i : i - 4;
                Numbers.Require(b.Confirmed && b.ByteOffset.HasValue && b.BitOffset.HasValue && b.AreaSizeBytes >= 1 &&
                    (ConfigurableMapping || (b.ByteOffset == 0 && b.BitOffset == bit)),
                    "Unbestätigte oder abweichende digitale Adresse: " + id);
            }
            Numbers.Require(map.Signals.All(s => !s.ByteOffset.HasValue || SignalIds.Contains(s.Id)),
                "Digitalprofil enthält zusätzliche Bindung. Keine analogen, Marker- oder DB-Zugriffe in diesem Profil.");
        }
        public PlantConfiguration ConfigurePlant(PlantConfiguration source)
        {
            Validate(); var copy = source.Copy(); copy.SpeedAt10V = TestSpeedMmPerSecond; copy.Validate(); return copy;
        }
    }
    public sealed record EndpointInspection(string InstanceName, string OperatingState, string OperatingMode, string[] Trace);
    public interface IEndpointInspection
    {
        bool UsesDigitalCycleProfile { get; }
        EndpointInspection Inspect();
    }
}
