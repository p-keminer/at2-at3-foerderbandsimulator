using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed class SignalRow : INotifyPropertyChanged
    {
        public string Signal { get; }
        public string Direction { get; private set; }
        public string Source { get; private set; }
        public string Value { get; private set; }
        public string Quality { get; private set; }
        public string Time { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public SignalRow(string signal, string direction, string source, string value, string quality, string time)
        { Signal = signal; Direction = direction; Source = source; Value = value; Quality = quality; Time = time; }
        internal void Update(SignalRow row)
        {
            void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (Direction != row.Direction) { Direction = row.Direction; Changed(nameof(Direction)); }
            if (Source != row.Source) { Source = row.Source; Changed(nameof(Source)); }
            if (Value != row.Value) { Value = row.Value; Changed(nameof(Value)); }
            if (Quality != row.Quality) { Quality = row.Quality; Changed(nameof(Quality)); }
            if (Time != row.Time) { Time = row.Time; Changed(nameof(Time)); }
        }
    }
    public sealed partial class MainWindow : Window
    {
        internal readonly SessionHost Host;
        private readonly DispatcherTimer renderTimer;
        internal readonly TabControl Tabs = new();
        private readonly PlantView plantView = new();
        private readonly TraceView trace = new();
        private readonly TextBlock status = Ui.Text("Nicht verbunden. Projektprofil wählen und mit der virtuellen SPS verbinden."),
            time = Ui.Text("", 15, true), motion = Ui.Text(""), diagnostic = Ui.Text(""), adapterStatus = Ui.Text("");
        private readonly DataGrid signals = GridTable(), events = GridTable();
        private readonly ObservableCollection<SignalRow> signalRows = new();
        private Button liveStopButton = null!;
        private readonly TextBlock profileSubtitle = Ui.Text("Lokale virtuelle Siemens-SPS · PLCSIM Advanced", 12);
        private readonly System.Diagnostics.Stopwatch presentationClock = System.Diagnostics.Stopwatch.StartNew();
        private long lastDetailRefresh;
        private int lastDetailTab = -1;
        private PlantConfiguration activeConfig;
        public MainWindow(PlantConfiguration config, string? userProfileDirectory = null)
        {
            this.userProfileDirectory = userProfileDirectory; activeConfig = config.Copy(); Host = new(config);
            Host.Change(s => s.SetMode(OperationMode.ExternalPlc));
            Title = "AT-2 / AT-3 · Virtueller Förderbandteststand";
            Width = Math.Min(1480, SystemParameters.WorkArea.Width); Height = Math.Min(900, SystemParameters.WorkArea.Height); MinWidth = 1000; MinHeight = 720;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Foreground = Ui.Ink; Background = Ui.Background;
            UseLayoutRounding = true;
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            var heading = new StackPanel(); heading.Children.Add(Ui.Text("AT-2 / AT-3", 12, true));
            var title = Ui.Text("Virtueller Förderbandteststand", 23, true); title.FontFamily = new FontFamily("Bahnschrift"); heading.Children.Add(title);
            heading.Children.Add(profileSubtitle);
            time.TextWrapping = TextWrapping.NoWrap; time.TextTrimming = TextTrimming.CharacterEllipsis; time.Height = 48;
            header.Children.Add(heading); Grid.SetColumn(time, 1); header.Children.Add(time); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            liveStopButton = Ui.Button("SPS-Verbindung trennen", DisconnectLive); liveStopButton.IsEnabled = false; bar.Children.Add(liveStopButton);
            var more = new StackPanel(); more.Children.Add(Ui.Button("Aufzeichnung exportieren…", Export)); more.Children.Add(Ui.Button("Snapshot speichern…", SaveSnapshot));
            var menu = new Menu { Background = Ui.Background, VerticalAlignment = VerticalAlignment.Center };
            var item = new MenuItem { Header = "Weitere Aktionen" }; item.Items.Add(more); menu.Items.Add(item); bar.Children.Add(menu);
            DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
            var cpuStatus = BuildCpuStatusBadge(); DockPanel.SetDock(cpuStatus, Dock.Top); root.Children.Add(cpuStatus);
            var profileStrip = new StackPanel(); DockPanel.SetDock(profileStrip, Dock.Top); root.Children.Add(profileStrip); BuildStudyProfilePanel(profileStrip);
            var statusBox = new Border { Child = new ScrollViewer { Content = status, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(8), Background = Ui.Surface, Height = 50, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetName(status, "Statusmeldung"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            DockPanel.SetDock(statusBox, Dock.Bottom); root.Children.Add(statusBox);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar = new StackPanel(); sidebar.Children.Add(BuildLivePanel());
            var detailToggle = Ui.Check("Verbindungsdetails und Ereignisse");
            detailToggle.Checked += (_, _) => { ((TabItem)Tabs.Items[2]).Visibility = ((TabItem)Tabs.Items[3]).Visibility = Visibility.Visible; };
            detailToggle.Unchecked += (_, _) => { Tabs.SelectedIndex = 0; ((TabItem)Tabs.Items[2]).Visibility = ((TabItem)Tabs.Items[3]).Visibility = Visibility.Collapsed; };
            sidebar.Children.Add(detailToggle); body.Children.Add(Ui.Scroll(sidebar));
            LiveConnect.Background = Ui.Accent; LiveConnect.Foreground = Ui.Surface; bar.Children.Insert(0, LiveConnect);
            bar.Children.Insert(bar.Children.IndexOf(liveStopButton) + 1, DefaultRecoveryAction!);
            BuildTabs();
            var observation = new Grid { ClipToBounds = true }; observation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); observation.RowDefinitions.Add(new RowDefinition());
            observation.Children.Add(plantView); Grid.SetRow(Tabs, 1); observation.Children.Add(Tabs);
            Grid.SetColumn(observation, 2); body.Children.Add(observation); root.Children.Add(body);
            InitializeStudyProfiles();
            renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            renderTimer.Tick += (_, _) => Refresh(false); renderTimer.Start();
            Closing += OnClosingLive;
            Closed += (_, _) => { renderTimer.Stop(); Host.Dispose(); };
            Refresh();
        }
        private static DataGrid GridTable() => new()
        {
            IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = Ui.Background, RowHeaderWidth = 0, FontSize = 13, RowHeight = 28,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        private static void Column(DataGrid grid, string header, string property, double width)
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(ToolTipProperty, new Binding(property)));
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width, ElementStyle = style });
        }
        private void UpdateSignalRows(SignalRow[] next)
        {
            // Stable row/column identity preserves selection, scrolling and user column widths.
            for (int i = 0; i < next.Length; i++)
                if (i < signalRows.Count && signalRows[i].Signal == next[i].Signal) signalRows[i].Update(next[i]);
                else if (i < signalRows.Count) signalRows[i] = next[i]; else signalRows.Add(next[i]);
            while (signalRows.Count > next.Length) signalRows.RemoveAt(signalRows.Count - 1);
        }
        internal void Safe(Action action)
        {
            try { SetStatus("Aktion ausgeführt."); action(); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException) { SetStatus(e.Message, true); }
            Refresh();
        }
        internal void SetStatus(string value, bool error = false) { status.Text = value; status.Foreground = error ? Ui.Warning : Ui.Ink; }
        private void BuildTabs()
        {
            var layout = new StackPanel();
            var p = Ui.Section(layout, "Bewegung"); p.Children.Add(motion); Ui.Fold(p, "Anlagendetails").Children.Add(diagnostic);
            Ui.Fold(layout, "Signalverlauf · letzte Sekunde").Children.Add(trace);
            AddTab("Bewegung", Ui.Scroll(layout));
            Column(signals, "Signal", "Signal", 155); Column(signals, "Wert", "Value", 85); Column(signals, "Richtung", "Direction", 145);
            Column(signals, "Qualität", "Quality", 155); Column(signals, "Zeit / Bindung", "Time", 290); Column(signals, "Quelle", "Source", 245);
            signals.ItemsSource = signalRows; AddTab("Signale", signals);
            layout = new StackPanel(); Ui.Section(layout, "Verbindung und Laufbericht").Children.Add(adapterStatus); AddTab("Verbindung", Ui.Scroll(layout));
            Column(events, "Sequenz", "Sequence", 80); Column(events, "Zeit [µs]", "TimeUs", 150); Column(events, "Ereignis", "Kind", 140); Column(events, "Details", "Detail", 650);
            AddTab("Ereignisse", events);
            ((TabItem)Tabs.Items[2]).Visibility = ((TabItem)Tabs.Items[3]).Visibility = Visibility.Collapsed;
        }
        private void AddTab(string label, UIElement child) => Tabs.Items.Add(new TabItem { Header = label, Content = child, Padding = new Thickness(12, 7, 12, 7) });
        internal void Reset(PlantConfiguration config)
        {
            Numbers.Require(!liveHost.Snapshot.IsActive && !liveWorkflowBusy && !RecoveryBusy, "Zuerst die SPS-Verbindung vollständig trennen.");
            Host.Reset(config); Host.Change(s => s.SetMode(OperationMode.ExternalPlc)); activeConfig = config.Copy(); plantView.Configuration = activeConfig;
            plantView.Marking.Reset();
        }
        internal void Refresh(bool forceDetails = true)
        {
            bool details = forceDetails || presentationClock.ElapsedMilliseconds - lastDetailRefresh >= 150 || lastDetailTab != Tabs.SelectedIndex;
            if (details) { lastDetailRefresh = presentationClock.ElapsedMilliseconds; lastDetailTab = Tabs.SelectedIndex; RefreshLiveControls(); }
            RefreshLive(details);
        }
        private void Export() => ExportLive();
        private void SaveSnapshot() => Safe(() =>
        {
            var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "snapshot.json" };
            if (d.ShowDialog(this) == true) Json.SaveAtomic(d.FileName, Json.Write(new { source = "LiveProbe", connection = liveHost.Snapshot, analogOutputs = "Unavailable" }));
        });
    }
}
