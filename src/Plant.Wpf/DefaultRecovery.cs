using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Plant.Core;

namespace Plant.Wpf
{
    public partial class MainWindow
    {
        private readonly DefaultRecoveryHost defaultRecovery = new();
        private readonly Dictionary<string, Task<bool>> automaticRecoveries = new(StringComparer.OrdinalIgnoreCase);
        private TextBlock? defaultRecoveryStatus;
        private readonly CpuStatusIndicator cpuIndicator = new();
        private TextBlock? cpuBadgeText;
        private Border? cpuBadge;
        internal Button? DefaultRecoveryAction;
        // Unconfirmed termination also blocks a competing connection until application restart.
        internal bool RecoveryBusy => defaultRecovery.IsActive || defaultRecovery.TerminationUnconfirmed;
        internal bool RecoveryRunning => defaultRecovery.IsActive;

        private FrameworkElement BuildCpuStatusBadge()
        {
            cpuBadgeText = Ui.Text("CPU-Zustand nicht bestätigt", 13, true);
            cpuBadgeText.Margin = new Thickness(0);
            cpuBadgeText.TextWrapping = TextWrapping.NoWrap; cpuBadgeText.TextTrimming = TextTrimming.CharacterEllipsis;
            cpuBadge = new Border { Child = cpuBadgeText, Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Letzte bestätigte CPU-Rückmeldung mit Uhrzeit. Änderungen außerhalb des Simulators werden erst beim nächsten SPS-Austausch oder einer Rückstellung erkannt." };
            return cpuBadge;
        }

        private FrameworkElement BuildDefaultRecoveryPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var details = new StackPanel();
            details.Children.Add(Ui.Text("Nach einer eigenen, sauber getrennten Sitzung wird die ausgewählte CPU automatisch auf Default zurückgestellt, auch wenn die CPU währenddessen auf STOP wechselte. Für einen zurückgebliebenen Freeze gibt es oben den Button „CPU auf Default zurückstellen“.", 12));
            DefaultRecoveryAction = Ui.Button("CPU auf Default zurückstellen", RestoreDefaultManually);
            // This action is placed in the main toolbar, outside the collapsed details.
            details.Children.Add(Ui.Text("Default beendet die externe Synchronisierung. Eine eingefrorene laufende CPU arbeitet wieder weiter; eine CPU in STOP bleibt in STOP.", 12));
            details.Children.Add(Ui.Text("Kein CPU-Start, kein RUN/STOP, kein automatischer neuer Förderbandlauf.", 12));
            panel.Children.Add(new Expander { Header = "CPU-Freigabe / Wiederherstellung", Content = details, IsExpanded = false });
            defaultRecoveryStatus = Ui.Text("Automatische Default-Freigabe nach eigenen Läufen aktiv.", 12);
            panel.Children.Add(defaultRecoveryStatus);
            return panel;
        }

        private void RefreshDefaultRecoveryControls()
        {
            if (DefaultRecoveryAction == null) return;
            DefaultRecoveryAction.Visibility = IsExternal ? Visibility.Visible : Visibility.Collapsed;
            DefaultRecoveryAction.IsEnabled = selectedProfile != null && selectedProfile.Map.LocalVirtualInstanceConfirmed && !string.IsNullOrWhiteSpace(selectedProfile.Map.InstanceName) && !RecoveryBusy && !liveWorkflowBusy && !liveHost.Snapshot.IsActive && Host.Read(s => s.Paused);
            DefaultRecoveryAction.ToolTip = DefaultRecoveryAction.IsEnabled
                ? "Ausgewählte CPU aus SingleStep/Freeze freigeben. STOP bleibt STOP. Kein RUN-/STOP-Befehl."
                : "Zuerst die SPS-Verbindung trennen und den Simulator pausieren; laufenden Abschluss abwarten.";
            var connection = liveHost.Snapshot;
            var badge = cpuIndicator.Describe(connection, RecoveryRunning, DateTime.Now);
            if (cpuBadge != null && cpuBadgeText != null)
            {
                cpuBadge.Visibility = IsExternal ? Visibility.Visible : Visibility.Collapsed;
                cpuBadgeText.Text = badge.Text; cpuBadgeText.ToolTip = badge.Text;
                if (cpuBadge.Tag as string != badge.Background)
                { cpuBadge.Background = Ui.Brush(badge.Background); cpuBadgeText.Foreground = Ui.Brush(badge.Foreground); cpuBadge.Tag = badge.Background; }
            }
            if (defaultRecoveryStatus != null && connection.IsActive)
                defaultRecoveryStatus.Text = connection.Latest == null ? "CPU-Verbindung wird aufgebaut; Zustand noch nicht bestätigt."
                    : "CPU wird für den E/A-Austausch synchronisiert. Default-Freigabe erfolgt beim Trennen.";
        }

        private static string FindRestoreDefault(string root)
        {
            string[] candidates = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restore-default", "AT2AT3.V5.RestoreDefault.exe"),
                Path.Combine(root, "tools", "RestoreDefault", "bin", "Release", "net48", "AT2AT3.V5.RestoreDefault.exe")
            };
            return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException(
                "RestoreDefault-Hilfsprogramm fehlt. tools/RestoreDefault/RestoreDefault.csproj zuerst kompilieren.");
        }

        internal static bool CanRestoreAfterRun(LiveProcessSnapshot run) => LiveSessionPolicy.CanRestoreOwnFreeze(run);

        internal Task<bool> RestoreDefaultAfterRunAsync(LiveProcessSnapshot run)
        {
            // A verified streamed syncpoint proves that this child owned the freeze.
            // In particular, the BeginCore guard failure has no snapshot and cannot trigger this.
            // Completion and window-close paths await the same task, including a failed
            // attempt. Never let a duplicate caller close while the owned child is running.
            if (automaticRecoveries.TryGetValue(run.ReportPath, out var existing)) return existing;
            if (!CanRestoreAfterRun(run) || RecoveryBusy || liveHost.Snapshot.IsActive) return Task.FromResult(false);
            var recovery = RunDefaultRecoveryAsync(true, run);
            automaticRecoveries.Add(run.ReportPath, recovery);
            return recovery;
        }

        private async void RestoreDefaultManually()
        {
            if (RecoveryBusy || liveWorkflowBusy || liveHost.Snapshot.IsActive || !Host.Read(s => s.Paused)) return;
            await RunDefaultRecoveryAsync(false);
        }

        private async Task<bool> RunDefaultRecoveryAsync(bool automatic, LiveProcessSnapshot? completedRun = null)
        {
            try
            {
                string root = FindProjectDirectory();
                string report = Path.Combine(root, "artifacts", "wpf-default", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
                string instance = (automatic ? liveMap?.InstanceName : selectedProfile.Map.InstanceName) ?? "";
                Numbers.Require(!string.IsNullOrWhiteSpace(instance) && (automatic || selectedProfile.Map.LocalVirtualInstanceConfirmed), "Lokale CPU zuerst im Profil auswählen und bestätigen.");
                var task = defaultRecovery.RunAsync(FindRestoreDefault(root), root, report, true, true, automatic, instance);
                if (defaultRecoveryStatus != null) defaultRecoveryStatus.Text = (automatic ? "Eigener Lauf beendet. " : "Manuell angefordert. ") + "Default-Freigabe läuft · maximal 10 Sekunden plus Prozessabschluss…";
                Refresh();
                var result = await task;
                cpuIndicator.RecoveryFinished(result.Passed ? result.CpuState : null, DateTime.Now);
                string detail = result.Passed
                    ? result.CpuState == "Stop" ? "CPU: STOP / Default bestätigt. Jetzt in TIA oder PLCSIM RUN wählen."
                        : "CPU: Run / Default bestätigt. Bereit für die nächste Verbindung."
                    : "Default-Freigabe nicht bestätigt: " + result.Error;
                if (defaultRecoveryStatus != null) { defaultRecoveryStatus.Text = detail; defaultRecoveryStatus.ToolTip = "Separater Abschlussbericht: " + report; }
                SetStatus(completedRun == null ? detail : LiveStatusText.AfterRecovery(completedRun, detail),
                    !result.Passed || completedRun?.State == LiveProcessState.Faulted);
                return result.Passed;
            }
            catch (Exception e)
            {
                cpuIndicator.RecoveryFinished(null, DateTime.Now);
                if (defaultRecoveryStatus != null) defaultRecoveryStatus.Text = "Freigabe nicht ausgeführt: " + e.Message;
                SetStatus("Default-Freigabe nicht ausgeführt oder unbestätigt: " + e.Message, true);
                return false;
            }
            finally { Refresh(); }
        }
    }

    internal sealed record DefaultRecoveryResult(bool Passed, bool ProcessExited, bool HardTimeout,
        bool OutputCaptured, int? ExitCode, string ReportPath, string? Error, string? CpuState = null);

    // The WPF process never loads Siemens code. This authorized operation invokes only the
    // existing fixed-instance maintenance executable; it does not weaken the live begin guard.
    internal sealed class DefaultRecoveryHost
    {
        private readonly object gate = new();
        private bool active, terminationUnconfirmed;
        public bool IsActive { get { lock (gate) return active; } }
        public bool TerminationUnconfirmed { get { lock (gate) return terminationUnconfirmed; } }

        internal Task<DefaultRecoveryResult> RunAsync(string executable, string workingDirectory,
            string reportPath, bool runtimeApproved, bool defaultApproved, bool automaticAfterOwnRun = false, string instanceName = "AT2_Kopplung")
        {
            Numbers.Require(runtimeApproved && defaultApproved, "Eigene Freigaben für Runtime und Default-Fortlauf fehlen.");
            Numbers.Require(!string.IsNullOrWhiteSpace(instanceName) && instanceName.Length <= 128 && !instanceName.Any(char.IsControl), "CPU-Instanzname fehlt oder ist ungültig.");
            executable = Path.GetFullPath(executable); workingDirectory = Path.GetFullPath(workingDirectory); reportPath = Path.GetFullPath(reportPath);
            Numbers.Require(File.Exists(executable) && string.Equals(Path.GetFileName(executable), "AT2AT3.V5.RestoreDefault.exe", StringComparison.OrdinalIgnoreCase), "RestoreDefault-Hilfsprogramm fehlt.");
            Numbers.Require(Directory.Exists(workingDirectory), "Arbeitsordner fehlt.");
            Numbers.Require(string.Equals(Path.GetExtension(reportPath), ".json", StringComparison.OrdinalIgnoreCase), "JSON-Bericht erforderlich.");
            foreach (string suffix in new[] { "", ".stdout.txt", ".stderr.txt", ".host.json" })
                Numbers.Require(!File.Exists(reportPath + suffix), "Bericht existiert bereits: " + reportPath + suffix);
            lock (gate)
            {
                Numbers.Require(!active && !terminationUnconfirmed, "Default-Freigabe läuft bereits oder Ende des vorherigen Hilfsprozesses ist unbestätigt.");
                active = true;
            }
            return Task.Run(() => Execute(executable, workingDirectory, reportPath, automaticAfterOwnRun, instanceName));
        }

        internal static bool ValidateReport(string text)
        {
            return DefaultRecoveryReport.ConfirmedState(text) != null;
        }

        private DefaultRecoveryResult Execute(string executable, string workingDirectory, string reportPath, bool automaticAfterOwnRun, string instanceName)
        {
            var info = new ProcessStartInfo {
                FileName = executable, WorkingDirectory = workingDirectory,
                Arguments = "--freigabe-runtime --freigabe-default-fortlauf --report " + LiveProcessHost.QuoteArgument(reportPath) + " --instance " + LiveProcessHost.QuoteArgument(instanceName),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            using var process = new Process { StartInfo = info };
            bool started = false, ended = false, timeout = false, captured = false, passed = false;
            int? exitCode = null; string? error = null, cpuState = null; Task<string>? stdout = null, stderr = null;
            var elapsed = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                if (!process.Start()) throw new InvalidOperationException("Hilfsprozess konnte nicht gestartet werden.");
                started = true; _ = process.Handle;
                stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
                ended = process.WaitForExit(10000);
                if (!ended)
                {
                    timeout = true;
                    process.Kill(); // Only this owned child, never the Siemens runtime or CPU.
                    ended = process.WaitForExit(2000);
                }
                if (ended) exitCode = process.ExitCode;
                captured = Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                if (timeout || !ended || !captured || exitCode != 0)
                    throw new InvalidOperationException("Prozessende, Frist oder Exitcode bestätigt die Rückstellung nicht.");
                var report = new FileInfo(reportPath);
                if (!report.Exists || report.Length > 1024 * 1024 ||
                    (cpuState = DefaultRecoveryReport.ConfirmedState(File.ReadAllText(reportPath), instanceName)) == null)
                    throw new InvalidOperationException("Abschlussbericht bestätigt Default, CPU-Zustand, Instanz und Cleanup nicht vollständig.");
                passed = true;
            }
            catch (Exception e) { error = e.Message; }
            finally
            {
                if (started && !ended)
                {
                    try { if (!process.HasExited) process.Kill(); ended = process.WaitForExit(2000); if (ended) exitCode = process.ExitCode; }
                    catch (Exception e) { error = (error ?? "Prozessende unbestätigt.") + " " + e.Message; }
                }
                lock (gate) terminationUnconfirmed = started && !ended;
                try
                {
                    void SaveNew(string path, string value)
                    { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); using var writer = new StreamWriter(file, new UTF8Encoding(false)); writer.Write(value); }
                    SaveNew(reportPath + ".stdout.txt", stdout?.Status == TaskStatus.RanToCompletion ? stdout.Result : "[Ausgabe unvollständig]");
                    SaveNew(reportPath + ".stderr.txt", stderr?.Status == TaskStatus.RanToCompletion ? stderr.Result : "[Ausgabe unvollständig]");
                    SaveNew(reportPath + ".host.json", Json.Write(new { utc = DateTime.UtcNow, passed, processExited = ended,
                        hardTimeout = timeout, outputCaptured = captured, exitCode, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
                        error, automaticAfterOwnRun, note = "Autorisierte Default-Freigabe. Kein automatischer Förderbandlauf. Zustand vor Dispose zurückgelesen; danach keine erneute Verbindung." }));
                }
                catch (Exception e) { passed = false; error = "Prozessprotokoll nicht vollständig gespeichert: " + e.Message; }
                lock (gate) active = false;
            }
            return new(passed, ended, timeout, captured, exitCode, reportPath, error, cpuState);
        }
    }
}
