using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Plant.Core;

namespace Plant.Wpf
{
    internal static class BeltMarkingSmokeChecks
    {
        private static IEnumerable<Drawing> Drawings(Drawing drawing)
        { yield return drawing; if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var part in Drawings(child)) yield return part; }
        internal static async Task Run(MainWindow window, string directory, Action<bool, string> check)
        {
            var view = StudyProfileSmokeChecks.All(window).OfType<PlantView>().Single();
            var previous = view.Snapshot; var config = view.Configuration;
            var fixture = new SimulationSession(new PlantConfiguration { InitialPresent = false }).Snapshot();
            view.Marking.SetZero(275);
            try
            {
                foreach (double position in new[] { 0d, 100 })
                {
                    view.Configuration = new PlantConfiguration { InitialPresent = false };
                    view.Snapshot = fixture with { Plant = fixture.Plant with { BeltMm = 275 + position, MotorMm = 500 + position } };
                    view.InvalidateVisual(); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render);
                    var drawing = VisualTreeHelper.GetDrawing(view);
                    var ticks = Drawings(drawing).OfType<GeometryDrawing>().Where(d => d.Geometry is LineGeometry line && line.StartPoint.Y == 235).ToArray();
                    check(ticks.Length == 101, "Maßstab zeichnet 101 Teilstriche im Abstand von 10 mm auf 1000 mm");
                    check(view.Marking.PositionMm(view.Snapshot.Plant.BeltMm) == position,
                        "Markierung liegt nach Nullsetzen bei " + position + " mm und folgt dem Band statt dem Motor");
                    StudyProfileSmokeChecks.Save(window, Path.Combine(directory, "bandmarkierung-" + position + "mm.png"));
                }
            }
            finally { view.Configuration = config; view.Snapshot = previous; view.Marking.Reset(); view.InvalidateVisual(); }
        }
    }
}
