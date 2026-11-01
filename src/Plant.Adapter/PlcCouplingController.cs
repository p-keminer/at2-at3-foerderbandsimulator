using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Plant.Core;

namespace Plant.Adapter
{
    // One operation owner. Callbacks only queue immutable messages or cancel; never perform model/SDK I/O.
    // Shared by bounded CLI and manually connected WPF; no reference controller here.
    public sealed class PlcCouplingController : IDisposable
    {
        private sealed record QueuedPoint(SyncNotification Point, long ReceivedTicks);
        private readonly IPlcEndpointFactory factory;
        private readonly IExternallyTimedPlant plant;
        private readonly CouplingOptions options;
        private readonly BlockingCollection<QueuedPoint> points;
        private readonly CancellationTokenSource lifetime = new();
        private readonly SemaphoreSlim operation = new(1, 1);
        private readonly object stateGate = new();
        private readonly SyncTimeline timeline;
        private IPlcEndpoint? endpoint;
        private bool connectAttempted;
        public SyncNotification? LastNotification { get; private set; }
        public int ContinueCalls { get; private set; }
        public EndpointInspection? BeforeDisconnect { get; private set; }
        public string? InspectionError { get; private set; }
        private int closing, cleanupFailed, invalidated;
        private CouplingSnapshot current = new(CouplingState.Disconnected, LinkQuality.Disconnected, 0, 0,
            new(Valid: false), null, "Nicht verbunden; Livevalidierung ausstehend.", EndpointEvidence.SiemensLiveUnverified);
        public PlcCouplingController(IPlcEndpointFactory factory, IExternallyTimedPlant plant, CouplingOptions options)
        {
            options.Validate(); this.factory = factory; this.plant = plant;
            this.options = new CouplingOptions { DigitalCycle = options.DigitalCycle?.Copy(), ConfirmedInputPartitions = (uint[])options.ConfirmedInputPartitions.Clone(), InputAdoptionReviewed = options.InputAdoptionReviewed,
                MaxVirtualDeltaNs = options.MaxVirtualDeltaNs, SyncTimeout = options.SyncTimeout, QueueCapacity = options.QueueCapacity };
            points = new(options.QueueCapacity); timeline = new(this.options);
        }
        public CouplingSnapshot Snapshot { get { lock (stateGate) return current; } }
        public async Task ConnectAsync(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation = default)
        {
            // Guards precede factory access, including SDK loading in a future explicitly approved launcher.
            authorization.RequireApproved();
            if (options.DigitalCycle != null) options.DigitalCycle.RequireMap(map); else map.RequireLiveValid();
            var frozenMap = Json.Read<SignalMap>(Json.Write(map));
            await operation.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (connectAttempted || Volatile.Read(ref closing) != 0) throw new InvalidOperationException("Neue Verbindung erfordert neue Instanz und erneute Prüfung.");
                connectAttempted = true;
                SetState(CouplingState.Connecting, LinkQuality.Unverified, "Vorhandene lokale Instanz öffnen; keine CPU-Erzeugung und kein RUN-Aufruf.");
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
                endpoint = await Task.Run(() => factory.OpenExisting(frozenMap, authorization, linked.Token), linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (endpoint.InstanceName != frozenMap.InstanceName) throw new InvalidOperationException("Falsche Instanz vom Endpunkt geliefert.");
                if (options.DigitalCycle != null && (!(endpoint is IEndpointInspection profile) || !profile.UsesDigitalCycleProfile))
                    throw new InvalidOperationException("Digitales C-Profil und Endpunkt stimmen nicht überein.");
                endpoint.SyncPoint += OnSyncPoint; endpoint.Invalidated += OnInvalidated;
                SetState(CouplingState.AwaitingSync, LinkQuality.Unverified, "Warte auf ersten bestätigten Eingangs-Synchronisationspunkt.", endpoint.Evidence);
                endpoint.BeginSingleStepInputs();
                linked.Token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref invalidated) != 0) throw new InvalidOperationException(Snapshot.Detail);
            }
            catch (Exception e) { Fail("Verbindungsaufbau abgebrochen: " + e.Message, LinkQuality.Faulted); throw; }
            finally { operation.Release(); }
        }
        public async Task<CouplingSnapshot> StepAsync(CancellationToken cancellation = default, Func<CouplingSnapshot, bool>? continueAfter = null)
        {
            // A canceled queued request must never execute a late simulation step.
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            await operation.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                if (endpoint == null || Volatile.Read(ref closing) != 0 || Volatile.Read(ref invalidated) != 0 || lifetime.IsCancellationRequested)
                    throw new InvalidOperationException("Keine aktive geprüfte Ablaufsteuerung.");
                QueuedPoint point;
                try
                {
                    point = await Task.Run(() => points.TryTake(out var value, (int)options.SyncTimeout.TotalMilliseconds, request.Token)
                        ? value : throw new TimeoutException("Kein frischer Synchronisationspunkt innerhalb der Wartezeit."), request.Token).ConfigureAwait(false);
                }
                catch (TimeoutException e) { Fail(e.Message, LinkQuality.Stale); throw; }
                request.Token.ThrowIfCancellationRequested();
                if ((Stopwatch.GetTimestamp() - point.ReceivedTicks) / (double)Stopwatch.Frequency > options.SyncTimeout.TotalSeconds)
                    throw new TimeoutException("Gepuffertes Synchronisationsereignis ist veraltet.");
                var grant = timeline.Accept(point.Point);
                LastNotification = point.Point;
                // Causal zero-order hold: OLD adopted output governs the elapsed interval.
                var held = Snapshot.Applied;
                if (grant.TimeNs > plant.TimeNs)
                {
                    if (!held.Valid) throw new InvalidOperationException("Kein gültiger gehaltener Befehl für das Zeitintervall.");
                    plant.AdvanceTo(grant.TimeNs, held, request.Token);
                }
                var ioGrant = new ClockGrant(grant.TimeNs / 1000, grant.Sequence);
                void RequireFresh()
                {
                    request.Token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref invalidated) != 0 || Volatile.Read(ref closing) != 0) throw new OperationCanceledException("Kopplung bereits invalidiert.", request.Token);
                    if ((Stopwatch.GetTimestamp() - point.ReceivedTicks) / (double)Stopwatch.Frequency > options.SyncTimeout.TotalSeconds)
                        throw new TimeoutException("Synchronisationspunkt während des Austauschs veraltet.");
                }
                RequireFresh();
                request.Token.ThrowIfCancellationRequested();
                PlcReadback read = endpoint.ReadOutputs(ioGrant);
                RequireFresh();
                if (!read.Actuators.Valid || read.Actuators.Sequence != grant.Sequence || read.Actuators.TimeUs != ioGrant.TimeUs)
                    throw new InvalidOperationException("Aktorframe ungültig, veraltet oder falsch zugeordnet.");
                if (!Analog.OutputInNominalRange(read.Actuators.SpeedCommandRaw)) throw new InvalidOperationException("Aktorrohwert außerhalb Nennbereich.");
                if (read.Actuators.MotorRight && read.Actuators.MotorLeft)
                    throw new InvalidOperationException("Richtungskonflikt: SPS befiehlt gleichzeitig rechts und links; keine weitere CPU-Fortschaltung.");
                if (read.MeasuredLengthMm.HasValue && !Numbers.Finite(read.MeasuredLengthMm.Value)) throw new InvalidOperationException("Ungültige Controllertelemetrie.");
                if (plant is IAdoptedOutputObserver observer) observer.AdoptOutputs(read.Actuators);
                SensorFrame inputs = plant.Inputs(grant.Sequence);
                if (!inputs.Valid || inputs.TimeUs != ioGrant.TimeUs || inputs.Sequence != grant.Sequence)
                    throw new InvalidOperationException("Anlageneingänge passen nicht zum Synchronisationspunkt.");
                request.Token.ThrowIfCancellationRequested();
                endpoint.WriteInputs(inputs); // CPU remains frozen across all individual SDK writes.
                RequireFresh();
                request.Token.ThrowIfCancellationRequested();
                lock (stateGate)
                {
                    if (lifetime.IsCancellationRequested || Volatile.Read(ref invalidated) != 0 || Volatile.Read(ref closing) != 0) throw new OperationCanceledException(request.Token);
                    current = new(CouplingState.SynchronizedUnverified,
                        endpoint.Evidence == EndpointEvidence.SiemensLiveUnverified ? LinkQuality.Connected : LinkQuality.Unverified, grant.Sequence, grant.TimeNs,
                        read.Actuators, read, options.DigitalCycle != null ? "Digitaler C-Austausch aktiv; Testgeschwindigkeit aus C#, SPS-Messwerte nicht angebunden." : "E/A-Austausch abgeschlossen; Siemens-Livevalidierung ausstehend.", endpoint.Evidence);
                }
                // Advance the existing RUN CPU only after a complete valid input publication.
                // No UI, file I/O or logging on SDK callback threads.
                RequireFresh();
                if (continueAfter == null || continueAfter(Snapshot))
                {
                    RequireFresh(); // Cancellation/invalidations may occur inside the progress/limit callback.
                    ContinueCalls++;
                    endpoint.ContinueToNextSyncPoint();
                }
                request.Token.ThrowIfCancellationRequested();
                return Snapshot;
            }
            catch (Exception e)
            {
                Fail(e is OperationCanceledException ? "Wartender/aktiver Simulationsschritt abgebrochen; keine automatische Wiederaufnahme." : e.Message,
                    e is TimeoutException ? LinkQuality.Stale : LinkQuality.Faulted);
                throw;
            }
            finally { operation.Release(); }
        }
        private void OnSyncPoint(SyncNotification point)
        {
            if (Volatile.Read(ref closing) != 0 || Volatile.Read(ref invalidated) != 0 || lifetime.IsCancellationRequested) return;
            if (!points.TryAdd(new(point, Stopwatch.GetTimestamp()))) Fail("Synchronisationspuffer überfüllt; kein Ereignis wird still verworfen.", LinkQuality.Faulted);
        }
        private void OnInvalidated(string detail) => Fail("Endpunkt ungültig: " + detail, LinkQuality.Stale);
        private void SetState(CouplingState state, LinkQuality quality, string detail, EndpointEvidence? evidence = null)
        { lock (stateGate) current = current with { State = state, Quality = quality, Detail = detail, Evidence = evidence ?? current.Evidence }; }
        private void Fail(string detail, LinkQuality quality)
        {
            lock (stateGate)
            {
                if (current.State == CouplingState.Closed || current.State == CouplingState.Closing) return;
                if (Volatile.Read(ref invalidated) != 0 || lifetime.IsCancellationRequested) return; // Preserve original disconnect/overflow reason.
                Volatile.Write(ref invalidated, 1); // Commit barrier precedes cancellation and external invalidation work.
                current = current with { State = quality == LinkQuality.Stale ? CouplingState.Stale : CouplingState.Faulted,
                    Quality = quality, Applied = current.Applied with { Valid = false },
                    Plc = current.Plc == null ? null : current.Plc with { Actuators = current.Plc.Actuators with { Valid = false } }, Detail = detail };
            }
            lifetime.Cancel(); plant.Invalidate();
        }
        public async Task DisconnectAsync(CancellationToken cleanupCancellation = default)
        {
            if (Volatile.Read(ref cleanupFailed) != 0) throw new InvalidOperationException("Frühere Handlebereinigung nicht bestätigt; kein erfolgreicher Abschluss wird nachträglich behauptet.");
            Interlocked.Exchange(ref closing, 1);
            lock (stateGate)
            {
                if (current.State == CouplingState.Closed) return;
                current = current with { State = CouplingState.Closing, Quality = LinkQuality.Stale, Applied = current.Applied with { Valid = false },
                    Plc = current.Plc == null ? null : current.Plc with { Actuators = current.Plc.Actuators with { Valid = false } }, Detail = "Lokale Kopplung wird beendet; kein CPU-Zustandswechsel." };
            }
            lifetime.Cancel(); plant.Invalidate();
            // Never dispose an endpoint while another operation may still be inside vendor code.
            await operation.WaitAsync(cleanupCancellation).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref cleanupFailed) != 0) throw new InvalidOperationException("Handlebereinigung ist weiterhin unbestätigt.");
                if (endpoint != null)
                {
                    var owned = endpoint; endpoint = null;
                    if (owned is IEndpointInspection inspect)
                    {
                        try { BeforeDisconnect = inspect.Inspect(); }
                        catch (Exception e) { InspectionError = e.ToString(); }
                    }
                    try { owned.SyncPoint -= OnSyncPoint; owned.Invalidated -= OnInvalidated; }
                    finally { owned.Dispose(); }
                }
                SetState(CouplingState.Closed, LinkQuality.Disconnected, "Handles freigegeben. Letzter CPU-Zustand: " +
                    (BeforeDisconnect == null ? "unbestätigt" : BeforeDisconnect.OperatingState + "/" + BeforeDisconnect.OperatingMode) +
                    "; nach Dispose nicht erneut verbunden. Kein automatischer Default-Reset.");
            }
            catch (Exception e) { Interlocked.Exchange(ref cleanupFailed, 1); SetState(CouplingState.Faulted, LinkQuality.Faulted, "Bereinigung fehlgeschlagen: " + e.Message); throw; }
            finally { operation.Release(); }
        }
        public void Dispose()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            DisconnectAsync(deadline.Token).GetAwaiter().GetResult();
            // Synchronization primitives deliberately remain usable for idempotent concurrent Close calls.
        }
    }
}
