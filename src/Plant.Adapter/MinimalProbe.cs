using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Plant.Core;

namespace Plant.Adapter
{
    public enum MinimalSynchronization { ProcessImagePartition, CycleControlPoint }
    // Own two-bit contract; no pretend motor commands and no full-plant snapshots.
    public interface IMinimalProbeEndpoint : IDisposable
    {
        string InstanceName { get; }
        EndpointEvidence Evidence { get; }
        MinimalSynchronization Synchronization { get; }
        event Action<SyncNotification> SyncPoint;
        event Action<string> Invalidated;
        void BeginSingleStepInputs();
        bool ReadLampGreen();
        void WriteSensorLeftHigh(bool high);
        void ContinueToNextSyncPoint();
    }
    public interface IMinimalProbeEndpointFactory
    {
        IMinimalProbeEndpoint OpenExisting(MinimalProbeMap map, LiveAuthorization authorization, CancellationToken cancellation);
    }
    public interface IMinimalProbeDiagnosticSource
    {
        // A snapshot of locally cached messages ONLY; no SDK reads during cleanup.
        string[] DiagnosticTrace { get; }
    }
    public sealed class MinimalProbeOptions
    {
        public uint InputPartition { get; set; }
        public MinimalSynchronization Synchronization { get; set; }
        public bool Ob1AutomaticImageConfirmed { get; set; }
        public uint ExpectedSyncPointId => Synchronization == MinimalSynchronization.CycleControlPoint ? 0u : InputPartition;
        public bool TiaMinimalProgramConfirmed { get; set; }
        public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(15);
        internal void Validate()
        {
            Numbers.Require(TiaMinimalProgramConfirmed, "TIA-Minimalprogramm, E/A-Übernahme und inaktive Motorlogik müssen ausdrücklich bestätigt sein.");
            Numbers.Require(InputPartition < 0xffff, "Minimaltest akzeptiert nur eine bestätigte P-Partition, keinen Zykluskontrollpunkt.");
            Numbers.Require(Enum.IsDefined(typeof(MinimalSynchronization), Synchronization), "Unbekanntes Minimal-Synchronisationsprofil.");
            Numbers.Require(Synchronization != MinimalSynchronization.CycleControlPoint || (Ob1AutomaticImageConfirmed && InputPartition == 0),
                "C-Profil erfordert ausdrücklich bestätigtes OB1-Prozessabbild mit automatischer Aktualisierung; keine fremde Partition.");
            Numbers.Require(Synchronization != MinimalSynchronization.ProcessImagePartition || !Ob1AutomaticImageConfirmed,
                "OB1-Automatikbestätigung gehört zum C-Profil, nicht zu einem geratenen P-Punkt 0.");
            Numbers.Require(SyncTimeout > TimeSpan.Zero && SyncTimeout <= TimeSpan.FromSeconds(5) && TotalTimeout >= SyncTimeout && TotalTimeout <= TimeSpan.FromSeconds(20),
                "Minimaltest: Sync-Timeout maximal 5 s, Gesamttimeout maximal 20 s.");
        }
    }
    public sealed record MinimalProbeSample(long Sequence, long VirtualTimeNs, bool LampGreen, bool? ExpectedLampGreen,
        bool? WrittenSensorLeftHigh, bool InputWriteConfirmed = false, uint RawSyncPointId = 0,
        long RawSinceAnyNs = 0, long RawSinceSameNs = 0, uint RawSyncPointCount = 1);
    public sealed class MinimalProbeResult
    {
        public string Profile { get; } = "SensorLeftHigh_I0.0_LampGreen_Q0.0";
        public string InstanceName { get; internal set; } = "";
        public uint InputPartition { get; internal set; }
        public string SynchronizationMode { get; internal set; } = "SingleStep_P";
        public uint ExpectedSyncPointId { get; internal set; }
        public EndpointEvidence? Evidence { get; internal set; }
        public bool Passed { get; internal set; }
        public bool CleanupConfirmed { get; internal set; }
        public int ContinueCalls { get; internal set; }
        public int ReceivedSyncEvents { get; internal set; }
        public string LastStage { get; internal set; } = "NotStarted";
        public string[] DiagnosticTrace { get; internal set; } = Array.Empty<string>();
        public string? DiagnosticTraceError { get; internal set; }
        public List<MinimalProbeSample> Samples { get; } = new();
        public string? Error { get; internal set; }
        public string? CleanupError { get; internal set; }
        public string CpuStateAfterCleanup { get; } = "Ungeprüft; kein Wechsel auf Default/RUN/STOP durch diesen Test.";
    }

    public static class MinimalProbe
    {
        private sealed record QueuedPoint(SyncNotification Point, long ReceivedTicks);

        // Synchronous native calls have no documented cancellation API. The dedicated CLI
        // process additionally needs the supplied external watchdog for a hard wall-time limit.
        public static MinimalProbeResult Run(IMinimalProbeEndpointFactory factory, MinimalProbeMap map,
            LiveAuthorization authorization, MinimalProbeOptions options, CancellationToken cancellation = default)
        {
            authorization.RequireApproved(); options.Validate();
            if (map == null) throw new ArgumentNullException(nameof(map));
            uint partition = options.InputPartition;
            var synchronization = options.Synchronization;
            uint expectedPointId = options.ExpectedSyncPointId;
            string mode = synchronization == MinimalSynchronization.CycleControlPoint ? "SingleStep_C" : "SingleStep_P";
            TimeSpan syncTimeout = options.SyncTimeout, totalTimeout = options.TotalTimeout;
            var result = new MinimalProbeResult { InstanceName = map.InstanceName, InputPartition = partition,
                SynchronizationMode = mode, ExpectedSyncPointId = expectedPointId };
            using var deadline = new CancellationTokenSource(totalTimeout);
            using var invalidation = new CancellationTokenSource();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token, invalidation.Token);
            using var points = new BlockingCollection<QueuedPoint>(8);
            // V5 U2 file 50.43.04.00 actually emits C ID 0 (cycle-01 evidence),
            // with nonzero first SinceAny and first SinceSame=0. No fallback IDs.
            var timeline = new SyncTimeline(new[] { expectedPointId }, 100000000,
                synchronization == MinimalSynchronization.CycleControlPoint);
            var eventGate = new object(); bool accepting = true; string? invalidReason = null; int received = 0;
            void Invalidate(string reason)
            {
                lock (eventGate)
                {
                    if (!accepting) return;
                    invalidReason ??= reason; invalidation.Cancel();
                }
            }
            void Receive(SyncNotification point)
            {
                lock (eventGate)
                {
                    if (!accepting || invalidReason != null) return;
                    received++;
                    if (!points.TryAdd(new(point, Stopwatch.GetTimestamp()))) Invalidate("Minimaltest: Ereignispuffer übergelaufen.");
                }
            }
            void RequireActive()
            {
                lock (eventGate) if (invalidReason != null) throw new InvalidOperationException(invalidReason);
                if (deadline.IsCancellationRequested) throw new TimeoutException("Minimaltest: Gesamttimeout erreicht.");
                stop.Token.ThrowIfCancellationRequested();
            }
            IMinimalProbeEndpoint? endpoint = null;
            try
            {
                result.LastStage = "OpeningExistingInstance";
                RequireActive(); endpoint = factory.OpenExisting(map, authorization, stop.Token);
                RequireActive(); Numbers.Require(endpoint.InstanceName == map.InstanceName, "Minimaltest: falsche Instanz.");
                Numbers.Require(endpoint.Synchronization == synchronization, "Minimaltest: Endpunkt und gewähltes Synchronisationsprofil stimmen nicht überein.");
                result.Evidence = endpoint.Evidence;
                result.LastStage = "Subscribing";
                endpoint.SyncPoint += Receive; endpoint.Invalidated += Invalidate;
                result.LastStage = synchronization == MinimalSynchronization.CycleControlPoint ? "RequestingSingleStepC" : "RequestingSingleStepP";
                RequireActive(); endpoint.BeginSingleStepInputs();
                // Initial arbitrary Q is recorded, never counted as a passed level test.
                // Exactly three CPU advances: write LOW, HIGH, LOW, then read final LOW and HOLD.
                bool[] levels = { false, true, false };
                for (int i = 0; i <= levels.Length; i++)
                {
                    RequireActive();
                    result.LastStage = "WaitingForSyncPoint";
                    if (!points.TryTake(out var queued, (int)Math.Ceiling(syncTimeout.TotalMilliseconds), stop.Token))
                        throw new TimeoutException("Minimaltest: kein Synchronisationspunkt für " + mode + " innerhalb des Sync-Timeouts. LastStage und DiagnosticTrace auswerten; noch kein Nachweis der E/A-Kopplung.");
                    result.LastStage = "ValidatingSyncPoint";
                    var grant = timeline.Accept(queued.Point);
                    void Fresh()
                    {
                        RequireActive();
                        if ((Stopwatch.GetTimestamp() - queued.ReceivedTicks) / (double)Stopwatch.Frequency > syncTimeout.TotalSeconds)
                            throw new TimeoutException("Minimaltest: veralteter Synchronisationspunkt.");
                    }
                    result.LastStage = "ReadingLampGreen";
                    Fresh(); bool lamp = endpoint.ReadLampGreen(); Fresh();
                    bool? expected = i == 0 ? null : levels[i - 1];
                    bool? write = i < levels.Length ? levels[i] : null;
                    result.Samples.Add(new(grant.Sequence, grant.TimeNs, lamp, expected, null,
                        RawSyncPointId: queued.Point.InputPartition, RawSinceAnyNs: queued.Point.SinceAnyNs,
                        RawSinceSameNs: queued.Point.SinceSameNs, RawSyncPointCount: queued.Point.Count));
                    Numbers.Require(!expected.HasValue || lamp == expected.Value,
                        "Minimaltest: LampGreen entspricht nicht dem zuvor geschriebenen SensorLeftHigh; TIA-Logik/TPA/Ausgangsübernahme prüfen.");
                    if (write.HasValue)
                    {
                        Fresh();
                        result.LastStage = "WritingSensorLeftHigh";
                        result.Samples[result.Samples.Count - 1] = result.Samples[result.Samples.Count - 1] with { WrittenSensorLeftHigh = write };
                        endpoint.WriteSensorLeftHigh(write.Value);
                        result.Samples[result.Samples.Count - 1] = result.Samples[result.Samples.Count - 1] with { InputWriteConfirmed = true };
                        Fresh(); result.LastStage = "ContinuingToNextSyncPoint";
                        result.ContinueCalls++; endpoint.ContinueToNextSyncPoint(); Fresh();
                    }
                    // Last point: no write, no continue, no auto resume.
                }
                lock (eventGate)
                {
                    RequireActive(); accepting = false; result.Passed = true; result.LastStage = "Completed";
                }
            }
            catch (Exception e)
            {
                string? reason; lock (eventGate) reason = invalidReason;
                result.Error = reason ?? (deadline.IsCancellationRequested ? "Gesamttimeout: " + e : e.ToString());
            }
            finally
            {
                // Stop callbacks BEFORE releasing their queue and cancellation sources.
                lock (eventGate) { accepting = false; result.ReceivedSyncEvents = received; }
                if (endpoint is IMinimalProbeDiagnosticSource diagnostics)
                {
                    try { result.DiagnosticTrace = diagnostics.DiagnosticTrace; }
                    catch (Exception e) { result.DiagnosticTraceError = e.ToString(); }
                }
                var cleanupErrors = new List<Exception>();
                void Attempt(Action action) { try { action(); } catch (Exception e) { cleanupErrors.Add(e); } }
                if (endpoint != null)
                {
                    Attempt(() => endpoint.SyncPoint -= Receive); Attempt(() => endpoint.Invalidated -= Invalidate);
                    Attempt(endpoint.Dispose);
                }
                result.CleanupConfirmed = cleanupErrors.Count == 0;
                if (cleanupErrors.Count > 0) result.CleanupError = new AggregateException(cleanupErrors).ToString();
                result.Passed = result.Passed && result.CleanupConfirmed;
            }
            return result;
        }
    }
}
