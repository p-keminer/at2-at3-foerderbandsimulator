using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    internal static class LayoutStabilityChecks
    {
        internal static IEnumerable<DependencyObject> All(DependencyObject n)
        { yield return n; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) foreach (var c in All(VisualTreeHelper.GetChild(n, i))) yield return c; }
        internal static void Run(MainWindow w, string dir, Action<bool, string> check)
        {
            var results = new List<object>();
            var original = (StudyProfile)w.ProfileSelect.SelectedItem;
            foreach (double width in new[] { 1000.0, 1480.0 })
            {
                w.Width = width; w.Height = 800; w.Tabs.SelectedIndex = 1; w.Refresh(); w.UpdateLayout();
                var grid = All(w).OfType<DataGrid>().Single(g => g.IsVisible);
                var plant = All(w).OfType<PlantView>().Single();
                var scroll = All(grid).OfType<ScrollViewer>().First();
                object? items = null; DataGridColumn? first = null; double[]? columns = null;
                Rect? bounds = null; double maxShift = 0, maxColumnShift = 0; int replacements = 0;
                for (int i = 0; i < 50; i++)
                {
                    var sample = new DigitalProgress { Sequence = i + 1, TimeNs = i % 2 == 0 ? 1000000 : 123456789012345,
                        Position = 200, Length = 120, Present = true, MotorRight = i % 2 == 0, Left = i % 2 != 0 };
                    w.DisplayLive(new LiveProcessSnapshot(LiveProcessState.Streaming, "Layout fixture", sample, IsActive: true));
                    w.UpdateLayout();
                    var b = new Rect(plant.TranslatePoint(new Point(), w), plant.RenderSize);
                    var c = grid.Columns.Select(x => x.ActualWidth).ToArray();
                    if (i > 5)
                    {
                        if (!ReferenceEquals(items, grid.ItemsSource) || !ReferenceEquals(first, grid.Columns[0])) replacements++;
                        maxShift = Math.Max(maxShift, Math.Abs(b.X - bounds!.Value.X) + Math.Abs(b.Y - bounds.Value.Y) + Math.Abs(b.Width - bounds.Value.Width) + Math.Abs(b.Height - bounds.Value.Height));
                        maxColumnShift = Math.Max(maxColumnShift, c.Zip(columns!, (a, z) => Math.Abs(a-z)).Max());
                    }
                    items = grid.ItemsSource; first = grid.Columns[0]; columns = c; bounds = b;
                }
                grid.Columns[0].Width = 181;
                grid.SelectedIndex = 4; var selected = grid.SelectedItem;
                scroll.ScrollToHorizontalOffset(110); scroll.ScrollToVerticalOffset(60); w.UpdateLayout();
                double horizontal = scroll.HorizontalOffset, vertical = scroll.VerticalOffset;
                var existing = (SignalRow)grid.Items[0];
                w.DisplayLive(new LiveProcessSnapshot(LiveProcessState.Streaming, "Layout fixture",
                    new DigitalProgress { Sequence = 99, TimeNs = 987654321, Position = 200, Length = 120, Present = true, Left = false }, IsActive: true));
                w.UpdateLayout();
                check(ReferenceEquals(selected, grid.SelectedItem) && grid.Columns[0].ActualWidth == 181 &&
                    Math.Abs(scroll.HorizontalOffset - horizontal) < .1 && Math.Abs(scroll.VerticalOffset - vertical) < .1,
                    "Auswahl, eigene Spaltenbreite und beide Scrollpositionen bleiben bei " + width + " px erhalten");
                check(ReferenceEquals(existing, grid.Items[0]) && existing.Value == "LOW / 0", "Signalwert aktualisiert sich ohne Austausch der Zeile bei " + width + " px");
                results.Add(new { width, maxShift, maxColumnShift, replacements, bounds, columns, horizontal, vertical });
                check(maxShift < .1 && maxColumnShift < .1 && replacements == 0, "Stabile Anlage und Signalspalten bei " + width + " px / 50 wechselnden Liveframes");
            }
            Json.SaveAtomic(Path.Combine(dir, "layout-stability.json"), Json.Write(results));
            w.ApplyStudyProfile(original); w.Refresh();
        }
    }
}
