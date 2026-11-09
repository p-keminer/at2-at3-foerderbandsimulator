using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CSharp;
using Plant.Adapter;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static void RunAnalogHostTests()
    {
        AsyncTest("AN06", "Echter Prozesskanal für laufendes Potentiometer; fehlende oder falsche Analogdaten werden abgelehnt", async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "AT2AT3-SiemensFree-Analog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            const string source = """
using System; using System.IO; using System.Text; using System.Threading; using System.Collections.Generic; using System.Web.Script.Serialization;
class AnalogFixture {
 static volatile bool stop; static int raw;
 static int Main(string[] args) {
  Console.OutputEncoding=new UTF8Encoding(false); string report=args[Array.IndexOf(args,"--report")+1]; string mode=Path.GetFileNameWithoutExtension(report);
  new Thread(()=>{ var reader=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true),true); string line;
   while((line=reader.ReadLine())!=null) {
    if(line=="STOP") { stop=true; return; }
    if(line.StartsWith("POTI ")) Interlocked.Exchange(ref raw,(int)Math.Round(double.Parse(line.Substring(5),System.Globalization.CultureInfo.InvariantCulture)*27648/10));
   } stop=true;
  }) { IsBackground=true }.Start();
  var json=new JavaScriptSerializer(); int n=0;
  while(!stop && n<1000) {
   n++; int value=Interlocked.CompareExchange(ref raw,0,0);
   var frame=new Dictionary<string,object> { {"SchemaVersion",3},{"AnalogMode",mode!="wrong"},{"Sequence",n},{"TimeNs",(n-1)*1000000L},{"Position",50},{"Length",120},{"BeltMm",42},{"MotorRight",value>0},{"MotorVelocity",value/27648.0*100},{"ObjectVelocity",value/27648.0*100} };
   if(mode!="missing") {frame["PotiRaw"]=value; frame["SpeedCommandRaw"]=value;}
   Console.WriteLine("SAMPLE "+json.Serialize(frame)); Console.Out.Flush(); Thread.Sleep(20);
  }
  Console.WriteLine("FINALIZING"); Console.Out.Flush();
  File.WriteAllText(report,json.Serialize(new { fixture=true,result=new { Profile="AT2_Kopplung_9_digital_bits_SingleStep_C",InteractiveSession=true,Passed=true,CleanupConfirmed=true,EndReason="Disconnected",BeforeDisconnect=new {InstanceName="AT2_Kopplung",OperatingState="Freeze",OperatingMode="SingleStep_C"} } })); return 0;
 }
}
""";
            string exe = Path.Combine(folder, "AT2AT3.V5.LiveProbe.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters(new[] { "System.dll", "System.Web.Extensions.dll" }, exe) { GenerateExecutable = true, CompilerOptions = "/platform:x64 /optimize+" };
                var compiled = compiler.CompileAssemblyFromSource(options, source);
                Assert(!compiled.Errors.HasErrors, string.Join("; ", compiled.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }
            string map = Path.Combine(folder, "map.json"), profile = Path.Combine(folder, "profile.json"), plant = Path.Combine(folder, "plant.json");
            Json.SaveAtomic(map, Json.Write(AnalogMap())); Json.SaveAtomic(profile, Json.Write(AnalogProfile())); Json.SaveAtomic(plant, Json.Write(new PlantConfiguration { InitialPresent = false }));
            foreach (string mode in new[] { "normal", "missing", "wrong" })
            {
                var host = new LiveProcessHost();
                var running = host.RunAsync(new LiveProcessRequest { ProbePath = exe, WorkingDirectory = folder, MapPath = map, ProfilePath = profile, PlantPath = plant, ReportPath = Path.Combine(folder, mode + ".json"),
                    RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true, Limits = new DigitalRunLimits { InteractiveSession = true, RequireStationary = false, MaxAdvances = 2, MaxVirtualSeconds = 0.01, WallTimeoutSeconds = 5 } });
                try
                {
                    for (int i = 0; i < 100 && host.Snapshot.State == LiveProcessState.Connecting; i++) await Task.Delay(20);
                    if (mode == "normal")
                    {
                        Assert(host.Snapshot.State == LiveProcessState.Streaming && host.Snapshot.Latest?.AnalogMode == true, "Analoger Prozessstart");
                        Assert(host.TryGetStoppedBeltDistance(out double distance) && distance == 42, "Markierungsnullpunkt benötigt frischen bestätigten Bandweg");
                        foreach (double volts in new[] { 5d, 10 })
                        {
                            await host.SetPotentiometerAsync(volts);
                            for (int i = 0; i < 100 && host.Snapshot.Latest?.PotiRaw != Analog.InputRaw(volts); i++) await Task.Delay(20);
                            var sample = host.Snapshot.Latest!;
                            Assert(sample.PotiRaw == Analog.InputRaw(volts) && sample.SpeedCommandRaw == sample.PotiRaw, "Potentiometer oder AQ im Prozesskanal verloren");
                            Assert(!host.TryGetStoppedBeltDistance(out _), "Bewegter Zustand erlaubt Nullsetzen der Bandmarkierung");
                        }
                    }
                }
                finally { await host.StopAsync(); await running; }
                Assert(!host.TryGetStoppedBeltDistance(out _), "Historischer oder ungültiger Zustand erlaubt Nullsetzen der Bandmarkierung");
                Assert(mode == "normal" ? host.Snapshot.Passed : !host.Snapshot.Passed && host.Snapshot.State == LiveProcessState.Faulted,
                    "Provenienz-/Vollständigkeitsfehler wurde nicht verworfen: " + mode + " " + host.Snapshot.Detail);
            }
        });
    }
}
