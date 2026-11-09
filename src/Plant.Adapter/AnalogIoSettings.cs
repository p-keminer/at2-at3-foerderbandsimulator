using System;
using Plant.Core;

namespace Plant.Adapter
{
    // Electrical module ranges, not a controller or a motor-speed override.
    public sealed class AnalogIoSettings
    {
        public double InputMinimumVolts { get; set; } = -10;
        public double InputMaximumVolts { get; set; } = 10;
        public double OutputMinimumVolts { get; set; } = 0;
        public double OutputMaximumVolts { get; set; } = 10;
        public double InitialPotentiometerVolts { get; set; }
        public AnalogIoSettings Copy() => Json.Read<AnalogIoSettings>(Json.Write(this));
        public void Validate()
        {
            // AI manual C-4: symmetric voltage ranges. Motor input is documented as 0..10 V.
            Numbers.Require(InputMinimumVolts == -InputMaximumVolts &&
                (InputMaximumVolts == 10 || InputMaximumVolts == 5 || InputMaximumVolts == 2.5 || InputMaximumVolts == 1),
                "AI-Messbereich: ±10 V, ±5 V, ±2,5 V oder ±1 V; für AT-2 ±10 V verwenden.");
            Numbers.Require(OutputMinimumVolts == 0 && OutputMaximumVolts == 10,
                "Motor-Drehzahlvorgabe benötigt AQ 0…10 V.");
            RequirePotentiometer(InitialPotentiometerVolts);
        }
        public short InputRaw(double voltage)
        {
            Numbers.Require(Numbers.Finite(voltage) && voltage >= InputMinimumVolts && voltage <= InputMaximumVolts,
                "Potentiometerspannung liegt außerhalb des konfigurierten AI-Nennbereichs.");
            return Analog.InputRaw(voltage * 10 / InputMaximumVolts);
        }
        public void RequirePotentiometer(double voltage)
        {
            Numbers.Require(Numbers.Finite(voltage) && voltage >= 0 && voltage <= 10,
                "SPS-Board-Potentiometer benötigt 0…10 V.");
            InputRaw(voltage);
        }
    }

    // Coalesces slider updates, bounded memory, no vendor calls. Applied only at an input boundary.
    public sealed class PotentiometerControl
    {
        private readonly object gate = new();
        private readonly AnalogIoSettings settings;
        private short requested;
        public PotentiometerControl(AnalogIoSettings settings)
        { settings.Validate(); this.settings = settings.Copy(); requested = settings.InputRaw(settings.InitialPotentiometerVolts); }
        public void SetVoltage(double voltage)
        { settings.RequirePotentiometer(voltage); lock (gate) requested = settings.InputRaw(voltage); }
        // Explicit diagnostic API, deliberately absent from the UI/process POTI command.
        public void SetDiagnosticVoltage(double voltage)
        { short raw = settings.InputRaw(voltage); lock (gate) requested = raw; }
        public short RequestedRaw { get { lock (gate) return requested; } }
    }
}
