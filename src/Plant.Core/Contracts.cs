using System;
using System.Collections.Generic;

namespace System.Runtime.CompilerServices { public static class IsExternalInit { } }

namespace Plant.Core
{
    public enum OperationMode { OfflineManual, OfflineReference, ExternalPlc }
    public enum Exercise { AT2, AT3 }
    public enum MachineMode { Automatic, Hand }
    public enum SensorFault { None, StuckLow, StuckHigh, Delayed, Flicker }
    public enum CycleState { Ready, Transport, Measuring, AfterMeasurement, Complete, Faulted }
    public enum LinkQuality { Offline, Unavailable, Unverified, Disconnected, Connected, Stale, Faulted }

    public sealed record ActuatorFrame(bool MotorRight = false, bool MotorLeft = false,
        int SpeedCommandRaw = 0, bool LampRed = false, bool LampYellow = false,
        bool LampGreen = false, bool Valid = true, long Sequence = 0, long TimeUs = 0);

    // Only electrical samples cross the controller boundary. No true length or model counters.
    public sealed record SensorFrame(bool SensorLeftHigh, bool OpticalHigh, bool SensorRightHigh,
        bool HallHigh, short PotiRaw, bool Valid, long Sequence, long TimeUs);
    public sealed record SensorReading(bool Ideal, bool Detected, bool High);
    public sealed record HallEdge(long Sequence, long TimeUs, bool High, double MotorMm)
    { public long TimeNs { get; init; } }
    public sealed record PlantEvent(long Sequence, long TimeUs, string Kind, string Detail);
    public sealed record ControllerTelemetry(CycleState State, bool Stopped, MachineMode MachineMode,
        long ObservedRisingEdges, long TestPulseCount, long MeasurementPulses,
        double? MeasuredLengthMm, bool MeasurementActive, string Message);
    public sealed record PlantSnapshot(long TimeUs, long Sequence, double MotorMm, double BeltMm,
        double MotorVelocity, double ObjectVelocity, bool Present, double LeftEdgeMm, double TrueLengthMm,
        SensorReading Left, SensorReading Optical, SensorReading Right, SensorFrame Sensors,
        ActuatorFrame Actuators, long GeneratedRisingEdges, bool DirectionConflict, bool ObjectOffBelt,
        bool AliasingRisk, bool MotorLocked, bool ObjectBlocked, double TransportFactor);
    public sealed record SessionSnapshot(PlantSnapshot Plant, ControllerTelemetry Controller,
        OperationMode Mode, bool Paused, LinkQuality Quality, string PauseReason, string ProfileHash);
    public sealed record InputSample(long TimeUs, long Sequence, OperationMode Mode, SensorFrame Sensors,
        ActuatorFrame Actuators, ControllerTelemetry Controller);

    public static class Numbers
    {
        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static double Clamp(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));
        public static void Require(bool ok, string message) { if (!ok) throw new ArgumentException(message); }
    }

    public static class Analog
    {
        public const int Nominal = 27648;
        public static short InputRaw(double voltage)
        {
            Numbers.Require(Numbers.Finite(voltage) && voltage >= -10 && voltage <= 10,
                "Analogeingang benötigt einen endlichen Nennwert von −10 bis +10 V.");
            return (short)Math.Round(voltage / 10 * Nominal);
        }
        public static double InputVoltage(short raw) => raw / (double)Nominal * 10;
        public static int OutputRaw(double voltage)
        {
            Numbers.Require(Numbers.Finite(voltage) && voltage >= 0 && voltage <= 10,
                "Analogausgang benötigt 0 bis 10 V.");
            return (int)Math.Round(voltage / 10 * Nominal);
        }
        public static double OutputVoltage(int raw) => Numbers.Clamp(raw, 0, Nominal) / Nominal * 10;
        public static bool OutputInNominalRange(int raw) => raw >= 0 && raw <= Nominal;
    }
}
