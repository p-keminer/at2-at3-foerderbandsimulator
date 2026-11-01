using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Plant.Core;

namespace Plant.Wpf
{
    // Siemens-free rendering checks. Timings are observations, never machine-dependent pass thresholds.
    internal static class RenderingChecks
    {
        private static readonly ControllerTelemetry Telemetry = new(CycleState.Ready, false, MachineMode.Automatic, 0, 0, 0, null, false, "Render fixture");
        private static InputSample Sample(long time, bool high) => new(time, time + 1, OperationMode.OfflineManual,
            new SensorFrame(high, high, high, high, 0, true, time + 1, time), new ActuatorFrame(MotorRight: high), Telemetry);

        private static Point[] Points(StreamGeometry geometry)
        {
            var result = new List<Point>();
            foreach (var figure in PathGeometry.CreateFromGeometry(geometry).Figures)
            {
                result.Add(figure.StartPoint);
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line) result.Add(line.Point);
                    else if (segment is PolyLineSegment polyline) result.AddRange(polyline.Points);
                    else throw new InvalidOperationException("Digitaler Signalverlauf enthält eine Kurve.");
                }
            }
            return result.ToArray();
        }
        private static Point[] Edges(Point[] points) => points.Skip(1).Where((p, i) => Math.Abs(p.Y - points[i].Y) > .001).ToArray();
        private static bool Same(double a, double b) => Math.Abs(a - b) < .00001;
        private static object Measure(int frames, Action action)
        {
            for (int i = 0; i < 5; i++) action();
            var times = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                var clock = Stopwatch.StartNew(); action(); clock.Stop(); times[i] = clock.Elapsed.TotalMilliseconds;
            }
            Array.Sort(times);
            return new { frames, medianMs = times[times.Length / 2], p95Ms = times[(int)((times.Length - 1) * .95)], maxMs = times[times.Length - 1] };
        }

        public static async Task Run(MainWindow window, string directory, Action<bool, string> check)
        {
            var constant = Enumerable.Range(0, 1000).Select(i => Sample(i * 1000, false)).ToArray();
            var dense = Enumerable.Range(0, 1000).Select(i => Sample(i * 1000, i % 2 == 1)).ToArray();
            var typical = Enumerable.Range(0, 1000).Select(i => Sample(i * 1000, (i / 6) % 2 == 1)).ToArray();
            for (int lane = 0; lane < 5; lane++)
            {
                var flatGeometry = TraceView.BuildLaneGeometry(constant, lane, 1110);
                var flat = Points(flatGeometry);
                check(flatGeometry.IsFrozen && flat.Length == 2 && Same(flat[0].Y, flat[1].Y) && Same(flat[1].X, 1092),
                    $"Signalspur {lane}: 1000 konstante Samples als unveränderter durchgehender Pegel");
                var points = Points(TraceView.BuildLaneGeometry(dense, lane, 1110));
                var edges = Edges(points);
                check(edges.Length == 999 && edges.Select((p, i) => Same(p.X, 94 + i) && Same(p.Y, lane * 36 + 25 - (i % 2 == 0 ? 18 : 0))).All(x => x),
                    $"Signalspur {lane}: sämtliche 999 Flanken behalten Zeitposition und elektrischen Pegel");
                check(points.Skip(1).Select((p, i) => Same(p.X, points[i].X) || Same(p.Y, points[i].Y)).All(x => x),
                    $"Signalspur {lane}: keine interpolierten oder diagonalen Übergänge");
            }
            var boundary = new[] { Sample(-1, true), Sample(0, false), Sample(250000, true), Sample(500000, true), Sample(750000, false), Sample(1000000, false) };
            var boundaryPoints = Points(TraceView.BuildLaneGeometry(boundary, 0, 1110));
            var boundaryEdges = Edges(boundaryPoints);
            check(Same(boundaryPoints[0].X, 92) && Same(boundaryPoints[0].Y, 25) && Same(boundaryPoints.Last().X, 1092) &&
                boundaryEdges.Length == 2 && Same(boundaryEdges[0].X, 342) && Same(boundaryEdges[0].Y, 7) && Same(boundaryEdges[1].X, 842) && Same(boundaryEdges[1].Y, 25),
                "Zeitfenster enthält exakte Randpunkte und beide Flanken; davor liegende Samples erscheinen nicht");
            check(TraceView.BuildLaneGeometry(Array.Empty<InputSample>(), 0, 1110).IsEmpty() &&
                TraceView.BuildLaneGeometry(new[] { Sample(0, true) }, 0, 1110).IsEmpty(), "Kein erfundener Verlauf bei null oder nur einem Sample");
            check(constant.Length == 1000 && dense.Length == 1000 && boundary[0].TimeUs == -1,
                "Zeichenoptimierung verändert keine Eingangs- oder Aufzeichnungssamples");

            var pen = new Pen(Brushes.DarkSlateGray, 1.5); pen.Freeze();
            void DrawBaseline()
            {
                var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
                for (int lane = 0; lane < 5; lane++)
                    for (int i = 1; i < typical.Length; i++)
                    {
                        double y = lane * 36 + 25, before = y - (typical[i - 1].Sensors.HallHigh ? 18 : 0), after = y - (typical[i].Sensors.HallHigh ? 18 : 0);
                        dc.DrawLine(pen, new Point(93 + i - 1, before), new Point(93 + i, before));
                        dc.DrawLine(pen, new Point(93 + i, before), new Point(93 + i, after));
                    }
            }
            void DrawOptimized()
            {
                var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
                for (int lane = 0; lane < 5; lane++) dc.DrawGeometry(null, pen, TraceView.BuildLaneGeometry(typical, lane, 1110));
            }
            await Task.Yield();
            object baseline = Measure(40, DrawBaseline), optimized = Measure(40, DrawOptimized);
            var plant = new PlantView { Configuration = new PlantConfiguration(), Snapshot = new SimulationSession(new PlantConfiguration()).Snapshot() };
            plant.Measure(new Size(640, double.PositiveInfinity)); plant.Arrange(new Rect(0, 0, 640, plant.DesiredSize.Height));
            check(plant.DesiredSize.Height >= 640.0 / 1080 * 386 && plant.DesiredSize.Height < 260,
                "Kompakte Anlagenzeichnung erhält ihre vollständige Zeichenhöhe bei 640 px Breite");
            var render = typeof(PlantView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object plantCost = Measure(80, () =>
            {
                var visual = new DrawingVisual(); using var dc = visual.RenderOpen(); render.Invoke(plant, new object[] { dc });
            });
            plant.Snapshot = null;
            var previewVisual = new DrawingVisual();
            using (var dc = previewVisual.RenderOpen()) render.Invoke(plant, new object[] { dc });
            check(plant.Snapshot == null && previewVisual.Drawing.Bounds.Width > 500 && previewVisual.Drawing.Bounds.Height > 150,
                "Unverbundene Anlage zeigt statische Vorschaugeometrie; fachlicher SPS-Snapshot bleibt null");
            await Task.Yield();
            object refreshCost = Measure(30, () => { window.Refresh(); window.UpdateLayout(); });
            Directory.CreateDirectory(directory);
            Json.SaveAtomic(Path.Combine(directory, "render-performance.json"), Json.Write(new
            {
                utc = DateTime.UtcNow, environment = ".NET Framework 4.8 x64 / WPF / real DrawingContext",
                sampleCount = typical.Length, lanes = 5, baselineDrawCommands = (typical.Length - 1) * 10, optimizedDrawCommands = 5,
                baselineTraceRecording = baseline, optimizedTraceRecording = optimized, plantRender = plantCost, windowRefreshAndLayout = refreshCost,
                note = "Wall-time observations, no FPS guarantee. Baseline replays previous two DrawLine calls per pair of samples and lane; optimized uses production geometry. No Siemens runtime or interpolation."
            }));
            check(true, "Render- und Refreshkosten mit echtem WPF gemessen; keine rechnerabhängige Timing-Passgrenze");
        }
    }
}
