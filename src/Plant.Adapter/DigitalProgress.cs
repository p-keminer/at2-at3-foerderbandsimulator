using Plant.Core;

namespace Plant.Adapter
{
    // Small, versioned process boundary for WPF. No Siemens object/SDK load in the UI.
    public sealed class DigitalProgress
    {
        public int SchemaVersion { get; set; } = 1;
        public long TimeNs { get; set; }
        public long Sequence { get; set; }
        public double Position { get; set; }
        public double Length { get; set; }
        public double MotorMm { get; set; }
        public double BeltMm { get; set; }
        public double MotorVelocity { get; set; }
        public double ObjectVelocity { get; set; }
        public bool Present { get; set; }
        public bool LeftIdeal { get; set; }
        public bool OpticalIdeal { get; set; }
        public bool RightIdeal { get; set; }
        public bool LeftDetected { get; set; }
        public bool OpticalDetected { get; set; }
        public bool RightDetected { get; set; }
        public bool Left { get; set; }
        public bool Optical { get; set; }
        public bool Right { get; set; }
        public bool Hall { get; set; }
        public bool MotorRight { get; set; }
        public bool MotorLeft { get; set; }
        public bool LampGreen { get; set; }
        public long GeneratedRisingEdges { get; set; }
        public static DigitalProgress From(DigitalSample sample) => new()
        {
            TimeNs = sample.Coupling.VirtualTimeNs, Sequence = sample.Coupling.Sequence,
            Position = sample.Plant.LeftEdgeMm, Length = sample.Plant.TrueLengthMm, MotorMm = sample.Plant.MotorMm, BeltMm = sample.Plant.BeltMm,
            MotorVelocity = sample.Plant.MotorVelocity, ObjectVelocity = sample.Plant.ObjectVelocity,
            Present = sample.Plant.Present,
            LeftIdeal = sample.Plant.Left.Ideal, OpticalIdeal = sample.Plant.Optical.Ideal, RightIdeal = sample.Plant.Right.Ideal,
            LeftDetected = sample.Plant.Left.Detected, OpticalDetected = sample.Plant.Optical.Detected, RightDetected = sample.Plant.Right.Detected,
            Left = sample.Published.SensorLeftHigh, Optical = sample.Published.OpticalHigh, Right = sample.Published.SensorRightHigh, Hall = sample.Published.HallHigh,
            MotorRight = sample.Coupling.Applied.MotorRight, MotorLeft = sample.Coupling.Applied.MotorLeft, LampGreen = sample.Coupling.Applied.LampGreen,
            GeneratedRisingEdges = sample.Plant.GeneratedRisingEdges
        };
    }
}
