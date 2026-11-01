using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CSharp;
using Plant.Adapter;

namespace Plant.Wpf
{
    // Invoked only by --smoke. This separately compiled executable has no Siemens references.
    internal static class DefaultRecoverySmokeChecks
    {
        internal static async Task Run(string directory, Action<bool, string> check)
        {
            string folder = Path.Combine(Path.GetFullPath(directory), "siemens-free-default-fixture");
            Directory.CreateDirectory(folder);
            string executable = Path.Combine(folder, "AT2AT3.V5.RestoreDefault.exe");
            const string source = """
using System;
using System.IO;
using System.Threading;
internal static class Fixture {
  static int Main(string[] args) {
    string report=args[Array.IndexOf(args,"--report")+1];
    string kind=Path.GetFileNameWithoutExtension(report);
    Console.WriteLine("SIEMENS_FREE_DEFAULT_FIXTURE");
    if(kind=="hang") Thread.Sleep(Timeout.Infinite);
    if(kind=="missing") return 0;
    string mode=kind=="wrong-mode"?"SingleStep_C":"Default";
    string cleanup=kind=="bad-cleanup"?"false":"true";
    File.WriteAllText(report,"{\"fixture\":true,\"passed\":true,\"operation\":\"ExplicitRestoreDefault\",\"instance\":\"AT2_Kopplung\",\"observedName\":\"AT2_Kopplung\",\"sdkProductVersion\":\"5.0.0.2\",\"stage\":\"Completed\",\"cleanupConfirmed\":"+cleanup+",\"defaultAndRunObserved\":true,\"afterMode\":\""+mode+"\",\"afterState\":\"Run\"}");
    return kind=="exit-failure"?1:0;
  }
}
""";
            File.WriteAllText(Path.Combine(folder, "Fixture.cs"), source);
            using (var compiler = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters(new[] { "System.dll" }, executable) {
                    GenerateExecutable = true, GenerateInMemory = false, CompilerOptions = "/platform:x64 /optimize+"
                };
                var result = compiler.CompileAssemblyFromSource(parameters, source);
                check(!result.Errors.HasErrors, "Siemensfreie Default-Fixture kompiliert: " + string.Join("; ", result.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }

            var owned = new LiveProcessSnapshot(LiveProcessState.Completed, "fixture", new DigitalProgress { Sequence = 2 },
                ReportPath: "owned.json", CleanupConfirmed: true, CpuBeforeDisconnect: "Freeze / SingleStep_C", ExitCode: 0, FreezeOwnershipConfirmed: true);
            check(MainWindow.CanRestoreAfterRun(owned), "Automatische Default-Freigabe nach eigenem abgeschlossenen Syncpunkt und Cleanup zulässig");
            check(MainWindow.CanRestoreAfterRun(owned with { ExitCode = 1 }), "Eigener kooperativ abgebrochener Lauf kann Default freigeben");
            foreach (var denied in new[] {
                owned with { Latest = null }, owned with { IsActive = true }, owned with { CleanupConfirmed = false },
                owned with { FreezeOwnershipConfirmed = false },
                owned with { HardKilled = true }, owned with { ExitCode = null }, owned with { ExitCode = 2 },
                owned with { CpuBeforeDisconnect = "Run / Default" }, owned with { ReportPath = "" },
                owned with { Latest = new DigitalProgress { Sequence = 0 } }
            }) check(!MainWindow.CanRestoreAfterRun(denied), "Unbestätigter oder fremder Freeze löst keine automatische Default-Freigabe aus");

            var host = new DefaultRecoveryHost();
            string refused = Path.Combine(folder, "refused.json");
            bool rejected = false;
            try { await host.RunAsync(executable, folder, refused, true, false); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && !File.Exists(refused + ".stdout.txt") && !host.IsActive,
                "Fehlende Default-Freigabe blockiert vor Prozessstart");

            var good = await host.RunAsync(executable, folder, Path.Combine(folder, "success.json"), true, true);
            check(good.Passed && good.ProcessExited && good.OutputCaptured && good.ExitCode == 0 && !good.HardTimeout,
                "Fixture-Erfolg verlangt Exit0, Default/Run, korrekte Instanz und bestätigtes Cleanup");
            check(!host.IsActive && !host.TerminationUnconfirmed && File.Exists(good.ReportPath + ".host.json"),
                "Default-Host beendet und protokolliert eigene Prozesse separat");
            string json = File.ReadAllText(good.ReportPath);
            check(!DefaultRecoveryHost.ValidateReport(json.Replace("AT2_Kopplung", "Other")), "Falsche Instanz im Default-Bericht zurückgewiesen");
            check(!DefaultRecoveryHost.ValidateReport(json.Replace("5.0.0.2", "6.0.0.0")), "Falsche SDK-Version im Default-Bericht zurückgewiesen");
            check(!DefaultRecoveryHost.ValidateReport(json.Replace("\"defaultAndRunObserved\":true", "\"defaultAndRunObserved\":false")), "Fehlendes Default/Run-Rücklesen zurückgewiesen");
            foreach (string name in new[] { "wrong-mode", "bad-cleanup", "exit-failure", "missing" })
            {
                var result = await host.RunAsync(executable, folder, Path.Combine(folder, name + ".json"), true, true);
                check(!result.Passed && result.ProcessExited && result.Error != null, "Default-Fixture " + name + " bestätigt keine erfolgreiche Rückstellung");
            }
            var timer = Stopwatch.StartNew();
            var hangTask = host.RunAsync(executable, folder, Path.Combine(folder, "hang.json"), true, true);
            rejected = false;
            try { await host.RunAsync(executable, folder, Path.Combine(folder, "overlap.json"), true, true); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "Konkurrierende Default-Hilfsprozesse werden vor Start verweigert");
            var hang = await hangTask;
            check(!hang.Passed && hang.HardTimeout && hang.ProcessExited && timer.Elapsed.TotalSeconds < 17,
                "Blockierte Siemensfreie Default-Fixture nach 10 Sekunden begrenzt; nur eigener Child beendet");
            check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => (a.GetName().Name ?? "").StartsWith("Siemens.")),
                "Default-Tests laden keine Siemens-Assembly in WPF");
        }
    }
}
