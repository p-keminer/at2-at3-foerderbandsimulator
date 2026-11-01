using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Plant.Core
{
    public sealed class BoundedLog<T>
    {
        private readonly T[] buffer;
        private int first, count;
        public int Capacity { get; }
        public long Dropped { get; private set; }
        public BoundedLog(int capacity) { Numbers.Require(capacity > 0, "Puffergröße muss positiv sein."); Capacity = capacity; buffer = new T[capacity]; }
        public void Add(T value)
        {
            if (count == Capacity) { buffer[first] = value; first = (first + 1) % Capacity; Dropped++; }
            else { buffer[(first + count) % Capacity] = value; count++; }
        }
        public T[] Copy() => Tail(count);
        public T[] Tail(int maximum)
        {
            int n = Math.Min(count, maximum); var result = new T[n];
            for (int i = 0; i < n; i++) result[i] = buffer[(first + count - n + i) % Capacity];
            return result;
        }
    }

    // Single owner: SessionHost serializes all calls. Standalone deterministic tests use it directly.
    public sealed class SimulationSession
    {
        private readonly PlantConfiguration configuration;
        public PlantConfiguration Configuration => configuration.Copy();
        public long ModelStepUs => configuration.ModelStepUs;
        public PlantModel Plant { get; }
        public ReferenceController Controller { get; }
        public SampledHallCounter Counter { get; }
        public CalibrationBook Calibration { get; } = new();
        public OperationMode Mode { get; private set; } = OperationMode.OfflineManual;
        public bool Paused { get; private set; } = true;
        public string PauseReason { get; private set; } = "Simulator angehalten";
        public LinkQuality Quality { get; private set; } = LinkQuality.Offline;
        public ActuatorFrame Manual { get; private set; } = new();
        public ActuatorFrame Output { get; private set; } = new();
        public BoundedLog<InputSample> Samples { get; } = new(120000);
        public BoundedLog<HallEdge> Edges { get; } = new(120000);
        public BoundedLog<PlantEvent> Events { get; } = new(2000);
        private long eventSequence;
        public string ProfileHash { get; }
        public SimulationSession(PlantConfiguration configuration)
        {
            configuration.Validate(); this.configuration = configuration.Copy(); ProfileHash = this.configuration.Hash();
            Plant = new(this.configuration);
            Controller = new(this.configuration.Sensor("left").ActiveHigh, this.configuration.Sensor("optical").ActiveHigh,
                this.configuration.Sensor("right").ActiveHigh, Exercise.AT2) { Calibration = this.configuration.CalibrationMmPerPulse };
            Controller.InitializeLevel(Plant.Sensors);
            Counter = new(Plant.Sensors.HallHigh);
            Plant.Edge += Edges.Add;
            Log("Reset", "Vollständiger Anlagen-/Controllerreset; Siemensfrei.");
        }
        public void Log(string kind, string detail) => Events.Add(new(++eventSequence, Plant.Sensors.TimeUs, kind, detail));
        public void Pause(string reason = "Simulatorpause") { Paused = true; PauseReason = reason; Log("Pause", reason); }
        public void Resume()
        {
            Numbers.Require(Mode != OperationMode.ExternalPlc && Quality == LinkQuality.Offline, "Externe Kopplung ist gesperrt; keine automatische Wiederaufnahme.");
            Paused = false; PauseReason = ""; Log("Resume", "Virtuelle Zeit läuft weiter.");
        }
        public void SetMode(OperationMode mode)
        {
            Numbers.Require(Paused, "Betriebsquelle nur bei Simulatorpause wechseln.");
            Mode = mode; Manual = Output = new();
            Quality = mode == OperationMode.ExternalPlc ? LinkQuality.Unavailable : LinkQuality.Offline;
            PauseReason = mode == OperationMode.ExternalPlc ? "ExternalPlc: Liveinitialisierung gesperrt; keine Verbindung." : "Modus gewählt; Simulator starten.";
            Controller.JogLeft = Controller.JogRight = false;
            Controller.ResetMeasurement();
            Log("Mode", mode.ToString());
        }
        public void SetManual(ActuatorFrame command)
        {
            Numbers.Require(Mode == OperationMode.OfflineManual, "Direkte Aktoren nur in OfflineManual.");
            Numbers.Require(Analog.OutputInNominalRange(command.SpeedCommandRaw), "Motorrohwert außerhalb 0…27648.");
            Manual = command with { TimeUs = Plant.Sensors.TimeUs, Sequence = Manual.Sequence + 1 };
            Log("ManualCommand", Json.Write(Manual));
        }
        public void Insert(double left, double length, bool metal, bool reflective)
        { Plant.Insert(left, length, metal, reflective); Log("WorkpieceInserted", FormattableString.Invariant($"x={left}; L={length}; Metall={metal}; Reflexion={reflective}")); }
        public void Remove() { Plant.Remove(); Log("WorkpieceRemoved", "Prüfling entfernt."); }
        public void SetSensorFault(string id, SensorFault fault) { Plant.SetFault(id, fault); Log("SensorFault", id + ": " + fault); }
        public void NotifyDisconnect()
        {
            Quality = LinkQuality.Stale; Output = Output with { Valid = false };
            Pause("Verbindung verloren: Werte veraltet. Wiederaufnahme erfordert Modus-/Zuordnungsprüfung.");
        }
        public SessionSnapshot Snapshot() => new(Plant.Snapshot(Output), Controller.Telemetry, Mode, Paused, Quality, PauseReason, ProfileHash);
        public void Step(bool single = false)
        {
            if (Paused && !single) return;
            Numbers.Require(Mode != OperationMode.ExternalPlc && Quality == LinkQuality.Offline, "Keine freigegebene externe Zeitquelle.");
            var input = Plant.Sensors;
            if (input.TimeUs % configuration.ControllerSampleUs == 0)
            {
                Counter.Sample(input, Output);
                Output = Mode == OperationMode.OfflineReference ? Controller.Tick(input) : Manual;
                Samples.Add(new(input.TimeUs, input.Sequence, Mode, input, Output, Controller.Telemetry));
            }
            Plant.Step(Output);
            var snapshot = Plant.Snapshot(Output);
            if (snapshot.ObjectOffBelt) Pause("ObjectOffBelt: Prüfling hat die Bandgrenze überschritten.");
        }
        public void Advance(long durationUs)
        {
            Numbers.Require(durationUs >= 0 && durationUs % configuration.ModelStepUs == 0, "Zeit muss ein Vielfaches des Modellschritts sein.");
            for (long t = 0; t < durationUs && !Paused; t += configuration.ModelStepUs) Step();
        }
        public ExportData CaptureExport() => new(Samples.Copy(), Edges.Copy(), Events.Copy(), ProfileHash,
            configuration.Seed, Samples.Dropped, Edges.Dropped, Events.Dropped);
    }

    public sealed record ExportData(InputSample[] Samples, HallEdge[] Edges, PlantEvent[] Events,
        string ProfileHash, int Seed, long DroppedSamples, long DroppedEdges, long DroppedEvents)
    {
        public void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            var csv = new StringBuilder("time_us,sequence,mode,left2,optical,right1,hall,poti_raw,motor_right,motor_left,speed_raw,red,yellow,green,observed_rising,measurement_count,length_mm,valid\n");
            string B(bool x) => x ? "1" : "0";
            foreach (var s in Samples)
                csv.AppendLine(string.Join(",", s.TimeUs, s.Sequence, s.Mode, B(s.Sensors.SensorLeftHigh), B(s.Sensors.OpticalHigh), B(s.Sensors.SensorRightHigh), B(s.Sensors.HallHigh), s.Sensors.PotiRaw,
                    B(s.Actuators.MotorRight), B(s.Actuators.MotorLeft), s.Actuators.SpeedCommandRaw, B(s.Actuators.LampRed), B(s.Actuators.LampYellow), B(s.Actuators.LampGreen),
                    s.Controller.ObservedRisingEdges, s.Controller.MeasurementPulses, s.Controller.MeasuredLengthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "", B(s.Sensors.Valid && s.Actuators.Valid)));
            Json.SaveAtomic(Path.Combine(directory, "input-samples.csv"), csv.ToString());
            csv.Clear().AppendLine("sequence,time_us,hall_high,motor_mm");
            foreach (var e in Edges) csv.AppendLine(FormattableString.Invariant($"{e.Sequence},{e.TimeUs},{B(e.High)},{e.MotorMm:R}"));
            Json.SaveAtomic(Path.Combine(directory, "hall-edges.csv"), csv.ToString());
            Json.SaveAtomic(Path.Combine(directory, "events.json"), Json.Write(Events));
            Json.SaveAtomic(Path.Combine(directory, "metadata.json"), Json.Write(new { schemaVersion = 1, version = "1.0.0", ProfileHash, Seed, DroppedSamples, DroppedEdges, DroppedEvents, generatedAtUtc = DateTime.UtcNow, note = "Ringpufferexport: Verlorene ältere Einträge sind ausdrücklich gezählt. Hallereignisse sind keine CPU-Abtastungen." }));
        }
    }
}
