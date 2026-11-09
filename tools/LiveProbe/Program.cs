using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Plant.Adapter;
using Plant.Core;

internal static class Program
{
    private static readonly string DefaultSdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), @"Siemens\PLCSIMADV\API\5.0\Siemens.Simatic.Simulation.Runtime.Api.x64.dll");
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        // No execution by the agent during preparation. A user attempt on 2026-11-01
        // reached the first sync wait and timed out, with no E/A or continue calls.
        // Runtime loading is isolated from Main's JIT and happens only after both explicit flags.
        if (!args.Contains("--freigabe-runtime") || !args.Contains("--freigabe-cpu-schritte"))
        {
            Console.Error.WriteLine("GESPERRT. Nur nach gesonderter menschlicher Livefreigabe verwenden. Erforderlich: --freigabe-runtime --freigabe-cpu-schritte --map <JSON> --partition <ID> --report <JSON>. Minimalprofil: --minimal-two-signal --confirm-tia-minimal (Start-MinimalProbe.ps1 mit Prozess-Timeout verwenden).");
            return 2;
        }
        try
        {
            var command = ProbeArguments.Parse(args);
            var map = Json.Read<SignalMap>(File.ReadAllText(command.Required("--map")));
            DigitalCycleProfile? digital = null;
            if (command.Digital) { digital = Json.Read<DigitalCycleProfile>(File.ReadAllText(command.Required("--profile"))); digital.RequireMap(map); }
            MinimalProbeMap? minimal = null;
            if (command.Minimal) minimal = MinimalProbeMap.FromConfirmedSignals(map); else if (!command.Digital) map.RequireLiveValid();
            uint partition = command.CycleControlPoint ? 0u : uint.Parse(command.Required("--partition")); int steps = int.Parse(command.Optional("--steps", "1"));
            Numbers.Require(partition < 0xffff, "Bestätigte P-Partition erforderlich; kein Zykluskontrollpunkt.");
            Numbers.Require(steps >= 1 && steps <= (command.Digital ? 50000 : 10), "Schrittgrenze ungültig.");
            string report = Path.GetFullPath(command.Required("--report"));
            Numbers.Require(!File.Exists(report), "Berichtsdatei existiert bereits; neuen Namen verwenden.");
            string sdk = Path.GetFullPath(command.Optional("--sdk", DefaultSdk));
            Numbers.Require(File.Exists(sdk) && Path.GetFileName(sdk) == "Siemens.Simatic.Simulation.Runtime.Api.x64.dll" &&
                ProbeArguments.SupportedSdkVersion(FileVersionInfo.GetVersionInfo(sdk).ProductVersion),
                "Originale lokale x64-V5.0-DLL (5.0.0.0 oder geprüftes Update 2: 5.0.0.2) erforderlich; kein Runtimefallback.");
            Numbers.Require(!command.CycleControlPoint || ProbeArguments.SupportedCycleSdkVersion(FileVersionInfo.GetVersionInfo(sdk).ProductVersion, FileVersionInfo.GetVersionInfo(sdk).FileVersion),
                "Separates C-Profil verlangt beobachtetes V5.0 Update 2 / 50.43.04.00; keine fremde Ereignis-ID-/Zeitkonvention übernehmen.");
            // No proprietary DLL is copied. This handler is installed only in this future approved launcher.
            ResolveEventHandler resolver = (_, request) => new AssemblyName(request.Name).Name == "Siemens.Simatic.Simulation.Runtime.Api.x64" ? Assembly.LoadFrom(sdk) : null;
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try { return digital != null ? RunDigitalApproved(map, digital, command, report, sdk).GetAwaiter().GetResult() : minimal != null ? RunMinimalApproved(minimal, partition, report, sdk, command.CycleControlPoint) : RunApproved(map, partition, steps, report).GetAwaiter().GetResult(); }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> RunDigitalApproved(SignalMap map, DigitalCycleProfile profile, ProbeArguments command, string report, string sdk)
    {
        var configuration = Json.LoadProfile(command.Required("--plant"));
        var limits = new DigitalRunLimits { MaxAdvances = int.Parse(command.Optional("--steps", "12"), CultureInfo.InvariantCulture),
            MaxVirtualSeconds = double.Parse(command.Optional("--max-virtual-seconds", "1"), CultureInfo.InvariantCulture),
            WallTimeoutSeconds = int.Parse(command.Optional("--wall-seconds", "15"), CultureInfo.InvariantCulture),
            RequireStationary = !command.Has("--allow-movement"), RequireCompletedTransport = command.Has("--require-transport"), InteractiveSession = command.Has("--interactive") };
        limits.Validate();
        var workpieceCommands = command.Has("--stream") ? new WorkpieceCommandChannel() : null;
        var potentiometer = profile.Analog == null ? null : new PotentiometerControl(profile.Analog);
        using var control = new InteractiveControlSession(workpieceCommands, limits.InteractiveSession, potentiometer: potentiometer);
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; control.RequestStop(); };
        Console.CancelKeyPress += onCancel;
        // Only the owned WPF stream can submit bounded plant commands. EOF/STOP still cancels.
        if (workpieceCommands != null) _ = Task.Run(() =>
        {
            try
            {
                // .NET Framework's redirected stdin may start with a UTF-8 BOM.
                using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true), true);
                string? line;
                while ((line = input.ReadLine()) != null)
                {
                    control.Accept(line);
                    if (control.StopRequested) break;
                }
            }
            catch (Exception e) { control.Fail(e.Message); Console.Error.WriteLine("Prüflings-Steuerkanal: " + e.Message); }
            finally { control.EndInput(); }
        });
        try
        {
            var result = await DigitalIntegrationRun.ExecuteAsync(new Plant.Siemens.V5.V5EndpointFactory(profile), map, profile,
                configuration, limits, new(true, true), control.Token,
                command.Has("--stream") ? sample => { Console.WriteLine("SAMPLE " + Json.Write(DigitalProgress.From(sample, profile.UsesAnalog))); Console.Out.Flush(); } : null,
                workpieceCommands, () => control.Failure, potentiometer);
            // The run has ended and cleanup has been attempted. A large report can take
            // longer than the stream freshness deadline; the process deadline still applies.
            if (command.Has("--stream")) { Console.WriteLine("FINALIZING"); Console.Out.Flush(); }
            var version = FileVersionInfo.GetVersionInfo(sdk);
            Json.SaveAtomic(report, Json.WriteReport(new { sdkProductVersion = version.ProductVersion, sdkFileVersion = version.FileVersion,
                map, profile, configuration, limits, result }));
            Console.WriteLine($"SPS-Test: {(result.Passed ? "PASS" : "FAIL")}; Punkte={result.Samples.Count}; Fortschaltungen={result.ContinueCalls}; Transport={result.TransportCompleted}; SPS-MesswertGeprüft={result.PlcMeasurementVerified}; Cleanup={result.CleanupConfirmed}; Bericht={report}");
            return result.Passed ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMinimalApproved(MinimalProbeMap map, uint partition, string report, string sdk, bool cycleControlPoint)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var synchronization = cycleControlPoint ? MinimalSynchronization.CycleControlPoint : MinimalSynchronization.ProcessImagePartition;
            var result = MinimalProbe.Run(new Plant.Siemens.V5.V5MinimalEndpointFactory(synchronization), map, new(true, true),
                new MinimalProbeOptions { InputPartition = partition, TiaMinimalProgramConfirmed = true,
                    Synchronization = synchronization, Ob1AutomaticImageConfirmed = cycleControlPoint }, cancel.Token);
            var version = FileVersionInfo.GetVersionInfo(sdk);
            Json.SaveAtomic(report, Json.Write(new { utc = DateTime.UtcNow, sdkProductVersion = version.ProductVersion,
                sdkFileVersion = version.FileVersion, map, result,
                note = "Nur dieser Zweibitversuch. Keine Motorbeobachtung, Hall-/Mess-/WinCC-Abnahme oder Freigabe des WPF-Livemodus." }));
            Console.WriteLine($"Minimaltest: {(result.Passed ? "PASS" : "FAIL")}; Modus={result.SynchronizationMode}; Punkte={result.Samples.Count}; Fortschaltungen={result.ContinueCalls}; Cleanup={result.CleanupConfirmed}; Bericht={report}");
            return result.Passed ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> RunApproved(SignalMap map, uint partition, int steps, string report)
    {
        // Future first test is stationary; the initial workpiece supplies the left sensor level.
        var model = new ExternalPlantSimulation(new PlantConfiguration());
        var options = new CouplingOptions { ConfirmedInputPartitions = new[] { partition }, InputAdoptionReviewed = true };
        var controller = new PlcCouplingController(new StationaryFactory(), model, options);
        var captures = new List<CouplingSnapshot>(); string? error = null; CouplingSnapshot? failureAtStop = null;
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            await controller.ConnectAsync(map, new(true, true), cancel.Token);
            for (int i = 0; i < steps; i++)
            {
                var sample = await controller.StepAsync(cancel.Token); captures.Add(sample);
                if (sample.Applied.MotorLeft || sample.Applied.MotorRight)
                    throw new InvalidOperationException("Motoranforderung im stationären Ersttest: lokal anhalten, CPU separat prüfen.");
            }
        }
        catch (Exception e) { error = e.ToString(); failureAtStop = controller.Snapshot; }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            try { using var closeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await controller.DisconnectAsync(closeLimit.Token); }
            catch (Exception e) { error += "\nBereinigung nicht bestätigt: " + e; }
            Json.SaveAtomic(report, Json.Write(new { snapshots = captures, failureAtStop, localModelTimeNs = model.TimeNs, final = controller.Snapshot, error,
                note = "Diagnose dieses einzelnen freigegebenen Versuchs. Keine automatische vollständige Siemens-Liveabnahme; CPU-STOP/Freeze/RUN separat prüfen." }));
        }
        return error == null ? 0 : 1;
    }
    private sealed class StationaryFactory : IPlcEndpointFactory
    {
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation) =>
            new StationaryEndpoint(new Plant.Siemens.V5.V5EndpointFactory().OpenExisting(map, authorization, cancellation));
    }
    private sealed class StationaryEndpoint : IPlcEndpoint
    {
        private readonly IPlcEndpoint inner;
        public StationaryEndpoint(IPlcEndpoint inner) { this.inner = inner; }
        public string InstanceName => inner.InstanceName;
        public EndpointEvidence Evidence => inner.Evidence;
        public event Action<SyncNotification> SyncPoint { add => inner.SyncPoint += value; remove => inner.SyncPoint -= value; }
        public event Action<string> Invalidated { add => inner.Invalidated += value; remove => inner.Invalidated -= value; }
        public void BeginSingleStepInputs() => inner.BeginSingleStepInputs();
        public PlcReadback ReadOutputs(ClockGrant grant)
        {
            var read = inner.ReadOutputs(grant);
            if (read.Actuators.MotorLeft || read.Actuators.MotorRight)
                throw new InvalidOperationException("Motorbefehl im stationären Ersttest: keine Eingänge schreiben und CPU nicht erneut freigeben.");
            return read;
        }
        public void WriteInputs(SensorFrame frame) => inner.WriteInputs(frame);
        public void ContinueToNextSyncPoint() => inner.ContinueToNextSyncPoint();
        public void Dispose() => inner.Dispose();
    }
}
