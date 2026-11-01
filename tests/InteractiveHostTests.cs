using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CSharp;
using Plant.Adapter;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static void RunInteractiveHostTests()
    {
        AsyncTest("I08", "Fensterloser WPF-Host bleibt über alte Prozessfrist verbunden, sendet Lebenszeichen und verarbeitet wiederholtes Einsetzen/Entnehmen", async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "AT2AT3-SiemensFree-Interactive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); var host = new LiveProcessHost();
            const string source = """
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
internal static class InteractiveFixture {
  static volatile bool stopped;
  static readonly object gate=new object();
  static Dictionary<string,object> pending;
  static int pings, changes;
  static int Main(string[] args) {
    string report=args[Array.IndexOf(args,"--report")+1];
    bool cpuStop=report.EndsWith("cpu-stop.json",StringComparison.Ordinal);
    if(Array.IndexOf(args,"--interactive")<0) return 2;
    Console.OutputEncoding=new UTF8Encoding(false);
    Console.WriteLine("SIEMENS_FREE_INTERACTIVE_PROTOCOL_FIXTURE");
    var input=new Thread(()=>{
      string line;
      while((line=Console.ReadLine())!=null) {
        if(line=="STOP") { stopped=true; return; }
        if(line=="PING") { Interlocked.Increment(ref pings); continue; }
        lock(gate) pending=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line.Substring(10));
      }
      stopped=true;
    }); input.IsBackground=true; input.Start();
    var json=new JavaScriptSerializer(); int n=0; bool present=false;
    while(!stopped) {
      n++; object change=null;
      lock(gate) if(pending!=null) {
        present=(string)pending["Action"]=="InsertLeft";
        change=new { Id=Convert.ToInt64(pending["Id"]), Sequence=n, TimeNs=(n-1)*1000000L,
          Applied=true, Detail="Siemens-free fixture: command accepted." };
        pending=null; changes++;
      }
      Console.WriteLine("SAMPLE "+json.Serialize(new { SchemaVersion=2, WorkpieceCommandsSupported=true,
        WorkpieceChange=change, Sequence=n, TimeNs=(n-1)*1000000L, Position=40, Length=120,
        Present=present, Left=present, LeftIdeal=present, LeftDetected=present,
        LampRed=false, LampYellow=false, LampGreen=!present }));
      Console.Out.Flush(); Thread.Sleep(20);
      if(cpuStop && n>=4) break;
    }
    Console.WriteLine("FINALIZING"); Console.Out.Flush();
    File.WriteAllText(report,json.Serialize(new { fixture=true, pings=pings, changes=changes,
      result=new { Profile="AT2_Kopplung_9_digital_bits_SingleStep_C", InteractiveSession=true,
        Passed=!cpuStop, CleanupConfirmed=true, BeforeDisconnect=new { InstanceName="AT2_Kopplung",
          OperatingState=cpuStop?"Stop":"Freeze", OperatingMode="SingleStep_C" },
        Error=cpuStop?"System.OperationCanceledException: CPU changed to STOP":null,
        EndReason=cpuStop?"FaultOrCanceled":"Disconnected" } }));
    return cpuStop?1:0;
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
                    var compiled = compiler.CompileAssemblyFromSource(options, source);
                    Assert(!compiled.Errors.HasErrors, string.Join("; ", compiled.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
                }
                string map = Path.Combine(folder, "map.json"), profile = Path.Combine(folder, "profile.json"), plant = Path.Combine(folder, "plant.json"), report = Path.Combine(folder, "report.json");
                File.WriteAllText(map, Json.Write(DigitalMap())); File.WriteAllText(profile, Json.Write(DigitalProfile()));
                File.WriteAllText(plant, Json.Write(new PlantConfiguration { InitialPresent = false }));
                var clock = Stopwatch.StartNew();
                var running = host.RunAsync(new() { ProbePath = exe, WorkingDirectory = folder, MapPath = map, ProfilePath = profile,
                    PlantPath = plant, ReportPath = report, Limits = InteractiveLimits(), RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true });
                while (host.Snapshot.Latest == null && host.Snapshot.IsActive && clock.ElapsedMilliseconds < 3000) await Task.Delay(20);
                Assert(host.CanChangeWorkpiece && host.Snapshot.InteractiveSession && !host.Snapshot.Latest!.Present, "Interaktive leere Verbindung nicht bedienbar: " + host.Snapshot.Error);
                for (int i = 0; i < 2; i++)
                {
                    Assert((await host.ChangeWorkpieceAsync("InsertLeft", 120)).Applied && host.Snapshot.Latest!.Present, "Einsetzen nicht bestätigt");
                    Assert((await host.ChangeWorkpieceAsync("Remove", 0)).Applied && !host.Snapshot.Latest!.Present, "Entnehmen nicht bestätigt");
                }
                await Task.Delay(Math.Max(1, 16000 - (int)clock.ElapsedMilliseconds));
                Assert(host.Snapshot.IsActive && !host.Snapshot.HardKilled && host.Snapshot.Error == null, "Verbindung an alter Test-/Prozessfrist beendet: " + host.Snapshot.Error);
                await host.StopAsync(); await running;
                var final = host.Snapshot;
                Assert(final.Passed && final.CleanupConfirmed && final.EndReason == "Disconnected" && !final.HardKilled, "Normales Trennen nicht bestätigt: " + final.Error);
                Assert(LiveSessionPolicy.CanRestoreOwnFreeze(final), "Eigene interaktive Sitzung kann Default nicht freigeben");
                var data = (System.Collections.Generic.Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(report));
                Assert((int)data["pings"] >= 10 && (int)data["changes"] == 4, "Lebenszeichen/Bedienbefehle fehlen im Kindprozess");
                await host.RunAsync(new() { ProbePath = exe, WorkingDirectory = folder, MapPath = map, ProfilePath = profile,
                    PlantPath = plant, ReportPath = Path.Combine(folder, "cpu-stop.json"), Limits = InteractiveLimits(),
                    RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true });
                var stopped = host.Snapshot;
                Assert(stopped.State == LiveProcessState.Faulted && !stopped.Passed && stopped.CpuBeforeDisconnect == "Stop / SingleStep_C",
                    "CPU-STOP darf nicht als erfolgreicher Transport erscheinen");
                Assert(stopped.CleanupConfirmed && !stopped.HardKilled && stopped.FreezeOwnershipConfirmed,
                    "Eigene Synchronisierung nach CPU-STOP nicht erkannt");
                Assert(LiveSessionPolicy.CanRestoreOwnFreeze(stopped), "Sauber beendeter CPU-STOP überspringt Default-Freigabe");
            }
            finally
            {
                if (host.Snapshot.IsActive) await host.StopAsync();
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        });
    }
}
