using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Plant.Core;

namespace Plant.Adapter
{
    // Portable user data, not a runtime connection. No Siemens references or side effects.
    public sealed class StudyProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ProjectFile { get; set; } = "";
        public PlantConfiguration Plant { get; set; } = new();
        public SignalMap Map { get; set; } = SignalMap.Unbound();
        public double DigitalSpeedMmPerSecond { get; set; } = 100;
        public AnalogIoSettings? Analog { get; set; }
        public StudyProfile Copy() => Json.Read<StudyProfile>(Json.Write(this));
        public override string ToString() => Name;
        public void Validate()
        {
            Numbers.Require(SchemaVersion == 1, "Unbekannte Profilversion.");
            Numbers.Require(Regex.IsMatch(Id ?? "", @"^[a-zA-Z0-9_-]{1,64}$"), "Ungültige Profilkennung.");
            Numbers.Require(!string.IsNullOrWhiteSpace(Name) && Name.Length <= 80, "Profilname: 1 bis 80 Zeichen.");
            Numbers.Require(Plant != null && Map != null, "Anlage oder Signalzuordnung fehlt.");
            Plant!.Validate();
            Analog?.Validate();
            var errors = Map!.Validate(false);
            Numbers.Require(errors.Count == 0, string.Join("\n", errors));
            Numbers.Require(Map.InstanceName == null || (Map.InstanceName.Length <= 128 && !Map.InstanceName.Any(char.IsControl)), "Ungültiger CPU-Instanzname.");
            Numbers.Require(Numbers.Finite(DigitalSpeedMmPerSecond) && DigitalSpeedMmPerSecond > 0 && DigitalSpeedMmPerSecond <= 100, "Digitale Modellgeschwindigkeit: > 0 bis 100 mm/s.");
        }
        public DigitalCycleProfile DigitalSettings() => new() { TestSpeedMmPerSecond = DigitalSpeedMmPerSecond,
            Ob1AutomaticImageConfirmed = true, ConfigurableMapping = true, ExpectedInstanceName = Map.InstanceName, Analog = Analog?.Copy() };
        public string[] ConnectionProblems()
        {
            try { Validate(); DigitalSettings().RequireMap(Map); return Array.Empty<string>(); }
            catch (ArgumentException e) { return new[] { e.Message }; }
            catch (InvalidOperationException e) { return new[] { e.Message }; }
        }
    }

    public static class SignalAddress
    {
        public static string Format(SignalBinding binding) => !binding.ByteOffset.HasValue ? "" :
            (binding.Area == "Input" ? "I" : binding.Area == "Output" ? "Q" : "M") +
            (binding.Type == "BOOL" ? binding.ByteOffset + "." + binding.BitOffset :
             (binding.Type == "INT" ? "W" : binding.Type == "DINT" ? "D" : "L") + binding.ByteOffset);
        public static void Apply(SignalBinding binding, string address, int areaBytes)
        {
            address = (address ?? "").Trim().TrimStart('%').ToUpperInvariant();
            if (address.Length == 0) { binding.ByteOffset = binding.BitOffset = null; binding.Confirmed = false; binding.AreaSizeBytes = areaBytes; return; }
            string prefix = binding.Area == "Input" ? "I" : binding.Area == "Output" ? "Q" : "M";
            string pattern = binding.Type == "BOOL" ? "^" + prefix + @"(\d{1,5})\.([0-7])$" : "^" + prefix +
                (binding.Type == "INT" ? "W" : binding.Type == "DINT" ? "D" : "L") + @"(\d{1,5})$";
            var match = Regex.Match(address, pattern);
            Numbers.Require(match.Success, binding.Id + ": Adresse wie " + prefix + (binding.Type == "BOOL" ? "0.0" : "W4") + " eingeben oder offen lassen.");
            binding.ByteOffset = int.Parse(match.Groups[1].Value);
            binding.BitOffset = binding.Type == "BOOL" ? int.Parse(match.Groups[2].Value) : (int?)null;
            binding.AreaSizeBytes = areaBytes; binding.Confirmed = true;
        }
    }

    public sealed class StudyProfileStore
    {
        private readonly string directory;
        public StudyProfileStore(string directory) { this.directory = Path.GetFullPath(directory); }
        public IReadOnlyList<StudyProfile> Load(IList<string> errors)
        {
            var result = new List<StudyProfile>();
            if (!Directory.Exists(directory)) return result;
            foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p))
            {
                try { var p = Read(path); result.Add(p); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
                { errors.Add(Path.GetFileName(path) + ": " + e.Message); }
            }
            return result;
        }
        public static StudyProfile Read(string path)
        {
            Numbers.Require(new FileInfo(path).Length <= 1024 * 1024, "Profildatei ist zu groß.");
            var p = Json.Read<StudyProfile>(File.ReadAllText(path)); p.Validate(); return p;
        }
        public void Save(StudyProfile profile) { profile.Validate(); Json.SaveAtomic(Path.Combine(directory, profile.Id + ".json"), Json.Write(profile)); }
    }
}
