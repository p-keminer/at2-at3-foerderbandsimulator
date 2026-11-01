using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Plant.Core;
using Siemens.Simatic.Simulation.Runtime;

// Separate maintenance command; never referenced by the simulator or LiveProbe.
internal static class Program
{
    internal const string InstanceName = "AT2_Kopplung";
    private static readonly string SdkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), @"Siemens\PLCSIMADV\API\5.0\Siemens.Simatic.Simulation.Runtime.Api.x64.dll");
    internal static bool NeedsChange(string name, string state, string mode, bool defaultEvents, string expectedInstance = InstanceName)
    {
        if (string.IsNullOrWhiteSpace(expectedInstance) || name != expectedInstance || (state != "Run" && state != "Freeze" && state != "Stop") || defaultEvents ||
            (mode != "SingleStep_P" && mode != "SingleStep_C" && mode != "Default"))
            throw new InvalidOperationException("Abbruch: Instanz/Zustand/Modus entspricht nicht der freigegebenen Rueckstellung.");
        return mode != "Default";
    }
    internal static bool IsRecovered(string initialState, string state, string mode) =>
        mode == "Default" && state == (initialState == "Stop" ? "Stop" : "Run");
    private static string RequireArguments(string[] args)
    {
        // Explicit instance name, fixed operation. No discovery, CPU creation or implicit approval.
        if ((args.Length != 4 && args.Length != 6) || args[0] != "--freigabe-runtime" ||
            args[1] != "--freigabe-default-fortlauf" || args[2] != "--report" ||
            string.IsNullOrWhiteSpace(args[3]) || args[3].StartsWith("--"))
            throw new ArgumentException("GESPERRT: --freigabe-runtime --freigabe-default-fortlauf --report <neue.json> erforderlich.");
        if (args.Length == 6 && (args[4] != "--instance" || string.IsNullOrWhiteSpace(args[5]) || args[5].Length > 128 || args[5].Any(char.IsControl)))
            throw new ArgumentException("Gültiger lokaler CPU-Instanzname erforderlich.");
        return Path.GetFullPath(args[3]);
    }
    private static int SelfTest()
    {
        int checks = 0;
        void Check(bool value) { checks++; if (!value) throw new Exception("Offlinepruefung fehlgeschlagen."); }
        void Reject(Action action) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Check(rejected); }
        Check(NeedsChange(InstanceName, "Run", "SingleStep_P", false));
        Check(NeedsChange(InstanceName, "Freeze", "SingleStep_P", false));
        Check(NeedsChange(InstanceName, "Run", "SingleStep_C", false));
        Check(NeedsChange(InstanceName, "Freeze", "SingleStep_C", false));
        Check(!NeedsChange(InstanceName, "Run", "Default", false));
        Check(NeedsChange("AT3_Uebung", "Freeze", "SingleStep_C", false, "AT3_Uebung"));
        Reject(() => NeedsChange("AT2_Kopplung", "Freeze", "SingleStep_C", false, "AT3_Uebung"));
        Check(RequireArguments(new[] { "--freigabe-runtime", "--freigabe-default-fortlauf", "--report", "x.json", "--instance", "AT3_Uebung" }).EndsWith("x.json"));
        Reject(() => RequireArguments(new[] { "--freigabe-runtime", "--freigabe-default-fortlauf", "--report", "x.json", "--instance", "" }));
        Reject(() => NeedsChange("Other", "Run", "SingleStep_P", false));
        Check(NeedsChange(InstanceName, "Stop", "SingleStep_P", false));
        Check(NeedsChange(InstanceName, "Stop", "SingleStep_C", false));
        Check(!NeedsChange(InstanceName, "Stop", "Default", false));
        Check(IsRecovered("Stop", "Stop", "Default"));
        Check(!IsRecovered("Stop", "Run", "Default"));
        Check(!IsRecovered("Stop", "Stop", "SingleStep_C"));
        Check(IsRecovered("Freeze", "Run", "Default"));
        Check(!IsRecovered("Freeze", "Stop", "Default"));
        Reject(() => NeedsChange(InstanceName, "Off", "Default", false));
        Reject(() => NeedsChange(InstanceName, "Run", "SingleStep_CP", false));
        Reject(() => NeedsChange(InstanceName, "Run", "SingleStep_P", true));
        Reject(() => RequireArguments(Array.Empty<string>()));
        Reject(() => RequireArguments(new[] { "--freigabe-runtime", "--report", "x.json" }));
        Reject(() => RequireArguments(new[] { "--freigabe-runtime", "--freigabe-default-fortlauf", "--report", "--bad" }));
        Check(RequireArguments(new[] { "--freigabe-runtime", "--freigabe-default-fortlauf", "--report", "x.json" }).EndsWith("x.json"));
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => (a.GetName().Name ?? "").StartsWith("Siemens.")));
        Console.WriteLine($"PASS: {checks} Offlinepruefungen; keine Siemens-Assembly geladen.");
        return 0;
    }
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" })) return SelfTest();
            string path = RequireArguments(args);
            if (Path.GetExtension(path) != ".json") throw new ArgumentException("JSON-Bericht erforderlich.");
            var version = FileVersionInfo.GetVersionInfo(SdkPath);
            if (version.ProductVersion != "5.0.0.2") throw new InvalidOperationException("Nur lokal bestaetigtes V5.0 Update 2 erlaubt.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // CreateNew prevents overwriting evidence; checkpoints survive a blocked native call.
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var report = new Dictionary<string, object?> {
                ["instance"] = args.Length == 6 ? args[5] : InstanceName, ["operation"] = "ExplicitRestoreDefault",
                ["utcStarted"] = DateTime.UtcNow.ToString("o"), ["sdkProductVersion"] = version.ProductVersion,
                ["sdkFileVersion"] = version.FileVersion, ["passed"] = false,
                ["setterAttempted"] = false, ["setterReturned"] = false, ["cleanupConfirmed"] = false,
                ["note"] = "Einmalig freigegebene Rueckstellung. Kein E/A-Zugriff, kein RUN/STOP, kein RunToNextSyncPoint, kein Kopplungstest. Default kann fortlaufende CPU-Ausfuehrung freigeben."
            };
            void Save(string stage)
            {
                report["stage"] = stage; report["utcUpdated"] = DateTime.UtcNow.ToString("o");
                byte[] bytes = Encoding.UTF8.GetBytes(Json.Write(report));
                file.Position = 0; file.Write(bytes, 0, bytes.Length); file.SetLength(bytes.Length); file.Flush(true);
                Console.WriteLine(stage);
            }
            Save("ApprovedBeforeSdkLoad");
            ResolveEventHandler resolver = (_, request) => new AssemblyName(request.Name).Name == "Siemens.Simatic.Simulation.Runtime.Api.x64" ? Assembly.LoadFrom(SdkPath) : null;
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try { NativeRestore.Run(report, Save); }
            catch (Exception e) { report["error"] = e.ToString(); report["passed"] = false; Save("Failed"); }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
            Console.WriteLine("Bericht: " + path);
            return Equals(report["passed"], true) ? 0 : 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 2; }
    }
}

internal static class NativeRestore
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(Dictionary<string, object?> report, Action<string> save)
    {
        IInstance? instance = null;
        try
        {
            save("OpeningExistingInstance");
            instance = SimulationRuntimeManager.CreateInterface((string)report["instance"]!);
            if (instance == null) throw new InvalidOperationException("Instanz nicht vorhanden; keine CPU wird erzeugt.");
            string name = instance.Name;
            string beforeState = instance.OperatingState.ToString();
            string beforeMode = instance.OperatingMode.ToString();
            bool defaultEvents = instance.IsSendSyncEventInDefaultModeEnabled;
            report["observedName"] = name; report["beforeState"] = beforeState;
            report["beforeMode"] = beforeMode; report["defaultSyncEvents"] = defaultEvents;
            save("BeforeChange");
            if (Program.NeedsChange(name, beforeState, beforeMode, defaultEvents, (string)report["instance"]!))
            {
                report["setterAttempted"] = true; save("SettingDefault");
                // Installed V5 manual p.96 and p.235. Exactly one setter; no retry/resume fallback.
                instance.OperatingMode = EOperatingMode.Default;
                report["setterReturned"] = true; save("SetterReturned");
            }
            var timer = Stopwatch.StartNew();
            bool confirmed;
            do
            {
                string mode = instance.OperatingMode.ToString();
                string state = instance.OperatingState.ToString();
                report["afterMode"] = mode; report["afterState"] = state;
                report["defaultAndRunObserved"] = mode == "Default" && state == "Run";
                report["defaultAndStopObserved"] = mode == "Default" && state == "Stop";
                confirmed = Program.IsRecovered(beforeState, state, mode);
                if (confirmed) break;
                Thread.Sleep(50);
            } while (timer.ElapsedMilliseconds < 2000);
            report["defaultStateConfirmed"] = confirmed; save("AfterReadback");
            if (!confirmed) throw new TimeoutException("Default mit erwartetem CPU-Zustand (STOP bleibt STOP) nicht bestaetigt; kein weiterer Setter oder CPU-Befehl.");
        }
        finally
        {
            try { save("DisposingInterface"); }
            finally
            {
                try { instance?.Dispose(); report["cleanupConfirmed"] = true; }
                catch (Exception e) { report["cleanupError"] = e.ToString(); throw; }
                finally { save("InterfaceDisposeFinished"); }
            }
        }
        report["passed"] = true;
        report["stateAfterDispose"] = "Nicht erneut verbunden; letzter beobachteter Zustand in afterState/afterMode.";
        save("Completed");
    }
}
