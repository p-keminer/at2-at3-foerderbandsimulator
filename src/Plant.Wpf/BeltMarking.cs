using Plant.Core;

namespace Plant.Wpf
{
    // Visual reference only. Never resets travel, Hall phase, PLC inputs or counters.
    internal sealed class BeltMarking
    {
        private double originMm;
        internal void Reset() => originMm = 0;
        internal void SetZero(double confirmedBeltMm)
        { Numbers.Require(Numbers.Finite(confirmedBeltMm), "Kein gültiger Bandweg für die Markierung."); originMm = confirmedBeltMm; }
        internal double PositionMm(double beltMm) => beltMm - originMm;
        internal bool IsOnScale(double beltMm, double lengthMm)
        { double position = PositionMm(beltMm); return Numbers.Finite(position) && position >= 0 && position <= lengthMm; }
    }
}
