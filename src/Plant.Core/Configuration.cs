using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Plant.Core
{
    public sealed class SensorConfiguration
    {
        public string Id { get; set; } = "";
        public double PositionMm { get; set; }
        public double ZoneMm { get; set; }
        public double HysteresisMm { get; set; }
        public long DelayUs { get; set; }
        public bool ActiveHigh { get; set; } = true;
    }

    public sealed class PlantConfiguration
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProfileId { get; set; } = "BEISPIEL – keine Laborkalibrierung";
        public double BeltLengthMm { get; set; } = 1000;
        public double InitialLeftMm { get; set; } = 50;
        public bool InitialPresent { get; set; } = true;
        public double WorkpieceLengthMm { get; set; } = 120;
        public bool Metallic { get; set; } = true;
        public bool Reflective { get; set; } = true;
        public double SpeedAt10V { get; set; } = 100;
        public bool DynamicMotor { get; set; }
        public double Acceleration { get; set; } = 200;
        public double Deceleration { get; set; } = 400;
        public double MinimumVoltage { get; set; }
        public double HallPitchMm { get; set; } = 1;
        public double HallInitialPhase { get; set; }
        public long ModelStepUs { get; set; } = 1000;
        public long ControllerSampleUs { get; set; } = 1000;
        public int Seed { get; set; } = 1632026;
        public double? CalibrationMmPerPulse { get; set; }
        public SensorConfiguration[] Sensors { get; set; } = new[]
        {
            new SensorConfiguration { Id = "left", PositionMm = 100 },
            new SensorConfiguration { Id = "optical", PositionMm = 500 },
            new SensorConfiguration { Id = "right", PositionMm = 900 }
        };
        public void Validate()
        {
            Numbers.Require(SchemaVersion == 1, "Unbekannte Konfigurationsversion.");
            Numbers.Require(!string.IsNullOrWhiteSpace(ProfileId), "Profilname fehlt.");
            Numbers.Require(Numbers.Finite(BeltLengthMm) && BeltLengthMm > 0 && BeltLengthMm <= 100000, "Bandlänge ungültig.");
            ValidateWorkpiece(InitialLeftMm, WorkpieceLengthMm);
            Numbers.Require(Numbers.Finite(SpeedAt10V) && SpeedAt10V > 0 && SpeedAt10V <= 100000, "Motorgeschwindigkeit ungültig.");
            Numbers.Require(Numbers.Finite(HallPitchMm) && HallPitchMm >= .001 && HallPitchMm <= BeltLengthMm, "Hallteilung ungültig.");
            Numbers.Require(Numbers.Finite(HallInitialPhase) && HallInitialPhase >= 0 && HallInitialPhase < 1, "Hallphase benötigt [0,1).");
            Numbers.Require(ModelStepUs >= 1 && ModelStepUs <= 1000, "Modellschritt muss zwischen 1 und 1000 µs liegen.");
            Numbers.Require(ControllerSampleUs >= ModelStepUs && ControllerSampleUs <= 1000000 && ControllerSampleUs % ModelStepUs == 0, "Abtastung muss ein ganzzahliges Vielfaches des Modellschritts sein.");
            Numbers.Require(Numbers.Finite(Acceleration) && Acceleration > 0 && Numbers.Finite(Deceleration) && Deceleration > 0, "Beschleunigung und Verzögerung müssen positiv sein.");
            Numbers.Require(Numbers.Finite(MinimumVoltage) && MinimumVoltage >= 0 && MinimumVoltage <= 10, "Anfahrspannung ungültig.");
            Numbers.Require(CalibrationMmPerPulse == null || (Numbers.Finite(CalibrationMmPerPulse.Value) && CalibrationMmPerPulse > 0), "Kalibrierkonstante ungültig.");
            Numbers.Require(Sensors != null && Sensors.Length == 3 && Sensors.All(s => s != null) && Sensors.Select(s => s.Id).OrderBy(s => s).SequenceEqual(new[] { "left", "optical", "right" }), "Genau left, optical und right erforderlich.");
            foreach (var s in Sensors!)
            {
                Numbers.Require(Numbers.Finite(s.PositionMm) && s.PositionMm >= 0 && s.PositionMm <= BeltLengthMm, "Sensorposition ungültig.");
                Numbers.Require(Numbers.Finite(s.ZoneMm) && s.ZoneMm >= 0 && s.ZoneMm <= BeltLengthMm && Numbers.Finite(s.HysteresisMm) && s.HysteresisMm >= 0 && s.HysteresisMm <= BeltLengthMm, "Sensorzone/Hysterese ungültig.");
                Numbers.Require(s.DelayUs >= 0 && s.DelayUs <= 10000000, "Sensorverzögerung ungültig.");
            }
            Numbers.Require(Sensor("left").PositionMm < Sensor("optical").PositionMm && Sensor("optical").PositionMm < Sensor("right").PositionMm, "Sensorreihenfolge muss links 2 → Optik → rechts 1 sein.");
        }
        public void ValidateWorkpiece(double left, double length) => Numbers.Require(Numbers.Finite(left) && Numbers.Finite(length) && length > 0 && length <= BeltLengthMm && left >= 0 && left + length <= BeltLengthMm, "Prüfling benötigt positive Länge und muss vollständig auf dem Band liegen.");
        public SensorConfiguration Sensor(string id) => Sensors.Single(s => s.Id == id);
        public PlantConfiguration Copy() => Json.Read<PlantConfiguration>(Json.Write(this));
        public string Hash()
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(this)))).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class Json
    {
        public static string Write(object value) => new JavaScriptSerializer().Serialize(value);
        // Full bounded transport traces exceed the serializer's 2 MiB default.
        // Configuration parsing keeps its smaller limit; only explicit reports use this budget.
        public const int MaxReportCharacters = 128 * 1024 * 1024;
        public static string WriteReport(object value) => new JavaScriptSerializer { MaxJsonLength = MaxReportCharacters }.Serialize(value);
        public static T Read<T>(string json)
        {
            var serializer = new JavaScriptSerializer();
            object root = serializer.DeserializeObject(json);
            void ValidateShape(object? value, Type expected)
            {
                if (value == null) return;
                if (expected.IsArray && value is object[] array)
                { foreach (var item in array) ValidateShape(item, expected.GetElementType()!); return; }
                if (!(value is IDictionary<string, object> fields)) return;
                var properties = expected.GetProperties().Where(p => p.CanWrite).ToArray();
                foreach (var item in fields)
                {
                    var property = properties.SingleOrDefault(p => string.Equals(p.Name, item.Key, StringComparison.OrdinalIgnoreCase));
                    Numbers.Require(property != null, "Unbekanntes JSON-Feld: " + item.Key);
                    ValidateShape(item.Value, property!.PropertyType);
                }
            }
            Numbers.Require(root != null, "JSON-Objekt fehlt.");
            ValidateShape(root, typeof(T));
            if (typeof(T).GetProperty("SchemaVersion") != null)
                Numbers.Require(root is IDictionary<string, object> f && f.Keys.Any(k => string.Equals(k, "SchemaVersion", StringComparison.OrdinalIgnoreCase)), "SchemaVersion fehlt.");
            T result = serializer.Deserialize<T>(json);
            Numbers.Require(result != null, "JSON-Objekt fehlt."); return result;
        }
        public static PlantConfiguration LoadProfile(string path)
        {
            var config = Read<PlantConfiguration>(File.ReadAllText(path));
            config.Validate(); return config;
        }
        public static void SaveAtomic(string path, string contents)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, contents, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
