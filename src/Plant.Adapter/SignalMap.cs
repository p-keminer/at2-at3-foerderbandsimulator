using System;
using System.Collections.Generic;
using System.Linq;
using Plant.Core;

namespace Plant.Adapter
{
    public sealed record SignalDefinition(string Id, string Type, string Direction, string Area, bool Required);
    public static class SignalContract
    {
        public static IReadOnlyList<SignalDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new SignalDefinition("SensorLeftHigh", "BOOL", "plant_to_plc", "Input", true),
            new SignalDefinition("OpticalHigh", "BOOL", "plant_to_plc", "Input", true),
            new SignalDefinition("SensorRightHigh", "BOOL", "plant_to_plc", "Input", true),
            new SignalDefinition("HallHigh", "BOOL", "plant_to_plc", "Input", true),
            new SignalDefinition("PotiRaw", "INT", "plant_to_plc", "Input", false),
            new SignalDefinition("MotorRight", "BOOL", "plc_to_plant", "Output", true),
            new SignalDefinition("MotorLeft", "BOOL", "plc_to_plant", "Output", true),
            new SignalDefinition("SpeedCommandRaw", "INT", "plc_to_plant", "Output", true),
            new SignalDefinition("LampRed", "BOOL", "plc_to_plant", "Output", true),
            new SignalDefinition("LampYellow", "BOOL", "plc_to_plant", "Output", true),
            new SignalDefinition("LampGreen", "BOOL", "plc_to_plant", "Output", true),
            new SignalDefinition("MeasuredLengthMm", "LREAL", "plc_to_diagnostics", "Marker", false),
            new SignalDefinition("ControllerPulseCount", "DINT", "plc_to_diagnostics", "Marker", false),
            new SignalDefinition("MeasurementActive", "BOOL", "plc_to_diagnostics", "Marker", false)
        });
    }
    public sealed class SignalBinding
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Area { get; set; } = "";
        public int? ByteOffset { get; set; }
        public int? BitOffset { get; set; }
        public int AreaSizeBytes { get; set; }
        public string? Symbol { get; set; }
        public bool Confirmed { get; set; }
    }
    public sealed class SignalMap
    {
        public int SchemaVersion { get; set; } = 1;
        public string? InstanceName { get; set; }
        public bool LocalVirtualInstanceConfirmed { get; set; }
        public SignalBinding[] Signals { get; set; } = Array.Empty<SignalBinding>();
        public static SignalMap Unbound() => new() { Signals = SignalContract.All.Select(d => new SignalBinding { Id = d.Id, Type = d.Type, Direction = d.Direction, Area = d.Area }).ToArray() };
        public IReadOnlyList<string> Validate(bool requireBound)
        {
            var errors = new List<string>();
            if (SchemaVersion != 1) errors.Add("Signalmap: SchemaVersion muss 1 sein.");
            if (requireBound && (string.IsNullOrWhiteSpace(InstanceName) || !LocalVirtualInstanceConfirmed)) errors.Add("Bewusst gewählte lokale virtuelle Instanz fehlt.");
            if (Signals == null) { errors.Add("Signalliste fehlt."); return errors; }
            if (Signals.Any(s => s == null)) { errors.Add("Signalliste enthält leeren Eintrag."); return errors; }
            var occupied = new List<(string Area, long First, long Last, string Id)>();
            foreach (var d in SignalContract.All)
            {
                var matches = Signals.Where(s => s.Id == d.Id).ToArray();
                if (matches.Length != 1) { if (d.Required || matches.Length > 1) errors.Add(d.Id + ": fehlt oder mehrfach vorhanden."); continue; }
                var s = matches[0];
                if (s.Type != d.Type || s.Direction != d.Direction || s.Area != d.Area) errors.Add(s.Id + ": Typ, Eigentümer oder Bereich falsch.");
                if (!string.IsNullOrWhiteSpace(s.Symbol)) errors.Add(s.Id + ": symbolische Bindung zuerst nachgewiesen in eindeutige Adresse auflösen; hier nicht verfügbar.");
                if (!s.ByteOffset.HasValue)
                { if (requireBound && d.Required) errors.Add(s.Id + ": Pflichtsignal ungebunden."); continue; }
                if (!s.Confirmed || s.ByteOffset < 0 || s.AreaSizeBytes <= 0 || s.AreaSizeBytes > 65536) errors.Add(s.Id + ": Adresse/Bereich nicht bestätigt oder ungültig.");
                int width = d.Type == "BOOL" ? 1 : d.Type == "INT" ? 16 : d.Type == "DINT" ? 32 : 64;
                if (width == 1 ? !s.BitOffset.HasValue || s.BitOffset < 0 || s.BitOffset > 7 : s.BitOffset.HasValue) errors.Add(s.Id + ": Bitposition ungültig.");
                long first = (long)s.ByteOffset.Value * 8 + (width == 1 ? s.BitOffset ?? 0 : 0);
                long last = first + width - 1;
                if (last >= (long)s.AreaSizeBytes * 8) errors.Add(s.Id + ": Adressbereich überschritten.");
                foreach (var old in occupied)
                    if (old.Area == s.Area && first <= old.Last && last >= old.First) errors.Add(s.Id + ": überlappt " + old.Id);
                occupied.Add((s.Area, first, last, s.Id));
            }
            foreach (var s in Signals) if (!SignalContract.All.Any(d => d.Id == s.Id)) errors.Add("Nicht erlaubtes Signal: " + s.Id);
            return errors.AsReadOnly();
        }
        public void RequireLiveValid()
        { var errors = Validate(true); Numbers.Require(errors.Count == 0, string.Join("\n", errors)); }
    }
}
