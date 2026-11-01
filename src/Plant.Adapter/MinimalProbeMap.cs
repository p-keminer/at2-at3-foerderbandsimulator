using System;
using System.Linq;
using Plant.Core;

namespace Plant.Adapter
{
    public sealed record MinimalBitBinding(uint ByteOffset, byte BitOffset, uint ConfirmedAreaSizeBytes);

    // A separate, deliberately narrow contract. NEVER accepted by the full simulator.
    public sealed class MinimalProbeMap
    {
        public string InstanceName { get; }
        public MinimalBitBinding SensorLeftHigh { get; }
        public MinimalBitBinding LampGreen { get; }
        private MinimalProbeMap(string name, MinimalBitBinding input, MinimalBitBinding output)
        { InstanceName = name; SensorLeftHigh = input; LampGreen = output; }

        public static MinimalProbeMap FromConfirmedSignals(SignalMap source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Numbers.Require(source.SchemaVersion == 1 && source.LocalVirtualInstanceConfirmed && !string.IsNullOrWhiteSpace(source.InstanceName),
                "Minimaltest: bestätigte lokale Instanz und SchemaVersion 1 erforderlich.");
            Numbers.Require(source.Signals != null && source.Signals.All(s => s != null), "Minimaltest: Signalliste fehlt oder enthält null.");
            MinimalBitBinding Select(string id, string direction, string area)
            {
                var matches = source.Signals!.Where(s => s.Id == id).ToArray();
                Numbers.Require(matches.Length == 1, "Minimaltest: " + id + " fehlt oder ist mehrfach vorhanden.");
                var s = matches[0];
                Numbers.Require(s.Type == "BOOL" && s.Direction == direction && s.Area == area && string.IsNullOrWhiteSpace(s.Symbol),
                    "Minimaltest: falscher Typ/Eigentümer/Bereich oder unaufgelöstes Symbol bei " + id);
                Numbers.Require(s.Confirmed && s.ByteOffset == 0 && s.BitOffset == 0 && s.AreaSizeBytes >= 1 && s.AreaSizeBytes <= 65536,
                    "Minimaltest: ausschließlich bestätigtes I0.0 / Q0.0 mit gültiger Bereichsgröße erlaubt: " + id);
                return new(0, 0, (uint)s.AreaSizeBytes);
            }
            return new(source.InstanceName!, Select("SensorLeftHigh", "plant_to_plc", "Input"), Select("LampGreen", "plc_to_plant", "Output"));
        }
    }
}
