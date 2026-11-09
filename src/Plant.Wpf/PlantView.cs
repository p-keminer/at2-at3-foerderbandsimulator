using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed class PlantView : FrameworkElement
    {
        private static readonly Typeface LabelTypeface = new("Segoe UI");
        private static readonly Brush Frame = Ui.Brush("#BAC8D2"), Roller = Ui.Brush("#667F8B"),
            RollerBorder = Ui.Brush("#A8BBC5"), BeltMark = Ui.Brush("#526975"), Workpiece = Ui.Brush("#E6AB4F"),
            SensorLow = Ui.Brush("#617483"), SensorIdle = Ui.Brush("#8799A3"), LampOff = Ui.Brush("#D9E0E5");
        private static readonly Brush[] LampColors = { Ui.Brush("#B92C37"), Ui.Brush("#D9A300"), Ui.Brush("#17814C") };
        private static readonly string[] SensorIds = { "left", "optical", "right" },
            SensorLabels = { "Induktiv 2 · links", "Optischer Sensor", "Induktiv 1 · rechts" }, LampNames = { "Rot", "Gelb", "Grün" };
        private static Pen FrozenPen(Brush brush, double width)
        { var pen = new Pen(brush, width); pen.Freeze(); return pen; }
        private static readonly Pen RollerOutline = FrozenPen(RollerBorder, 3), RollerAxis = FrozenPen(Ui.Surface, 2),
            BeltStroke = FrozenPen(BeltMark, 2), WorkpieceOutline = FrozenPen(Ui.Warning, 2),
            SensorActiveLine = FrozenPen(Ui.Accent, 2), SensorIdleLine = FrozenPen(SensorIdle, 2), Outline = FrozenPen(Ui.Ink, 1);
        private static readonly Brush ReferenceColor = Ui.Brush("#FFE08A");
        private static readonly Pen ReferenceOutline = FrozenPen(Ui.Ink, 7), ReferenceStroke = FrozenPen(ReferenceColor, 4);
        private static readonly StreamGeometry ReferencePointer = MakeReferencePointer();
        internal BeltMarking Marking { get; } = new();
        private static StreamGeometry MakeReferencePointer()
        {
            var geometry = new StreamGeometry(); using (var context = geometry.Open())
            { context.BeginFigure(new Point(0, 230), true, true); context.LineTo(new Point(-7, 216), true, false); context.LineTo(new Point(7, 216), true, false); }
            geometry.Freeze(); return geometry;
        }
        private static readonly RectangleGeometry BeltClip = MakeBeltClip();
        private static RectangleGeometry MakeBeltClip()
        { var geometry = new RectangleGeometry(new Rect(88, 119, 800, 92)); geometry.Freeze(); return geometry; }
        // Retain stable labels across frames. Dynamic distances are deliberately not cached.
        private readonly Dictionary<(string Text, double Size, Brush Ink), FormattedText> labels = new();
        private double labelScale = double.NaN, labelPixelsPerDip = double.NaN;
        private PlantConfiguration? previewConfiguration;
        private SessionSnapshot? previewSnapshot;
        public SessionSnapshot? Snapshot { get; set; }
        public PlantConfiguration Configuration { get; set; } = new();
        private double fontScale = 1;
        public PlantView() { MinHeight = 210; Focusable = true; System.Windows.Automation.AutomationProperties.SetName(this, "Förderband: links induktiver Sensor 2, Mitte Optik, rechts induktiver Sensor 1. Aktuelle Position, Bewegung und Signalpegel am Teststand."); }
        private void Label(DrawingContext dc, string text, double x, double y, double size = 13, Brush? brush = null, bool cache = true)
        {
            Brush ink = brush ?? Ui.Ink;
            var key = (text, size, ink);
            if (!cache || !labels.TryGetValue(key, out var label))
            {
                label = new FormattedText(text, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight,
                    LabelTypeface, size / fontScale, ink, labelPixelsPerDip);
                if (cache) { if (labels.Count >= 128) labels.Clear(); labels[key] = label; }
            }
            dc.DrawText(label, new Point(x, y));
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            bool preview = Snapshot == null;
            if (preview && (!ReferenceEquals(previewConfiguration, Configuration) || previewSnapshot == null))
            {
                // Static geometry only: the session stays paused and is never advanced.
                // Do not publish this preview as Snapshot or as an external PLC sample.
                previewSnapshot = new SimulationSession(Configuration).Snapshot();
                previewConfiguration = Configuration;
            }
            double scale = Math.Max(.4, ActualWidth / 1080);
            fontScale = scale;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (labelScale != scale || labelPixelsPerDip != pixelsPerDip)
            { labels.Clear(); labelScale = scale; labelPixelsPerDip = pixelsPerDip; }
            dc.PushTransform(new ScaleTransform(scale, scale));
            var p = (Snapshot ?? previewSnapshot!).Plant; double X(double mm) => 65 + mm / Configuration.BeltLengthMm * 850;
            dc.DrawRectangle(Ui.Surface, null, new Rect(0, 0, 1080, scale < .8 ? 386 : 340));
            Label(dc, preview ? "ANLAGE  /  Vorschau · keine SPS-Werte" : "ANLAGE  /  technische Draufsicht", 30, 12, 15);
            bool compact = scale < .8;
            if (compact) dc.PushTransform(new TranslateTransform(0, 46));
            dc.DrawRoundedRectangle(Frame, null, new Rect(35, 102, 910, 126), 18, 18);
            dc.DrawRoundedRectangle(Ui.Belt, null, new Rect(49, 114, 882, 102), 40, 40);
            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? 65 : 915;
                dc.DrawEllipse(Roller, RollerOutline, new Point(x, 165), 29, 43);
                dc.DrawLine(RollerAxis, new Point(x - 15 * Math.Cos(p.BeltMm / 30), 165 - 27 * Math.Sin(p.BeltMm / 30)), new Point(x + 15 * Math.Cos(p.BeltMm / 30), 165 + 27 * Math.Sin(p.BeltMm / 30)));
            }
            dc.PushClip(BeltClip);
            double shift = ((p.BeltMm % 80) + 80) % 80 / Configuration.BeltLengthMm * 850;
            for (double x = -40 + shift; x < 950; x += 68) dc.DrawLine(BeltStroke, new Point(x, 127), new Point(x + 18, 203));
            dc.Pop();
            double markPosition = Marking.PositionMm(p.BeltMm);
            bool markOnScale = Marking.IsOnScale(p.BeltMm, Configuration.BeltLengthMm);
            if (markOnScale)
            {
                double x = X(markPosition);
                dc.DrawLine(ReferenceOutline, new Point(x, 125), new Point(x, 212));
                dc.DrawLine(ReferenceStroke, new Point(x, 125), new Point(x, 212));
                dc.PushTransform(new TranslateTransform(x, 0));
                dc.DrawGeometry(ReferenceColor, Outline, ReferencePointer); dc.Pop();
            }
            if (p.Present)
            {
                double width = p.TrueLengthMm / Configuration.BeltLengthMm * 850;
                dc.DrawRoundedRectangle(Workpiece, WorkpieceOutline, new Rect(X(p.LeftEdgeMm), 143, width, 45), 3, 3);
                Label(dc, width * scale > 75 ? "Prüfling P1" : "P1", X(p.LeftEdgeMm) + 7, 155, 12);
            }
            SensorReading[] readings = { p.Left, p.Optical, p.Right };
            for (int i = 0; i < 3; i++)
            {
                double x = X(Configuration.Sensor(SensorIds[i]).PositionMm);
                dc.DrawLine(!preview && readings[i].Ideal ? SensorActiveLine : SensorIdleLine, new Point(x, 97), new Point(x, 142));
                dc.DrawRoundedRectangle(!preview && readings[i].High ? Ui.Accent : SensorLow, null, new Rect(x - 12, 78, 24, 25), 3, 3);
                Label(dc, SensorLabels[i], x - 58, compact ? 10 : 40, 12);
                Label(dc, preview ? "—" : readings[i].High ? "HIGH" : "LOW", x - 18, compact ? 40 : 58, 11);
            }
            for (double mm = 0; mm <= Configuration.BeltLengthMm; mm += 10)
            {
                double x = X(mm); bool major = mm % 100 == 0;
                dc.DrawLine(Outline, new Point(x, 235), new Point(x, major ? 247 : mm % 50 == 0 ? 244 : 241));
                if (major) Label(dc, mm.ToString("0", CultureInfo.CurrentCulture), x - 10, 250, 11);
            }
            Label(dc, compact ? "mm · Teilung 10" : "mm · Teilung 10 mm · Beispielgeometrie", 70, 273, 12);
            dc.DrawRoundedRectangle(Roller, Outline, new Rect(872, 277, 68, 35), 4, 4);
            Label(dc, "M + Hall", 878, 285, compact ? 10 : 12, Ui.Surface);
            bool[] lamps = { p.Actuators.LampRed, p.Actuators.LampYellow, p.Actuators.LampGreen };
            for (int i = 0; i < 3; i++)
            {
                double y = 112 + i * 53;
                dc.DrawEllipse(!preview && lamps[i] ? LampColors[i] : LampOff, Outline, new Point(985, y), 14, 14);
                bool unavailable = preview || (Snapshot!.Mode == OperationMode.ExternalPlc && !p.Actuators.Valid);
                string label = scale < .8 ? (i == 0 ? "R" : i == 1 ? "Ge" : "Gr") + (unavailable ? " —" : lamps[i] ? " 1" : " 0") : LampNames[i] + (unavailable ? " —" : lamps[i] ? " EIN" : " AUS");
                Label(dc, label, 1007, y - 9, scale < .8 ? 10 : 11);
            }
            Label(dc, preview ? "Vorschau · noch keine Bewegungsdaten" : compact ? $"Band {p.BeltMm:F1} · Motor {p.MotorMm:F1} mm" : $"Bandweg {p.BeltMm:F1} mm   |   Motorweg {p.MotorMm:F1} mm", 325, 290, 13, cache: preview);
            Label(dc, preview ? "Bandmarkierung · Start bei 0 mm" :
                (compact ? "Marke: " : "Bandmarkierung: ") + $"{markPosition:F1} mm" + (!markOnScale ? " · außerhalb der Skala" : "") +
                (Snapshot!.Mode == OperationMode.ExternalPlc && !p.Actuators.Valid ? " · letzter Wert" : ""), 70, 315, 12, cache: preview);
            if (compact) dc.Pop();
            dc.Pop();
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 1080 : availableSize.Width;
            return new(width, Math.Max(210, width / 1080 * (width / 1080 < .8 ? 386 : 340)));
        }
    }

    public sealed class TraceView : FrameworkElement
    {
        private static readonly Typeface TraceTypeface = new("Consolas");
        private static readonly string[] LaneNames = { "Hall", "Optik", "Sensor 2", "Sensor 1", "Motor R" };
        private static readonly Brush[] LaneColors = { Ui.Warning, Ui.Accent, Ui.Ink, Ui.Ink, Ui.Brush("#596B7C") };
        private static readonly Pen[] LanePens = CreateLanePens();
        private static readonly Pen GridPen = FrozenPen(Ui.Brush("#DAE1E7"), 1);
        private static Pen FrozenPen(Brush brush, double width)
        { var pen = new Pen(brush, width); pen.Freeze(); return pen; }
        private static Pen[] CreateLanePens()
        {
            var pens = new Pen[LaneColors.Length];
            for (int lane = 0; lane < pens.Length; lane++) pens[lane] = FrozenPen(LaneColors[lane], 1.5);
            return pens;
        }
        private FormattedText[]? laneLabels;
        private FormattedText? timeLabel;
        private double labelPixelsPerDip = double.NaN;
        public InputSample[] Samples { get; set; } = Array.Empty<InputSample>();
        public TraceView() { Height = 205; System.Windows.Automation.AutomationProperties.SetName(this, "Signalverlauf der letzten Sekunde: Hall, Optik, Sensor 2, Sensor 1 und Motor rechts."); }
        internal static StreamGeometry BuildLaneGeometry(InputSample[] samples, int lane, double width)
        {
            var geometry = new StreamGeometry();
            if (samples.Length < 2) { geometry.Freeze(); return geometry; }
            long end = samples[samples.Length - 1].TimeUs, start = end - 1000000;
            double X(long t) => 92 + (t - start) / 1000000.0 * Math.Max(20, width - 110);
            bool Value(InputSample s) => lane == 0 ? s.Sensors.HallHigh : lane == 1 ? s.Sensors.OpticalHigh : lane == 2 ? s.Sensors.SensorLeftHigh : lane == 3 ? s.Sensors.SensorRightHigh : s.Actuators.MotorRight;
            int first = 0;
            while (first < samples.Length && samples[first].TimeUs < start) first++;
            if (first >= samples.Length - 1) { geometry.Freeze(); return geometry; }
            double y = lane * 36 + 25;
            // Equal-level samples lie on one horizontal segment. Only drawing commands
            // are coalesced: every sampled edge keeps its exact timestamp and level.
            using (var path = geometry.Open())
            {
                bool level = Value(samples[first]);
                path.BeginFigure(new Point(X(samples[first].TimeUs), y - (level ? 18 : 0)), false, false);
                for (int i = first + 1; i < samples.Length; i++)
                {
                    bool next = Value(samples[i]);
                    if (next == level) continue;
                    double x = X(samples[i].TimeUs);
                    path.LineTo(new Point(x, y - (level ? 18 : 0)), true, false);
                    path.LineTo(new Point(x, y - (next ? 18 : 0)), true, false);
                    level = next;
                }
                path.LineTo(new Point(X(end), y - (level ? 18 : 0)), true, false);
            }
            geometry.Freeze(); return geometry;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); dc.DrawRectangle(Ui.Surface, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (Samples.Length == 0) return;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (laneLabels == null || labelPixelsPerDip != pixelsPerDip)
            {
                labelPixelsPerDip = pixelsPerDip; laneLabels = new FormattedText[LaneNames.Length];
                for (int lane = 0; lane < LaneNames.Length; lane++)
                    laneLabels[lane] = new FormattedText(LaneNames[lane], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TraceTypeface, 12, Ui.Ink, pixelsPerDip);
                timeLabel = new FormattedText("−1000 ms                               abgetastete Pegel → jetzt", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TraceTypeface, 11, Ui.Ink, pixelsPerDip);
            }
            for (int lane = 0; lane < 5; lane++)
            {
                double y = lane * 36 + 25;
                dc.DrawText(laneLabels[lane], new Point(5, y - 15));
                dc.DrawLine(GridPen, new Point(90, y), new Point(ActualWidth - 8, y));
                dc.DrawGeometry(null, LanePens[lane], BuildLaneGeometry(Samples, lane, ActualWidth));
            }
            dc.DrawText(timeLabel!, new Point(92, 185));
        }
    }
}
