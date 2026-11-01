using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Wpf
{
    internal enum LiveProcessState { Disconnected, Connecting, Streaming, Stopping, Completed, Faulted, Finalizing }
    internal sealed class LiveProcessRequest
    {
        public string ProbePath { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string MapPath { get; set; } = "";
        public string ProfilePath { get; set; } = "";
        public string PlantPath { get; set; } = "";
        public string ReportPath { get; set; } = "";
        public DigitalRunLimits Limits { get; set; } = new();
        public bool RuntimeApproved { get; set; }
        public bool CpuStepsApproved { get; set; }
        public bool TiaConfirmed { get; set; }
    }
    internal sealed record LiveProcessSnapshot(LiveProcessState State, string Detail,
        DigitalProgress? Latest = null, string ReportPath = "", bool IsActive = false,
        bool Passed = false, bool TransportCompleted = false, bool CleanupConfirmed = false,
        string CpuBeforeDisconnect = "Ungeprüft", int? ExitCode = null, bool HardKilled = false,
        bool StopRequested = false, string? Error = null);

    // This host never references Siemens assemblies. Only an explicitly authorized child
    // uses the existing native adapter. A blocked native call cannot freeze the WPF thread.
    // Kill applies only to our owned child, NEVER to a CPU/Siemens process or CPU mode.
    internal sealed class LiveProcessHost
    {
        private readonly object gate = new();
        private LiveProcessSnapshot current = new(LiveProcessState.Disconnected, "Keine Siemens-Verbindung gestartet.");
        private TaskCompletionSource<string>? stopSignal;
        private TaskCompletionSource<bool>? completion;
        private string? protocolError;
        private long lastSampleTicks;
        private bool finalizing;
        private int generation;
        private bool childTerminationUnconfirmed;
        private readonly Queue<DigitalProgress> samples = new();
        private const int MaxReportBytes = 128 * 1024 * 1024;
        private const int MaxQueuedSamples = 50001; // 50000 allowed advances plus the initial point.
        private const int MaxLineChars = 64 * 1024;
        public LiveProcessSnapshot Snapshot
        {
            get { lock (gate) return current with { Latest = Copy(current.Latest) }; }
        }
        private static DigitalProgress? Copy(DigitalProgress? value) => value == null ? null : new DigitalProgress
        {
            SchemaVersion = value.SchemaVersion, TimeNs = value.TimeNs, Sequence = value.Sequence,
            Position = value.Position, Length = value.Length, MotorMm = value.MotorMm, BeltMm = value.BeltMm,
            MotorVelocity = value.MotorVelocity, ObjectVelocity = value.ObjectVelocity, Present = value.Present,
            LeftIdeal = value.LeftIdeal, OpticalIdeal = value.OpticalIdeal, RightIdeal = value.RightIdeal,
            LeftDetected = value.LeftDetected, OpticalDetected = value.OpticalDetected, RightDetected = value.RightDetected,
            Left = value.Left, Optical = value.Optical, Right = value.Right, Hall = value.Hall,
            MotorRight = value.MotorRight, MotorLeft = value.MotorLeft, LampGreen = value.LampGreen,
            GeneratedRisingEdges = value.GeneratedRisingEdges
        };
        public DigitalProgress[] DrainSamples()
        {
            DigitalProgress[] pending;
            lock (gate)
            {
                pending = samples.ToArray(); samples.Clear();
            }
            // Accepted samples are never mutated by the host. Detach the bounded queue
            // quickly, then copy outside the stream/watchdog lock to keep input flowing.
            for (int i = 0; i < pending.Length; i++) pending[i] = Copy(pending[i])!;
            return pending;
        }
        private void Update(Func<LiveProcessSnapshot, LiveProcessSnapshot> change)
        { lock (gate) current = change(current); }
        private void Fault(string error, int? expectedGeneration = null)
        {
            lock (gate)
            {
                if (expectedGeneration.HasValue && expectedGeneration != generation) return;
                protocolError ??= error;
                current = current with { State = LiveProcessState.Faulted, Detail = error, Passed = false, Error = protocolError };
                stopSignal?.TrySetResult(error);
            }
        }
        // Windows CRT command-line quoting; no shell evaluation or string-built shell command.
        internal static string QuoteArgument(string text)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in text)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes).Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        private static LiveProcessRequest Prepare(LiveProcessRequest source)
        {
            Numbers.Require(source.RuntimeApproved && source.CpuStepsApproved && source.TiaConfirmed,
                "Manuelle Freigaben für Runtime, CPU-Schritte und TIA-Digitalstand fehlen.");
            source.Limits.Validate();
            var request = new LiveProcessRequest
            {
                ProbePath = Path.GetFullPath(source.ProbePath), WorkingDirectory = Path.GetFullPath(source.WorkingDirectory),
                MapPath = Path.GetFullPath(source.MapPath), ProfilePath = Path.GetFullPath(source.ProfilePath),
                PlantPath = Path.GetFullPath(source.PlantPath), ReportPath = Path.GetFullPath(source.ReportPath),
                Limits = Json.Read<DigitalRunLimits>(Json.Write(source.Limits)), RuntimeApproved = true, CpuStepsApproved = true, TiaConfirmed = true
            };
            Numbers.Require(File.Exists(request.ProbePath) && string.Equals(Path.GetFileName(request.ProbePath), "AT2AT3.V5.LiveProbe.exe", StringComparison.OrdinalIgnoreCase),
                "Getrennter LiveProbe fehlt. Optionalen Siemens-Compilerbuild ausführen.");
            Numbers.Require(Directory.Exists(request.WorkingDirectory), "Arbeitsordner des LiveProbe fehlt.");
            var profile = Json.Read<DigitalCycleProfile>(File.ReadAllText(request.ProfilePath));
            profile.RequireMap(Json.Read<SignalMap>(File.ReadAllText(request.MapPath)));
            profile.ConfigurePlant(Json.LoadProfile(request.PlantPath));
            Numbers.Require(string.Equals(Path.GetExtension(request.ReportPath), ".json", StringComparison.OrdinalIgnoreCase), "JSON-Bericht erforderlich.");
            foreach (string suffix in new[] { "", ".stdout.txt", ".stderr.txt", ".host.json" })
                Numbers.Require(!File.Exists(request.ReportPath + suffix), "Bericht existiert bereits: " + request.ReportPath + suffix);
            Directory.CreateDirectory(Path.GetDirectoryName(request.ReportPath)!);
            return request;
        }
        private static string Arguments(LiveProcessRequest r)
        {
            var args = new List<string> { "--freigabe-runtime", "--freigabe-cpu-schritte", "--digital-plant", "--confirm-tia-digital",
                "--cycle-control-point", "--confirm-ob1-automatic", "--stream", "--map", r.MapPath, "--profile", r.ProfilePath,
                "--plant", r.PlantPath, "--report", r.ReportPath, "--steps", r.Limits.MaxAdvances.ToString(CultureInfo.InvariantCulture),
                "--max-virtual-seconds", r.Limits.MaxVirtualSeconds.ToString(CultureInfo.InvariantCulture),
                "--wall-seconds", r.Limits.WallTimeoutSeconds.ToString(CultureInfo.InvariantCulture) };
            if (!r.Limits.RequireStationary) args.Add("--allow-movement");
            if (r.Limits.RequireCompletedTransport) args.Add("--require-transport");
            return string.Join(" ", args.Select(QuoteArgument));
        }
        public async Task RunAsync(LiveProcessRequest requested)
        {
            // All application/map/limits guards precede Process.Start, not merely native API use.
            var request = Prepare(requested);
            TaskCompletionSource<string> stop; TaskCompletionSource<bool> done;
            int ownerGeneration;
            lock (gate)
            {
                Numbers.Require(!current.IsActive, "Es läuft bereits eine Kopplung. Erst kontrolliert trennen.");
                Numbers.Require(!childTerminationUnconfirmed, "Ende des vorherigen Adapterprozesses unbestätigt. Prozess/CPU manuell prüfen und Anwendung anschließend neu öffnen.");
                stopSignal = stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
                completion = done = new(TaskCreationOptions.RunContinuationsAsynchronously);
                protocolError = null; lastSampleTicks = 0; finalizing = false;
                ownerGeneration = ++generation; samples.Clear();
                current = new(LiveProcessState.Connecting, "Verbinde vorhandene lokale Instanz AT2_Kopplung; C-Profil.",
                    ReportPath: request.ReportPath, IsActive: true);
            }
            var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = request.ProbePath, Arguments = Arguments(request), WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            }, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult(true);
            bool started = false, processExited = false, hardKilled = false, outputCaptured = false;
            int? exitCode = null; string? killError = null;
            Task? stdout = null, stderr = null;
            var clock = Stopwatch.StartNew();
            using var hardDeadline = new CancellationTokenSource();
            Timer? freshness = null;
            try
            {
                if (!process.Start()) throw new InvalidOperationException("LiveProbe-Prozess konnte nicht gestartet werden.");
                started = true;
                _ = process.Handle; // Keep the owned process handle even if a fast child already exited.
                stdout = Pump(process.StandardOutput, request.ReportPath + ".stdout.txt", true, ownerGeneration);
                stderr = Pump(process.StandardError, request.ReportPath + ".stderr.txt", false, ownerGeneration);
                freshness = new Timer(_ =>
                {
                    try { if (process.HasExited) return; }
                    catch (InvalidOperationException) { return; }
                    // The marker and the decision to fault share the same lock. A timer
                    // queued before FINALIZING must not invalidate an accepted marker.
                    lock (gate)
                    {
                        if (current.IsActive && generation == ownerGeneration && !finalizing && lastSampleTicks != 0 &&
                            (Stopwatch.GetTimestamp() - lastSampleTicks) / (double)Stopwatch.Frequency > 5)
                            Fault("Seit mehr als 5 s kein frischer SPS-Snapshot. Modell angehalten; keine automatische Wiederaufnahme.", ownerGeneration);
                    }
                }, null, 250, 250);
                Task hard = Task.Delay(TimeSpan.FromSeconds(request.Limits.WallTimeoutSeconds + 10), hardDeadline.Token);
                Task first = await Task.WhenAny(exited.Task, stop.Task, hard).ConfigureAwait(false);
                if (first == stop.Task && !exited.Task.IsCompleted)
                {
                    Update(s => s with { State = s.State == LiveProcessState.Faulted ? s.State : LiveProcessState.Stopping,
                        Detail = protocolError ?? "Trenne: STOP an eigenen Adapterprozess; CPU wird nicht auf Default gesetzt.", StopRequested = true });
                    Task grace = Task.Delay(5000);
                    try
                    {
                        var send = process.StandardInput.WriteLineAsync("STOP");
                        _ = send.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        await Task.WhenAny(send, Task.Delay(200)).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is IOException || e is InvalidOperationException || e is ObjectDisposedException) { }
                    await Task.WhenAny(exited.Task, grace, hard).ConfigureAwait(false);
                }
                if (!exited.Task.IsCompleted)
                {
                    hardKilled = true;
                    if (hard.IsCompleted) Fault("Harte Prozessfrist erreicht. Beendigung des eigenen Adapterprozesses angefordert; CPU-Zustand und Cleanup unbestätigt.");
                    else Fault("Adapter hat nach STOP nicht innerhalb von 5 s beendet. CPU-Zustand und Cleanup unbestätigt.");
                    try { process.Kill(); } catch (Exception e) { killError = e.Message; }
                    await Task.WhenAny(exited.Task, Task.Delay(2000)).ConfigureAwait(false);
                }
                processExited = exited.Task.IsCompleted;
                if (processExited) exitCode = process.ExitCode;
                if (stdout != null && stderr != null)
                {
                    Task drain = Task.WhenAll(stdout, stderr);
                    _ = drain.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    outputCaptured = await Task.WhenAny(drain, Task.Delay(2000)).ConfigureAwait(false) == drain && drain.Status == TaskStatus.RanToCompletion;
                }
                if (!processExited || !outputCaptured) Fault("Prozessende oder vollständiger Ausgabeabschluss unbestätigt.");
                ReadFinalReport(request.ReportPath, exitCode, hardKilled, outputCaptured);
            }
            catch (Exception e)
            {
                Fault("Liveprozess fehlgeschlagen: " + e.Message);
                if (started && !exited.Task.IsCompleted)
                {
                    hardKilled = true;
                    try { process.Kill(); } catch (Exception kill) { killError = kill.Message; }
                    await Task.WhenAny(exited.Task, Task.Delay(2000)).ConfigureAwait(false);
                    processExited = exited.Task.IsCompleted;
                    if (processExited) exitCode = process.ExitCode;
                }
            }
            finally
            {
                hardDeadline.Cancel(); freshness?.Dispose();
                lock (gate) childTerminationUnconfirmed = started && !processExited;
                Update(s => s with { ExitCode = exitCode, HardKilled = hardKilled,
                    CleanupConfirmed = s.CleanupConfirmed && !hardKilled && outputCaptured && processExited });
                try
                {
                    File.WriteAllText(request.ReportPath + ".host.json", Json.Write(new { utc = DateTime.UtcNow, processExited,
                        exitCode, hardKilled, outputCaptured, killError, elapsedSeconds = clock.Elapsed.TotalSeconds,
                        state = Snapshot with { IsActive = false }, note = "Nur eigener Adapterprozess. Kein automatischer CPU-Zustandswechsel oder Wiederanlauf." }), new UTF8Encoding(false));
                }
                catch (Exception e) { Update(s => s with { State = LiveProcessState.Faulted, Passed = false, Error = e.Message, Detail = "Hostbericht konnte nicht gespeichert werden: " + e.Message }); }
                try { process.Dispose(); } catch (Exception) { }
                Update(s => s with { IsActive = false });
                done.TrySetResult(true);
            }
        }
        public async Task StopAsync()
        {
            Task? done;
            lock (gate)
            {
                if (!current.IsActive) return;
                current = current with { State = current.State == LiveProcessState.Faulted ? current.State : LiveProcessState.Stopping,
                    StopRequested = true, Detail = "Trennen angefordert; letzter Anlagenzustand bleibt sichtbar." };
                stopSignal?.TrySetResult("Benutzer hat Trennen angefordert."); done = completion?.Task;
            }
            // RunAsync owns the 5 s graceful, 2 s process and 2 s drain deadlines.
            if (done != null && await Task.WhenAny(done, Task.Delay(10000)).ConfigureAwait(false) != done)
                Fault("Trennen ist nach 10 s nicht bestätigt. Kein automatischer Wiederanlauf.");
        }
        private async Task Pump(StreamReader reader, string path, bool acceptSamples, int ownerGeneration)
        {
            try
            {
                using var log = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                var buffer = new char[4096]; var line = new StringBuilder(); int logged = 0; bool discardLine = false, logFull = false;
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (!logFull)
                    {
                        int bytes = Encoding.UTF8.GetByteCount(buffer, 0, count);
                        if (logged + bytes > MaxReportBytes) { logFull = true; Fault("Adapterausgabe überschreitet 128 MiB; Kopplung wird beendet.", ownerGeneration); }
                        else { await log.WriteAsync(buffer, 0, count).ConfigureAwait(false); logged += bytes; }
                    }
                    for (int i = 0; i < count; i++)
                    {
                        char c = buffer[i];
                        if (c == '\n')
                        {
                            if (!discardLine && acceptSamples) AcceptLine(line.ToString().TrimEnd('\r'), ownerGeneration);
                            line.Clear(); discardLine = false;
                        }
                        else if (!discardLine)
                        {
                            if (line.Length >= MaxLineChars) { discardLine = true; line.Clear(); Fault("Überlange Adapter-Protokollzeile; Kopplung wird beendet.", ownerGeneration); }
                            else line.Append(c);
                        }
                    }
                }
                if (!discardLine && line.Length > 0 && acceptSamples) AcceptLine(line.ToString().TrimEnd('\r'), ownerGeneration);
            }
            catch (Exception e) { Fault("Adapterausgabe konnte nicht vollständig gelesen werden: " + e.Message, ownerGeneration); throw; }
        }
        private void AcceptLine(string line, int ownerGeneration)
        {
            bool finalMarker = line.StartsWith("FINALIZING", StringComparison.Ordinal);
            if (!finalMarker && !line.StartsWith("SAMPLE ", StringComparison.Ordinal)) return;
            try
            {
                if (finalMarker)
                {
                    lock (gate)
                    {
                        if (!current.IsActive || generation != ownerGeneration) return;
                        Numbers.Require(line == "FINALIZING", "Ungültiger FINALIZING-Abschlussmarker.");
                        Numbers.Require(!finalizing, "Doppelter FINALIZING-Abschlussmarker.");
                        // A native failure can finish before the first sample. The marker
                        // changes presentation/watchdog phase only; the report decides success.
                        finalizing = true;
                        current = current with
                        {
                            State = protocolError != null ? LiveProcessState.Faulted : LiveProcessState.Finalizing,
                            Detail = protocolError ?? "Abschlussbericht wird geschrieben; letzter Anlagenzustand ist historisch. Erfolg noch nicht bestätigt."
                        };
                    }
                    return;
                }
                var sample = Json.Read<DigitalProgress>(line.Substring(7));
                Numbers.Require(sample.SchemaVersion == 1 && sample.Sequence >= 1 && sample.TimeNs >= 0 && sample.GeneratedRisingEdges >= 0,
                    "Ungültige Version/Zeit/Sequenz in Adapterdaten.");
                Numbers.Require(new[] { sample.Position, sample.Length, sample.MotorMm, sample.BeltMm, sample.MotorVelocity, sample.ObjectVelocity }.All(Numbers.Finite) && sample.Length > 0,
                    "Ungültige Modellwerte in Adapterdaten.");
                Numbers.Require(!(sample.MotorRight && sample.MotorLeft), "Gleichzeitige Richtungsbefehle im Adapterdatenstrom.");
                lock (gate)
                {
                    if (!current.IsActive || generation != ownerGeneration) return;
                    Numbers.Require(!finalizing, "SAMPLE nach FINALIZING-Abschlussmarker ist unzulässig.");
                    if (protocolError != null) return;
                    var previous = current.Latest;
                    Numbers.Require(previous == null ? sample.Sequence == 1 && sample.TimeNs == 0 : sample.Sequence == previous.Sequence + 1 && sample.TimeNs > previous.TimeNs,
                        "Fehlender, doppelter oder zeitlich falscher Adapter-Snapshot.");
                    Numbers.Require(samples.Count < MaxQueuedSamples, "Mehr als 50001 ungelesene Adapter-Snapshots; Kopplung wird angehalten.");
                    current = current with { State = current.StopRequested ? LiveProcessState.Stopping : LiveProcessState.Streaming,
                        Detail = "Digitaler SPS-Austausch aktiv; Messwerte-DB nicht angebunden.", Latest = sample };
                    samples.Enqueue(sample);
                    lastSampleTicks = Stopwatch.GetTimestamp();
                }
            }
            catch (Exception e) { Fault("Ungültige Adapterdaten: " + e.Message, ownerGeneration); }
        }
        private void ReadFinalReport(string path, int? exitCode, bool hardKilled, bool outputCaptured)
        {
            if (!File.Exists(path)) { Fault("Kein vollständiger fachlicher Bericht vorhanden. stderr/Hostbericht prüfen; CPU-Zustand unbestätigt."); return; }
            var file = new FileInfo(path);
            if (file.Length > MaxReportBytes) { Fault("Fachlicher Bericht überschreitet 128 MiB."); return; }
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = MaxReportBytes, RecursionLimit = 128 }.DeserializeObject(File.ReadAllText(path)) as IDictionary<string, object>;
                if (root == null || !root.TryGetValue("result", out var resultValue) || !(resultValue is IDictionary<string, object> result))
                    throw new InvalidOperationException("Digitaler Ergebnisabschnitt fehlt.");
                bool Bool(string key) => result.TryGetValue(key, out var value) && value is bool b && b;
                string? Text(string key) => result.TryGetValue(key, out var value) ? value as string : null;
                Numbers.Require(Text("Profile") == "AT2_Kopplung_7_digital_bits_SingleStep_C", "Ergebnis gehört nicht zum digitalen C-Profil.");
                string cpu = "Ungeprüft";
                if (result.TryGetValue("BeforeDisconnect", out var inspectValue) && inspectValue is IDictionary<string, object> inspect)
                {
                    string Field(string key) => inspect.TryGetValue(key, out var v) ? v as string ?? "?" : "?";
                    Numbers.Require(Field("InstanceName") == "AT2_Kopplung", "Falsche Instanz im Abschlussbericht.");
                    cpu = Field("OperatingState") + " / " + Field("OperatingMode");
                }
                bool cleanup = Bool("CleanupConfirmed") && !hardKilled && outputCaptured;
                bool stopped, hasSamples; string? streamError;
                lock (gate) { stopped = current.StopRequested; hasSamples = current.Latest != null; streamError = protocolError; }
                bool frozen = cpu == "Freeze / SingleStep_C";
                string? nativeError = Text("Error");
                string? error = streamError ?? nativeError ?? Text("CleanupError") ?? Text("InspectionError");
                bool passed = Bool("Passed") && exitCode == 0 && cleanup && frozen && hasSamples && error == null;
                if (Bool("Passed") && error == null && !passed)
                    error = "Erfolg nicht bestätigt: gültige Streamdaten, Exitcode 0, Freeze/SingleStep_C und vollständiges Cleanup erforderlich.";
                bool canceled = nativeError != null && (nativeError.Contains("OperationCanceledException") || nativeError.Contains("TaskCanceledException"));
                bool controlledStop = stopped && cleanup && frozen && streamError == null && Text("CleanupError") == null && Text("InspectionError") == null &&
                    exitCode == 1 && Text("EndReason") == "FaultOrCanceled" && canceled;
                string detail = passed ? "Begrenzter digitaler Lauf beendet. " : controlledStop ? "Kopplung kontrolliert getrennt; Lauf abgebrochen. " : "Digitaler Lauf fehlgeschlagen oder unbestätigt. ";
                detail += "Letzter CPU-Zustand: " + cpu + ". Nach Dispose nicht erneut verbunden; kein Default-Reset. Bericht: " + path;
                Update(s => s with { State = passed || controlledStop ? LiveProcessState.Completed : LiveProcessState.Faulted,
                    Detail = detail, Passed = passed, TransportCompleted = Bool("TransportCompleted"), CleanupConfirmed = cleanup,
                    CpuBeforeDisconnect = cpu, Error = error, ExitCode = exitCode, HardKilled = hardKilled });
            }
            catch (Exception e) { Fault("Abschlussbericht ist unvollständig/ungültig: " + e.Message); }
        }
    }
}
