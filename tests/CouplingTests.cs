using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static List<object> calibrationEvidence = new();
    private sealed class EndpointDouble : IPlcEndpoint, IPlcEndpointFactory
    {
        public string InstanceName { get; set; } = "EXAMPLE_LOCAL_ONLY";
        public EndpointEvidence Evidence => EndpointEvidence.TestDouble;
        public event Action<SyncNotification>? SyncPoint;
        public event Action<string>? Invalidated;
        public readonly List<string> Calls = new();
        public readonly List<SensorFrame> Inputs = new();
        public int Opens, Disposals, Continues;
        public bool StaleOutputs, ThrowOnWrite, ThrowOnDispose;
        public Action? AfterRead, AfterWrite, Begin;
        public ActuatorFrame Command = new(true, SpeedCommandRaw: Analog.OutputRaw(5));
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation)
        { authorization.RequireApproved(); cancellation.ThrowIfCancellationRequested(); Opens++; Calls.Add("open"); return this; }
        public void BeginSingleStepInputs() { Calls.Add("begin"); Begin?.Invoke(); }
        public void Emit(long any = 0, long same = 0, uint id = 1, uint count = 1, string? error = null) => SyncPoint?.Invoke(new(id, any, same, count, error));
        public void Break(string why) => Invalidated?.Invoke(why);
        public PlcReadback ReadOutputs(ClockGrant grant)
        {
            Calls.Add("read:" + grant.TimeUs); AfterRead?.Invoke();
            return new(Command with { Sequence = grant.Sequence - (StaleOutputs ? 1 : 0), TimeUs = grant.TimeUs }, 120, 120, false);
        }
        public void WriteInputs(SensorFrame frame)
        {
            Calls.Add("write:" + frame.TimeUs);
            if (ThrowOnWrite) throw new InvalidOperationException("Testdouble: unvollständiger Schreibvorgang");
            Inputs.Add(frame); AfterWrite?.Invoke();
        }
        public void ContinueToNextSyncPoint() { Continues++; Calls.Add("continue"); }
        public void Dispose()
        {
            Disposals++; Calls.Add("dispose");
            if (ThrowOnDispose) throw new InvalidOperationException("Testdouble: Dispose fehlgeschlagen");
        }
        public bool HasSubscribers => SyncPoint != null || Invalidated != null;
    }
    private sealed class PlantDouble : IExternallyTimedPlant
    {
        public long TimeNs { get; private set; }
        public bool Valid = true;
        public readonly List<string> Calls;
        public readonly List<ActuatorFrame> Held = new();
        public Action? OnInvalidate { get; set; }
        public PlantDouble(List<string> calls) { Calls = calls; }
        public void AdvanceTo(long ns, ActuatorFrame held, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Calls.Add("advance:" + ns); TimeNs = ns; Held.Add(held); }
        public SensorFrame Inputs(long sequence) => new(false, false, false, false, 0, Valid, sequence, TimeNs / 1000);
        public void Invalidate() { Valid = false; OnInvalidate?.Invoke(); }
    }
    private static CouplingOptions Options(double timeoutMs = 1000) => new()
    { InputAdoptionReviewed = true, ConfirmedInputPartitions = new uint[] { 1, 2 }, SyncTimeout = TimeSpan.FromMilliseconds(timeoutMs) };
    private static LiveAuthorization FakeApproval => new(true, true); // Applies ONLY to pure testdoubles here.
    private sealed class WaitingFactory : IPlcEndpointFactory
    {
        public readonly ManualResetEventSlim Entered = new();
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation)
        { Entered.Set(); cancellation.WaitHandle.WaitOne(); cancellation.ThrowIfCancellationRequested(); throw new InvalidOperationException("Kein Endpunkt erstellt."); }
    }
    private static async Task MustFail(Task task)
    {
        try { await task; } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is OperationCanceledException || e is TimeoutException) { Assert(true, "Erwarteter Fehler"); return; }
        Assert(false, "Unerwarteter Erfolg");
    }
    private static void AsyncTest(string id, string name, Func<Task> test) => Test(id, name, () => test().GetAwaiter().GetResult());
    private static void RunCouplingTests()
    {
        AsyncTest("S01", "Live-Sperre und ungültige Zuordnung verhindern bereits Factoryzugriff", async () =>
        {
            var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await MustFail(c.ConnectAsync(BoundMap(), new()));
            await MustFail(c.ConnectAsync(SignalMap.Unbound(), FakeApproval));
            Assert(fake.Opens == 0 && c.Snapshot.State == CouplingState.Disconnected, "Sperre initialisiert Endpunkt");
        });
        AsyncTest("S02", "E/A-Reihenfolge und kausales Halten des vorigen Befehls", async () =>
        {
            var fake = new EndpointDouble(); var p = new PlantDouble(fake.Calls); using var c = new PlcCouplingController(fake, p, Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); var first = await c.StepAsync();
            Assert(first.VirtualTimeNs == 0 && p.Held.Count == 0 && first.Evidence == EndpointEvidence.TestDouble && !first.LiveValidated, "Erstversorgung falsch");
            fake.Command = Drive(false); fake.Emit(2000000, 2000000); var next = await c.StepAsync();
            Assert(p.Held.Single().MotorRight && next.Applied.MotorLeft, "Neuer Ausgang rückwirkend integriert");
            Assert(fake.Calls.SequenceEqual(new[] { "open", "begin", "read:0", "write:0", "continue", "advance:2000000", "read:2000", "write:2000", "continue" }), "Austauschreihenfolge falsch");
            Assert(next.Quality == LinkQuality.Unverified && next.VirtualTimeNs == 2000000, "Testdouble als Liveprüfung markiert");
        });
        AsyncTest("S03", "Nanosekunden und Restschritte verwenden den vorhandenen Anlagenkern", async () =>
        {
            var fake = new EndpointDouble(); var p = new ExternalPlantSimulation(new()); using var c = new PlcCouplingController(fake, p, Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await c.StepAsync();
            fake.Emit(3125125, 3125125); await c.StepAsync();
            Assert(p.TimeNs == 3125125 && c.Snapshot.VirtualTimeNs == 3125125, "Nanosekunden verloren");
            Equal(p.Snapshot.MotorMm, 50 * .003125125, 1e-9);
            Assert(fake.Inputs.Last().TimeUs == 3125, "Vertrag projiziert Zeit falsch");
        });
        AsyncTest("S04", "Gleicher virtueller Zeitpunkt verschiedener Partitionen ohne Doppelbewegung", async () =>
        {
            var fake = new EndpointDouble(); var p = new PlantDouble(fake.Calls); using var c = new PlcCouplingController(fake, p, Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await c.StepAsync();
            fake.Emit(id: 2); await c.StepAsync(); Assert(p.Held.Count == 0 && c.Snapshot.Sequence == 2, "Nullzeitpunkt erzeugt Modellzeit");
            fake.Emit(1000000, 1000000, 1); await c.StepAsync();
            fake.Emit(0, 1000000, 2); await c.StepAsync(); Assert(p.Held.Count == 1 && c.Snapshot.Sequence == 4, "Doppelintegration");
        });
        AsyncTest("S05", "Zusammengefasste, doppelte und inkonsistente Synchronisationspunkte sperren", async () =>
        {
            foreach (var bad in new[] { new SyncNotification(1, 1000, 1000, 2), new(1, 0, 0, 1), new(1, 1000, 500, 1), new(0xffff, 1000, 1000, 1), new(1, -1, -1, 1), new(1, 100000001, 100000001, 1), new(1, 1, 1, 1, "Timeout") })
            {
                var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
                await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await c.StepAsync();
                fake.Emit(bad.SinceAnyNs, bad.SinceSameNs, bad.InputPartition, bad.Count, bad.Error); await MustFail(c.StepAsync());
                Assert(fake.Continues == 1 && !c.Snapshot.Applied.Valid && c.Snapshot.State == CouplingState.Faulted, "Fehlerpunkt fortgeschaltet");
            }
        });
        AsyncTest("S06", "Veralteter Aktorframe wird nicht geschrieben oder fortgeschaltet", async () =>
        {
            var fake = new EndpointDouble { StaleOutputs = true }; using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await MustFail(c.StepAsync());
            Assert(fake.Inputs.Count == 0 && fake.Continues == 0 && c.Snapshot.State == CouplingState.Faulted, "Stale-Frame übernommen");
        });
        AsyncTest("S07", "Ausbleibender Synchronisationspunkt wird stale statt Wandzeitfortschritt", async () =>
        {
            var fake = new EndpointDouble(); var p = new PlantDouble(fake.Calls); using var c = new PlcCouplingController(fake, p, Options(30));
            await c.ConnectAsync(BoundMap(), FakeApproval); await MustFail(c.StepAsync());
            Assert(c.Snapshot.State == CouplingState.Stale && p.TimeNs == 0 && !p.Valid && fake.Continues == 0, "Wandzeit trieb Modell");
            await MustFail(c.StepAsync());
        });
        AsyncTest("S08", "Abbruch beim Warten cancelt Schritt ohne späteren Wiederanlauf", async () =>
        {
            var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval);
            using var cancel = new CancellationTokenSource(); Task waiting = c.StepAsync(cancel.Token); cancel.Cancel();
            await MustFail(waiting); fake.Emit(); await MustFail(c.StepAsync());
            Assert(fake.Continues == 0 && !c.Snapshot.Applied.Valid, "Abgebrochener Schritt nachgeliefert");
        });
        AsyncTest("S09", "Verbindungsabbruch beendet wartenden Schritt und erhält Ursache", async () =>
        {
            var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); Task waiting = c.StepAsync(); fake.Break("Verbindung verloren"); await MustFail(waiting);
            Assert(c.Snapshot.Quality == LinkQuality.Stale && c.Snapshot.Detail.Contains("Verbindung verloren") && !c.Snapshot.Applied.Valid, "Ursache ersetzt oder stale gültig");
        });
        AsyncTest("S10", "Kontrolliertes Ende cancelt wartenden Schritt und löst Handler/Handles einmal", async () =>
        {
            var fake = new EndpointDouble(); var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); Task waiting = c.StepAsync(); await c.DisconnectAsync(); await MustFail(waiting);
            await c.DisconnectAsync(); c.Dispose();
            Assert(fake.Disposals == 1 && !fake.HasSubscribers && c.Snapshot.State == CouplingState.Closed && fake.Continues == 0, "Bereinigung nicht kontrolliert");
            await MustFail(c.ConnectAsync(BoundMap(), FakeApproval));
        });
        AsyncTest("S11", "CPU-STOP und Projektänderung invalidieren ohne Wiederaufnahme", async () =>
        {
            foreach (string why in new[] { "CPU STOP", "Softwareprojekt geändert", "Runtime-Manager verloren" })
            {
                var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
                await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await c.StepAsync(); fake.Break(why);
                Assert(c.Snapshot.State == CouplingState.Stale && !c.Snapshot.Applied.Valid, "Projekt-/CPU-Wechsel bleibt gültig");
                Assert(c.Snapshot.Plc != null && !c.Snapshot.Plc.Actuators.Valid, "Alter gelesener Frame blieb als gültig markiert");
                await MustFail(c.StepAsync()); Assert(fake.Continues == 1, "Automatischer Neustart");
            }
        });
        AsyncTest("S12", "Schreibfehler und Abbruch während des Austauschs verhindern CPU-Freigabe", async () =>
        {
            foreach (int scenario in new[] { 0, 1, 2 })
            {
                var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
                await c.ConnectAsync(BoundMap(), FakeApproval);
                if (scenario == 0) fake.ThrowOnWrite = true;
                else if (scenario == 1) fake.AfterRead = () => fake.Break("Abbruch nach Lesen");
                else fake.AfterWrite = () => fake.Break("Abbruch nach Schreiben");
                fake.Emit(); await MustFail(c.StepAsync()); Assert(fake.Continues == 0 && !c.Snapshot.Applied.Valid, "Unvollständigen Austausch freigegeben");
            }
        });
        AsyncTest("S13", "Begrenzter Callbackpuffer meldet Überlauf", async () =>
        {
            var fake = new EndpointDouble(); var o = Options(); o.QueueCapacity = 1;
            using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), o); await c.ConnectAsync(BoundMap(), FakeApproval);
            fake.Emit(); fake.Emit(id: 2); await MustFail(c.StepAsync()); Assert(c.Snapshot.Detail.Contains("überfüllt") && fake.Continues == 0, "Ereignis still gelöscht");
        });
        AsyncTest("S14", "Falsche Instanz und Erstzeitbasis werden abgelehnt", async () =>
        {
            var fake = new EndpointDouble { InstanceName = "FALSCH" }; using (var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options()))
            { await MustFail(c.ConnectAsync(BoundMap(), FakeApproval)); Assert(!fake.Calls.Contains("begin"), "Falsche Instanz aktiviert"); }
            fake = new(); using var next = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await next.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(1, 1); await MustFail(next.StepAsync()); Assert(fake.Continues == 0, "Zeitbasis geraten");
        });
        AsyncTest("S15", "Aliasing am tatsächlichen virtuellen Takt führt zu Sperre", async () =>
        {
            var fake = new EndpointDouble(); var p = new ExternalPlantSimulation(new()); using var c = new PlcCouplingController(fake, p, Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await c.StepAsync();
            fake.Emit(20000000, 20000000); await MustFail(c.StepAsync());
            Assert(c.Snapshot.Detail.Contains("Aliasing") && fake.Continues == 1 && !p.Snapshot.Actuators.Valid, "Aliasing verdeckt");
        });
        AsyncTest("S16", "Konkurrierender wartender Schritt ist abbrechbar und wird nicht nachgeholt", async () =>
        {
            var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); Task first = c.StepAsync();
            using var cancel = new CancellationTokenSource(); Task queued = c.StepAsync(cancel.Token); cancel.Cancel(); await MustFail(queued);
            fake.Emit(); await first; Assert(fake.Continues == 1 && c.Snapshot.Sequence == 1, "Abgebrochene Queueanforderung ausgeführt");
        });
        AsyncTest("S17", "Bereinigungsfehler meldet Faulted statt fälschlich Closed", async () =>
        {
            var fake = new EndpointDouble { ThrowOnDispose = true }; var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); await MustFail(c.DisconnectAsync());
            Assert(c.Snapshot.State == CouplingState.Faulted && fake.Disposals == 1 && !fake.HasSubscribers, "Handlefehler verschluckt");
            await MustFail(c.DisconnectAsync()); Assert(c.Snapshot.State == CouplingState.Faulted, "Unbestätigte Freigabe später als Closed ausgegeben");
        });
        Test("S18", "Tests referenzieren keine Siemensbibliothek und Optionsgrenzen sind streng", () =>
        {
            Reject(() => new CouplingOptions().Validate());
            var options = Options(); options.ConfirmedInputPartitions = new uint[] { 0xffff }; Reject(options.Validate);
            Assert(typeof(PlcCouplingController).Assembly.GetReferencedAssemblies().All(a => !a.Name.StartsWith("Siemens")), "Testdoubles laden SDK");
            Assert(AppDomain.CurrentDomain.GetAssemblies().All(a => !a.GetName().Name!.StartsWith("Siemens")), "Herstellerassembly in Testprozess");
        });
        AsyncTest("S19", "Frühes Ereignis beim Moduswechsel wird gepuffert; Startfehler wird bereinigt", async () =>
        {
            var fake = new EndpointDouble(); fake.Begin = () => fake.Emit();
            using (var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options()))
            { await c.ConnectAsync(BoundMap(), FakeApproval); await c.StepAsync(); Assert(fake.Inputs.Count == 1, "Früher Callback verloren"); }
            fake = new(); fake.Begin = () => throw new InvalidOperationException("Testdouble: Moduswechsel gescheitert");
            using (var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options()))
            { await MustFail(c.ConnectAsync(BoundMap(), FakeApproval)); Assert(c.Snapshot.State == CouplingState.Faulted, "Startfehler nicht sichtbar"); }
            Assert(fake.Disposals == 1 && !fake.HasSubscribers, "Teilaufbau nicht bereinigt");
        });
        AsyncTest("S20", "Überaltertes Queueereignis wird vor jedem E/A-Zugriff verworfen", async () =>
        {
            var fake = new EndpointDouble(); using var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options(40));
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.Emit(); await Task.Delay(90); await MustFail(c.StepAsync());
            Assert(c.Snapshot.Quality == LinkQuality.Stale && !fake.Calls.Any(s => s.StartsWith("read:")) && fake.Continues == 0, "Alter Punkt führte E/A aus");
        });
        AsyncTest("S21", "Verbindungsaufbau lässt sich vor Besitz eines Handles abbrechen", async () =>
        {
            var factory = new WaitingFactory(); using var c = new PlcCouplingController(factory, new PlantDouble(new()), Options());
            using var cancel = new CancellationTokenSource(); Task connecting = c.ConnectAsync(BoundMap(), FakeApproval, cancel.Token);
            Assert(factory.Entered.Wait(1000), "Factory wurde nicht betreten"); cancel.Cancel(); await MustFail(connecting);
            await c.DisconnectAsync(); Assert(c.Snapshot.State == CouplingState.Closed && !c.Snapshot.Applied.Valid, "Aufbauabbruch nicht bereinigt");
        });
        AsyncTest("S22", "Cleanupfrist bricht keinen laufenden Endpunktaufruf gewaltsam ab", async () =>
        {
            var fake = new EndpointDouble(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var c = new PlcCouplingController(fake, new PlantDouble(fake.Calls), Options());
            await c.ConnectAsync(BoundMap(), FakeApproval); fake.AfterRead = () => { entered.Set(); release.Wait(); }; fake.Emit();
            Task step = Task.Run(() => c.StepAsync());
            try
            {
                Assert(entered.Wait(1000), "Testdouble-Endpunktaufruf wurde nicht betreten");
                using var deadline = new CancellationTokenSource(30); await MustFail(c.DisconnectAsync(deadline.Token));
                Assert(c.Snapshot.State == CouplingState.Closing && fake.Disposals == 0 && !c.Snapshot.Applied.Valid, "Verfrüht freigegeben/Closed behauptet");
            }
            finally { release.Set(); }
            await MustFail(step); await c.DisconnectAsync(); Assert(fake.Disposals == 1 && fake.Continues == 0, "Nachlauf nach Abbruch");
        });
        AsyncTest("S23", "Zeitgleicher Abbruch kann Stale nicht durch späten Schrittabschluss überschreiben", async () =>
        {
            var fake = new EndpointDouble(); var p = new PlantDouble(fake.Calls);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var c = new PlcCouplingController(fake, p, Options()); await c.ConnectAsync(BoundMap(), FakeApproval);
            Task? disconnectNotification = null;
            p.OnInvalidate = () => { entered.Set(); release.Wait(); };
            fake.AfterWrite = () =>
            {
                disconnectNotification = Task.Run(() => fake.Break("Zeitgleicher Abbruch"));
                if (!entered.Wait(1000)) throw new InvalidOperationException("Invalidierung wurde nicht betreten.");
            };
            fake.Emit();
            try
            {
                await MustFail(Task.Run(() => c.StepAsync()));
                Assert(c.Snapshot.State == CouplingState.Stale && !c.Snapshot.Applied.Valid && fake.Continues == 0, "Später Commit machte Stale wieder gültig");
            }
            finally { release.Set(); }
            if (disconnectNotification != null) await disconnectNotification;
        });
    }
}
