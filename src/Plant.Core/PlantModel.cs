using System;
using System.Collections.Generic;

namespace Plant.Core
{
    internal sealed class SensorModel
    {
        private readonly SensorConfiguration config;
        private readonly int seed;
        private bool candidate, detected;
        private long candidateSince;
        public SensorFault Fault { get; set; }
        public SensorModel(SensorConfiguration config, int seed) { this.config = config; this.seed = seed; }
        public SensorReading Update(bool present, double left, double length, bool suitable, long now)
        {
            bool ideal = present && suitable && left <= config.PositionMm + config.ZoneMm / 2 + 1e-8 && left + length > config.PositionMm - config.ZoneMm / 2 + 1e-8;
            double h = detected ? config.HysteresisMm : 0;
            bool requested = present && suitable && left <= config.PositionMm + config.ZoneMm / 2 + h + 1e-8 && left + length > config.PositionMm - config.ZoneMm / 2 - h + 1e-8;
            if (requested != candidate) { candidate = requested; candidateSince = now; }
            long delay = (config.DelayUs + (Fault == SensorFault.Delayed ? 100000 : 0)) * 1000;
            if (now - candidateSince >= delay) detected = candidate;
            bool electrical = config.ActiveHigh ? detected : !detected;
            if (Fault == SensorFault.StuckHigh) electrical = true;
            if (Fault == SensorFault.StuckLow) electrical = false;
            if (Fault == SensorFault.Flicker && ((unchecked((uint)(now / 5000000) * 1664525u + (uint)seed) >> 19) & 3) == 0) electrical = !electrical;
            return new SensorReading(ideal, detected, electrical);
        }
    }

    public sealed class PlantModel
    {
        private readonly PlantConfiguration config;
        private readonly SensorModel left, optical, right;
        private double motor, belt, position, velocity;
        private double length;
        private bool present = true, metallic, reflective;
        private long timeNs, sequence, edgeSequence, rising;
        public long TimeNs => timeNs;
        private long time => timeNs / 1000;
        private SensorReading leftReading = new(false, false, false), opticalReading = new(false, false, false), rightReading = new(false, false, false);
        public bool MotorLocked { get; set; }
        public bool ObjectBlocked { get; set; }
        private double transportFactor = 1;
        public double TransportFactor { get => transportFactor; set { Numbers.Require(Numbers.Finite(value) && value >= 0 && value <= 1, "Transportfaktor benötigt [0,1]."); transportFactor = value; } }
        public short PotiRaw { get; set; }
        public event Action<HallEdge>? Edge;
        public PlantModel(PlantConfiguration configuration)
        {
            configuration.Validate(); config = configuration.Copy();
            position = config.InitialLeftMm; length = config.WorkpieceLengthMm;
            metallic = config.Metallic; reflective = config.Reflective;
            left = new(config.Sensor("left"), config.Seed);
            optical = new(config.Sensor("optical"), config.Seed + 1);
            right = new(config.Sensor("right"), config.Seed + 2);
            UpdateSensors();
        }
        public void SetFault(string id, SensorFault fault)
        {
            Numbers.Require(Enum.IsDefined(typeof(SensorFault), fault), "Unbekannter Sensorfehler.");
            (id == "left" ? left : id == "optical" ? optical : id == "right" ? right : throw new ArgumentException("Sensor unbekannt.")).Fault = fault;
            UpdateSensors();
        }
        public void Insert(double leftMm, double lengthMm, bool metal, bool reflects)
        {
            config.ValidateWorkpiece(leftMm, lengthMm);
            position = leftMm; length = lengthMm; metallic = metal; reflective = reflects; present = true; UpdateSensors();
        }
        public void Remove() { present = false; UpdateSensors(); }
        private static long HalfIndex(double value)
        {
            double rounded = Math.Round(value);
            return (long)Math.Floor(Math.Abs(value - rounded) < 1e-8 ? rounded : value);
        }
        private bool Hall => (HalfIndex((motor / config.HallPitchMm + config.HallInitialPhase) * 2) & 1) != 0;
        public SensorFrame Sensors => new(leftReading.High, opticalReading.High, rightReading.High, Hall, PotiRaw, true, sequence, time);
        public void Step(ActuatorFrame command) => StepNanoseconds(command, config.ModelStepUs * 1000);
        public void StepNanoseconds(ActuatorFrame command, long durationNs)
        {
            Numbers.Require(durationNs > 0 && durationNs <= config.ModelStepUs * 1000, "Teilmodellschritt muss positiv und höchstens ein regulärer Modellschritt sein.");
            double dt = durationNs / 1000000000.0;
            int direction = command.MotorLeft == command.MotorRight ? 0 : command.MotorRight ? 1 : -1;
            double volts = Analog.OutputVoltage(command.SpeedCommandRaw);
            double target = command.Valid && volts >= config.MinimumVoltage && !MotorLocked ? direction * config.SpeedAt10V * volts / 10 : 0;
            double oldV = velocity;
            if (MotorLocked) velocity = 0;
            else if (!config.DynamicMotor) velocity = target;
            else
            {
                // Brake through zero before reversing; piecewise constant acceleration.
                double aim = oldV * target < 0 ? 0 : target;
                double rate = Math.Abs(aim) > Math.Abs(oldV) ? config.Acceleration : config.Deceleration;
                velocity += Numbers.Clamp(aim - velocity, -rate * dt, rate * dt);
            }
            double displacement = MotorLocked ? 0 : (config.DynamicMotor ? (oldV + velocity) * .5 : velocity) * dt;
            double previous = motor; motor += displacement;
            double half = config.HallPitchMm / 2;
            long a = HalfIndex(previous / half + config.HallInitialPhase * 2), b = HalfIndex(motor / half + config.HallInitialPhase * 2);
            void Emit(long k, bool high)
            {
                double boundary = (k - config.HallInitialPhase * 2) * half;
                long stampNs = checked(timeNs + (long)Math.Round(durationNs * Numbers.Clamp((boundary - previous) / displacement, 0, 1)));
                var e = new HallEdge(++edgeSequence, stampNs / 1000, high, boundary) { TimeNs = stampNs };
                if (high) rising++;
                Edge?.Invoke(e);
            }
            if (b > a) for (long k = a + 1; k <= b; k++) Emit(k, (k & 1) != 0);
            else if (b < a) for (long k = a; k > b; k--) Emit(k, ((k - 1) & 1) != 0);
            double beltDelta = displacement * transportFactor;
            belt += beltDelta;
            if (present && !ObjectBlocked) position += beltDelta;
            timeNs = checked(timeNs + durationNs); sequence++;
            UpdateSensors();
        }
        private void UpdateSensors()
        {
            leftReading = left.Update(present, position, length, metallic, timeNs);
            opticalReading = optical.Update(present, position, length, reflective, timeNs);
            rightReading = right.Update(present, position, length, metallic, timeNs);
        }
        public PlantSnapshot Snapshot(ActuatorFrame command)
        {
            // Strict reserve: sample interval <= quarter of shortest full Hall period.
            bool alias = Math.Abs(velocity) * config.ControllerSampleUs / 1000000.0 > config.HallPitchMm / 4;
            return new(time, sequence, motor, belt, velocity, ObjectBlocked ? 0 : velocity * transportFactor,
                present, position, length, leftReading, opticalReading, rightReading, Sensors, command, rising,
                command.MotorLeft && command.MotorRight,
                present && (position < -1e-6 || position + length > config.BeltLengthMm + 1e-6), alias,
                MotorLocked, ObjectBlocked, transportFactor);
        }
    }
}
