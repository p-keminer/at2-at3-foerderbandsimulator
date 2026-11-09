using System;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static void RunBeltMarkingTests()
    {
        Test("BM01", "Bandmarkierung: drei reale Modellfahrten, Rückwärtsfahrt und Nullsetzen ohne Eingriff in Hall oder Sensoren", () =>
        {
            var model = new PlantModel(new PlantConfiguration { InitialPresent = false, SpeedAt10V = 100, HallPitchMm = 1.2 });
            var marking = new BeltMarking(); var stopped = new ActuatorFrame();
            for (int run = 0; run < 3; run++)
            {
                var before = model.Snapshot(stopped); marking.SetZero(before.BeltMm);
                Equal(marking.PositionMm(before.BeltMm), 0);
                Assert(model.Snapshot(stopped) == before, "Nullsetzen hat das Modell oder Hall-Signal verändert");
                Move(model, 1000, Drive(volts: 10));
                var after = model.Snapshot(stopped);
                Equal(marking.PositionMm(after.BeltMm), 100, 1e-6);
                Assert(after.GeneratedRisingEdges > before.GeneratedRisingEdges, "Keine Hallimpulse aus der tatsächlichen Bewegung");
            }
            Move(model, 250, Drive(right: false, volts: 10));
            Equal(marking.PositionMm(model.Snapshot(stopped).BeltMm), 75, 1e-6);
            marking.SetZero(100);
            Assert(!marking.IsOnScale(99, 1000) && !marking.IsOnScale(1101, 1000), "Markierung springt nach Verlassen der Skala zurück");
            Equal(marking.PositionMm(1101), 1001);
            Assert(marking.IsOnScale(100, 1000) && marking.IsOnScale(1100, 1000), "Skalenendpunkte fehlen");
            Reject(() => marking.SetZero(double.NaN)); marking.Reset(); Equal(marking.PositionMm(50), 50);
        });
    }
}
