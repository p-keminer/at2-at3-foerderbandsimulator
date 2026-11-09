using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using Microsoft.CSharp;
using Plant.Adapter;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static void RunLampProtocolTests()
    {
        AsyncTest("L01", "Fensterloser Prozesskanal: acht Lampenkombinationen, Snapshotkopie und Ablehnung alter Sieben-Bit-Daten", async () =>
        {
            // The real WPF process host, but no WPF window, UI Automation or Siemens assembly.
            string folder = Path.Combine(Path.GetTempPath(), "AT2AT3-SiemensFree-Lamps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            const string fixture = """
using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
internal static class SiemensFreeLampFixture {
  static int Main(string[] args) {
    string report=args[Array.IndexOf(args,"--report")+1];
    bool legacy=Path.GetFileName(report)=="legacy.json";
    Console.OutputEncoding=new UTF8Encoding(false);
    Console.WriteLine("SIEMENS_FREE_LAMP_PROTOCOL_FIXTURE");
    var json=new JavaScriptSerializer();
    for(int mask=0;mask<8;mask++) {
      Console.WriteLine("SAMPLE "+json.Serialize(new { SchemaVersion=legacy?1:3,
        Sequence=mask+1, TimeNs=mask*1000000L, Position=50, Length=120, Present=true,
        LampRed=(mask&1)!=0, LampYellow=(mask&2)!=0, LampGreen=(mask&4)!=0 }));
    }
    Console.WriteLine("FINALIZING");
    File.WriteAllText(report,json.Serialize(new { fixture=true, result=new {
      Profile="AT2_Kopplung_9_digital_bits_SingleStep_C", Passed=true,
      CleanupConfirmed=true, BeforeDisconnect=new { InstanceName="AT2_Kopplung",
        OperatingState="Freeze", OperatingMode="SingleStep_C" }, EndReason="AdvanceLimit" } }));
    return 0;
  }
}
""";
            try
            {
                string exe = Path.Combine(folder, "AT2AT3.V5.LiveProbe.exe");
                using (var compiler = new CSharpCodeProvider())
                {
                    var options = new CompilerParameters(new[] { "System.dll", "System.Web.Extensions.dll" }, exe)
                    { GenerateExecutable = true, CompilerOptions = "/platform:x64 /optimize+" };
                    var compiled = compiler.CompileAssemblyFromSource(options, fixture);
                    Assert(!compiled.Errors.HasErrors, string.Join("; ", compiled.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
                }
                string map = Path.Combine(folder, "map.json"), profile = Path.Combine(folder, "profile.json"), plant = Path.Combine(folder, "plant.json");
                File.WriteAllText(map, Json.Write(DigitalMap()));
                File.WriteAllText(profile, Json.Write(DigitalProfile()));
                File.WriteAllText(plant, Json.Write(new PlantConfiguration()));
                foreach (bool legacy in new[] { false, true })
                {
                    var host = new LiveProcessHost();
                    await host.RunAsync(new LiveProcessRequest { ProbePath = exe, WorkingDirectory = folder, MapPath = map,
                        ProfilePath = profile, PlantPath = plant, ReportPath = Path.Combine(folder, legacy ? "legacy.json" : "current.json"),
                        RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true,
                        Limits = new DigitalRunLimits { MaxAdvances = 8, WallTimeoutSeconds = 5 } });
                    var state = host.Snapshot;
                    Assert(state.StationaryTest && state.EndReason == "AdvanceLimit", "Testart oder Endgrund im Prozessbericht verloren");
                    Assert(state.Passed == !legacy && !state.HardKilled && !state.IsActive, "Falsches Protokoll akzeptiert oder Prozess nicht beendet: " + state.Error);
                    if (legacy)
                    {
                        Assert(state.Latest == null && state.Error != null && state.Error.Contains("Version"), "Altes Protokoll wurde als gültige Lampenwerte dargestellt");
                        continue;
                    }
                    var samples = host.DrainSamples();
                    Assert(samples.Length == 8 && state.CleanupConfirmed, "Lampenkombinationen im Prozesskanal verloren");
                    for (int mask = 0; mask < 8; mask++)
                    {
                        var view = samples[mask].ToActuatorFrame(true);
                        Assert(view.LampRed == ((mask & 1) != 0) && view.LampYellow == ((mask & 2) != 0) && view.LampGreen == ((mask & 4) != 0),
                            "Lampenkombination im Prozesskanal verändert");
                    }
                    Assert(state.Latest!.LampRed && state.Latest.LampYellow && state.Latest.LampGreen, "Hostkopie verliert Lampen");
                    state.Latest.LampRed = state.Latest.LampYellow = state.Latest.LampGreen = false;
                    Assert(host.Snapshot.Latest!.LampRed && host.Snapshot.Latest.LampYellow && host.Snapshot.Latest.LampGreen, "Hostkopie ist veränderbarer interner Zustand");
                }
            }
            finally
            {
                // Only our exact GUID fixture directory; no recursive deletion or computed parent removal.
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        });
        Test("L02", "Stationärer Austausch ist keine Fahrt; CPU-Rückstellung verdeckt den Laufstatus nicht", () =>
        {
            var stationary = new LiveProcessSnapshot(LiveProcessState.Completed, "fixture", Passed: true, StationaryTest: true, EndReason: "AdvanceLimit");
            Assert(LiveStatusText.StartButton(false).Contains("Stationären") && LiveStatusText.StartButton(true).Contains("Förderbanddurchlauf"), "Startbutton verschweigt Testart");
            Assert(LiveStatusText.Hint(false).Contains("ausgeschalteten Motoren") && LiveStatusText.Hint(true).Contains("Betriebsfreigaben"), "Voraussetzungen nicht erkennbar");
            string status = LiveStatusText.AfterRecovery(stationary, "CPU: Run / Default bestätigt.");
            Assert(status.Contains("Kein Förderbanddurchlauf") && status.Contains("Run / Default"), "CPU-Status verdeckt das stationäre Ergebnis");
            var fault = stationary with { State = LiveProcessState.Faulted, Passed = false, Error = "Fixture: Zeitüberschreitung" };
            Assert(LiveStatusText.AfterRecovery(fault, "CPU: Run / Default bestätigt.").Contains(fault.Error), "Rückstellung verdeckt Laufabbruch");
            var transport = stationary with { StationaryTest = false, TransportCompleted = true };
            Assert(LiveStatusText.Outcome(transport).Contains("Endabschaltung bestätigt"), "Bestätigter Transport nicht erkennbar");
            Assert(!LiveStatusText.Outcome(transport with { Passed = false, Error = "fixture" }).Contains("bestanden ·"), "Fehler als erfolgreicher Transport angezeigt");
        });
    }
}
