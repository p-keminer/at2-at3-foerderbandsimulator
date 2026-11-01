using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.CSharp;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    // Runs only from the explicit --smoke harness. The compiled child contains no
    // simulator/adapter reference and cannot call Siemens. Its evidence is marked fixture.
    internal static class LiveSmokeChecks
    {
        public static async Task Run(MainWindow window, string directory, Action<bool, string> check)
        {
            string folder = Path.Combine(Path.GetFullPath(directory), "siemens-free-live-fixture"); Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, "AT2AT3.V5.LiveProbe.exe");
            const string source = """
using System;
using System.IO;
using System.Text;
using System.Threading;
internal static class Fixture {
  static string Value(string[] args, string name) { int i=Array.IndexOf(args,name); return i<0?"":args[i+1]; }
  static void Sample(int n) {
    Console.WriteLine("SAMPLE {\"SchemaVersion\":1,\"Sequence\":"+n+",\"TimeNs\":"+((n-1)*1000000L)+",\"Position\":"+(50+n)+",\"Length\":120,\"MotorMm\":"+n+",\"BeltMm\":"+n+",\"MotorVelocity\":100,\"ObjectVelocity\":100,\"Present\":true,\"LeftIdeal\":true,\"LeftDetected\":true,\"Left\":true,\"Optical\":false,\"Right\":false,\"Hall\":true,\"MotorRight\":true,\"MotorLeft\":false,\"LampGreen\":true,\"GeneratedRisingEdges\":"+n+"}");
    Console.Out.Flush();
  }
  static int Main(string[] args) {
    Console.OutputEncoding=new UTF8Encoding(false);
    string report=Value(args,"--report"), kind=Path.GetFileNameWithoutExtension(report);
    Console.WriteLine("SIEMENS_FREE_WPF_PROTOCOL_FIXTURE");
    Sample(1);
    if(kind=="hang") Thread.Sleep(Timeout.Infinite);
    bool cancel=kind=="cancel";
    if(cancel) Console.ReadLine();
    else { Sample(kind=="bad-sequence"?3:2); Thread.Sleep(700); }
    Console.WriteLine(kind=="malformed-final"?"FINALIZING {}":"FINALIZING"); Console.Out.Flush();
    if(kind=="duplicate-final") { Console.WriteLine("FINALIZING"); Console.Out.Flush(); }
    if(kind=="sample-after-final") Sample(3);
    if(kind=="slow-final") Thread.Sleep(6500);
    if(kind=="final-hang") Thread.Sleep(Timeout.Infinite);
    if(kind=="no-report") return 0;
    string mode=kind=="wrong-mode"?"Default":"SingleStep_C";
    File.WriteAllText(report,"{\"fixture\":true,\"result\":{\"Profile\":\"AT2_Kopplung_7_digital_bits_SingleStep_C\",\"Passed\":"+(cancel?"false":"true")+",\"TransportCompleted\":false,\"PlcMeasurementVerified\":false,\"CleanupConfirmed\":true,\"BeforeDisconnect\":{\"InstanceName\":\"AT2_Kopplung\",\"OperatingState\":\"Freeze\",\"OperatingMode\":\""+mode+"\"},\"Error\":"+(cancel?"\"System.OperationCanceledException: fixture\"":"null")+",\"CleanupError\":null,\"InspectionError\":null,\"EndReason\":\""+(cancel?"FaultOrCanceled":"StepLimit")+"\"}}",new UTF8Encoding(false));
    return cancel?1:0;
  }
}
""";
            File.WriteAllText(Path.Combine(folder, "Fixture.cs"), source);
            using (var compiler = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters(new[] { "System.dll" }, probe) { GenerateExecutable = true, GenerateInMemory = false, CompilerOptions = "/platform:x64 /optimize+" };
                var result = compiler.CompileAssemblyFromSource(parameters, source);
                check(!result.Errors.HasErrors, "Siemensfreie Protokollfixture separat kompiliert: " + string.Join("; ", result.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string map = Path.Combine(folder, "confirmed-map.json"), profile = Path.Combine(folder, "digital-profile.json"), plant = Path.Combine(folder, "plant.json");
            File.Copy(Path.Combine(root, "config", "io-map.AT2_Kopplung.partial.json"), map, true);
            Json.SaveAtomic(profile, Json.Write(new DigitalCycleProfile { Ob1AutomaticImageConfirmed = true }));
            Json.SaveAtomic(plant, Json.Write(new PlantConfiguration { HallPitchMm = 1.2 }));
            LiveProcessRequest Request(string name) => new() { ProbePath = probe, WorkingDirectory = folder,
                MapPath = map, ProfilePath = profile, PlantPath = plant, ReportPath = Path.Combine(folder, name + ".json"),
                RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true,
                Limits = new DigitalRunLimits { MaxAdvances = 12, WallTimeoutSeconds = 5, MaxVirtualSeconds = 1, RequireStationary = false } };
            async Task WaitSample(LiveProcessHost host)
            {
                var clock = Stopwatch.StartNew();
                while (host.Snapshot.Latest == null && host.Snapshot.IsActive && clock.ElapsedMilliseconds < 3000) await Task.Delay(10);
                check(host.Snapshot.Latest != null, "Fixture-Snapshot erreicht WPF-Prozesshost");
            }
            PlantView? FindPlant(DependencyObject target)
            {
                if (target is PlantView view) return view;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
                { var found = FindPlant(VisualTreeHelper.GetChild(target, i)); if (found != null) return found; }
                return null;
            }
            check(!window.LiveConnect.IsEnabled && !window.LiveDisconnect.IsEnabled, "Keine automatische Liveverbindung; drei Freigaben fehlen");
            window.RuntimeConsent.IsChecked = true; window.CpuConsent.IsChecked = true;
            check(!window.LiveConnect.IsEnabled, "Zwei Freigaben erlauben noch keinen Live-Start");
            window.TiaConsent.IsChecked = true;
            check(window.LiveConnect.IsEnabled, "Drei bewusste Freigaben aktivieren den manuellen Startbutton ohne Prozessstart");
            check(window.LiveProcess.Snapshot.State == LiveProcessState.Disconnected, "Freigabe-Checkboxen initialisieren keine Runtime");
            window.RuntimeConsent.IsChecked = window.CpuConsent.IsChecked = window.TiaConsent.IsChecked = false;
            var refused = Request("refused"); refused.RuntimeApproved = false; bool rejected = false;
            try { await window.LiveProcess.RunAsync(refused); } catch (ArgumentException) { rejected = true; }
            check(rejected && !File.Exists(refused.ReportPath + ".stdout.txt"), "Host verweigert fehlende Runtimefreigabe vor dem Prozessstart");
            window.SourceSelect.SelectedIndex = 2; window.Refresh();
            long offlineTime = window.Host.Read(s => s.Snapshot().Plant.TimeUs);
            var run = window.LiveProcess.RunAsync(Request("success")); await WaitSample(window.LiveProcess);
            window.Tabs.SelectedIndex = 0; window.Refresh(); window.UpdateLayout();
            var live = window.LiveProcess.Snapshot;
            var rendered = FindPlant(window)!.Snapshot!;
            check(rendered.Plant.LeftEdgeMm > 50 && rendered.Plant.Present && rendered.Plant.Left.Ideal && rendered.Plant.Actuators.MotorRight && rendered.Quality == LinkQuality.Connected,
                "Anlagenansicht rendert Position, Prüfling, Sensorgeometrie und SPS-Motorbit aus dem echten Fixture-Datenstrom");
            var rows = window.MakeLiveSignals(live.Latest, true);
            check(rows.Single(r => r.Signal == "MotorRight").Value == "HIGH / 1" && rows.Single(r => r.Signal == "SpeedCommandRaw").Quality == "Unavailable", "SPS-Motorbit sichtbar; analoge Vorgabe bleibt ausdrücklich Unavailable");
            check(rows.Single(r => r.Signal == "MeasuredLengthMm").Value == "—", "Kein erfundener SPS-Messwert aus Modellgeometrie");
            check(!window.RunButton.IsEnabled && !window.StepButton.IsEnabled && !window.SourceSelect.IsEnabled, "Offlinebedienung und Quellenwechsel während des digitalen Laufs gesperrt");
            check(!((System.Windows.Controls.TabItem)window.Tabs.Items[2]).IsEnabled && !((System.Windows.Controls.TabItem)window.Tabs.Items[3]).IsEnabled && !((System.Windows.Controls.TabItem)window.Tabs.Items[4]).IsEnabled, "Kalibrierung, Störungen und Profiländerungen während Livebetrieb gesperrt");
            await run; window.Refresh();
            check(window.LiveProcess.Snapshot.Passed && window.LiveProcess.Snapshot.CleanupConfirmed && window.LiveProcess.Snapshot.State == LiveProcessState.Completed, "Fixture-PASS wird erst mit Exit0, Cleanup und Freeze/SingleStep_C akzeptiert");
            check(window.Host.Read(s => s.Paused && s.Snapshot().Plant.TimeUs == offlineTime), "Kein paralleler Offline- oder Referenzcontrollerfortschritt");
            var copy = window.LiveProcess.Snapshot; copy.Latest!.Position = -999;
            check(window.LiveProcess.Snapshot.Latest!.Position != -999, "Sichtbarer Snapshot kann Hostdaten nicht verändern");
            double stoppedAt = window.LiveProcess.Snapshot.Latest!.Position;
            await Task.Delay(100); window.Refresh();
            check(window.LiveProcess.Snapshot.Latest!.Position == stoppedAt && window.Host.Read(s => s.Mode) == OperationMode.ExternalPlc, "Nach Trennen bleibt letzter Zustand stehen; kein automatischer Offlinefallback");
            check(FindPlant(window)!.Snapshot!.Quality == LinkQuality.Stale && FindPlant(window)!.Snapshot!.Plant.LeftEdgeMm == stoppedAt,
                "Gerenderte Anlagenansicht hält den letzten Wert sichtbar und markiert ihn nach Trennen als Stale");
            check(window.MakeLiveSignals(window.LiveProcess.Snapshot.Latest, false).Single(r => r.Signal == "MotorRight").Quality == "veraltet / ungültig", "Historische Motorbefehle werden nach Trennen als ungültig markiert");
            var finalizingRun = window.LiveProcess.RunAsync(Request("slow-final"));
            var finalizingClock = Stopwatch.StartNew();
            while (window.LiveProcess.Snapshot.State != LiveProcessState.Finalizing && window.LiveProcess.Snapshot.IsActive && finalizingClock.ElapsedMilliseconds < 3000) await Task.Delay(10);
            window.Refresh();
            check(window.LiveProcess.Snapshot.State == LiveProcessState.Finalizing && !window.LiveProcess.Snapshot.Passed && !window.LiveProcess.Snapshot.CleanupConfirmed,
                "FINALIZING zeigt Abschlussphase; Marker allein bestätigt weder Erfolg noch Cleanup");
            check(FindPlant(window)!.Snapshot!.Quality == LinkQuality.Stale && !FindPlant(window)!.Snapshot!.Plant.Actuators.Valid,
                "Während der Berichtserstellung ist der letzte sichtbare Aktorframe historisch und ungültig");
            await Task.Delay(5500);
            check(window.LiveProcess.Snapshot.State == LiveProcessState.Finalizing && !window.LiveProcess.Snapshot.StopRequested,
                "Mehr als 5 s Berichtserstellung lösen nach FINALIZING keinen falschen Sampletimeout aus");
            await finalizingRun;
            check(window.LiveProcess.Snapshot.Passed && window.LiveProcess.Snapshot.State == LiveProcessState.Completed,
                "Langsame Berichtserstellung wird nach geprüftem Abschlussbericht korrekt als PASS gewertet");
            var finalDeadline = Stopwatch.StartNew();
            await window.LiveProcess.RunAsync(Request("final-hang"));
            check(finalDeadline.Elapsed.TotalSeconds >= 14 && finalDeadline.Elapsed.TotalSeconds < 20 && window.LiveProcess.Snapshot.HardKilled &&
                !window.LiveProcess.Snapshot.CleanupConfirmed && window.LiveProcess.Snapshot.Error!.Contains("Harte Prozessfrist"),
                "FINALIZING hebt die feste Gesamtfrist nicht auf; blockierende Berichtserstellung endet am Prozesswatchdog");
            foreach (string name in new[] { "bad-sequence", "wrong-mode", "no-report", "duplicate-final", "sample-after-final", "malformed-final" })
            {
                await window.LiveProcess.RunAsync(Request(name));
                check(window.LiveProcess.Snapshot.State == LiveProcessState.Faulted && !window.LiveProcess.Snapshot.Passed, "Protokoll-/Abschlussfehler wird nicht als Erfolg gewertet: " + name);
            }
            var cancelRun = window.LiveProcess.RunAsync(Request("cancel")); await WaitSample(window.LiveProcess);
            await window.LiveProcess.StopAsync(); await cancelRun;
            check(window.LiveProcess.Snapshot.State == LiveProcessState.Completed && !window.LiveProcess.Snapshot.Passed && window.LiveProcess.Snapshot.CleanupConfirmed,
                "STOP beendet Fixture kontrolliert mit Exit1; Abbruch bleibt kein bestandener Test");
            var hangRun = window.LiveProcess.RunAsync(Request("hang")); await WaitSample(window.LiveProcess);
            var stopClock = Stopwatch.StartNew(); await window.LiveProcess.StopAsync(); await hangRun;
            check(stopClock.Elapsed.TotalSeconds < 10 && window.LiveProcess.Snapshot.HardKilled && !window.LiveProcess.Snapshot.CleanupConfirmed && window.LiveProcess.Snapshot.State == LiveProcessState.Faulted,
                "Blockiertes eigenes Fixturekind wird begrenzt beendet; Cleanup bleibt unbestätigt");
            var standalone = new LiveProcessHost(); await standalone.RunAsync(Request("queue"));
            var all = standalone.DrainSamples();
            check(all.Length == 2 && all[0].Sequence == 1 && all[1].Sequence == 2 && standalone.DrainSamples().Length == 0,
                "Signalverlauf erhält sämtliche Streamsamples zwischen Render-Ticks");
            window.Refresh();
            Json.SaveAtomic(Path.Combine(folder, "evidence-note.json"), Json.Write(new { fixture = true, siemensApiCalls = 0,
                note = "Ausschließlich harmloser separat kompilierter Prozess: Protokoll, UI-Gates, Darstellung, STOP, Timeout und Fehlerpfade. Kein Nachweis einer Siemens-Livekopplung." }));
        }
    }
}
