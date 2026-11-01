using System;
using System.Threading;
using Plant.Core;

namespace Plant.Adapter
{
    public enum CouplingState { Disconnected, Connecting, AwaitingSync, SynchronizedUnverified, Stale, Faulted, Closing, Closed }
    public enum EndpointEvidence { TestDouble, SiemensLiveUnverified }
    public sealed record SyncNotification(uint InputPartition, long SinceAnyNs, long SinceSameNs, uint Count, string? Error = null);
    public sealed record PlcReadback(ActuatorFrame Actuators, double? MeasuredLengthMm = null, int? PulseCount = null, bool? MeasurementActive = null);
    public sealed record CouplingSnapshot(CouplingState State, LinkQuality Quality, long Sequence, long VirtualTimeNs,
        ActuatorFrame Applied, PlcReadback? Plc, string Detail, EndpointEvidence Evidence, bool LiveValidated = false);

    // These are our contracts. SDK objects stay inside the optional V5 assembly.
    public interface IPlcEndpoint : IDisposable
    {
        string InstanceName { get; }
        EndpointEvidence Evidence { get; }
        event Action<SyncNotification> SyncPoint;
        event Action<string> Invalidated;
        void BeginSingleStepInputs();
        PlcReadback ReadOutputs(ClockGrant grant);
        void WriteInputs(SensorFrame frame);
        void ContinueToNextSyncPoint();
    }
    public interface IPlcEndpointFactory
    {
        IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation);
    }
    public sealed record LiveAuthorization(bool RuntimeInitializationAllowed = false, bool AdvancingExistingCpuAllowed = false)
    {
        public void RequireApproved()
        {
            if (!RuntimeInitializationAllowed || !AdvancingExistingCpuAllowed)
                throw new InvalidOperationException("Live-Sperre: gesonderte Freigabe für API-Initialisierung und Fortschalten der ausgewählten vorhandenen CPU fehlt.");
        }
    }
    public sealed class CouplingOptions
    {
        public DigitalCycleProfile? DigitalCycle { get; set; }
        public uint[] ConfirmedInputPartitions { get; set; } = Array.Empty<uint>();
        public bool InputAdoptionReviewed { get; set; }
        public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public long MaxVirtualDeltaNs { get; set; } = 100000000;
        public int QueueCapacity { get; set; } = 8;
        public void Validate()
        {
            DigitalCycle?.Validate();
            Numbers.Require(DigitalCycle == null || (ConfirmedInputPartitions != null && ConfirmedInputPartitions.Length == 1 && ConfirmedInputPartitions[0] == 0),
                "Digitales C-Profil verlangt die beobachtete U2-Ereignis-ID 0.");
            Numbers.Require(InputAdoptionReviewed && ConfirmedInputPartitions != null && ConfirmedInputPartitions.Length > 0,
                "Bestätigte Teilprozessabbilder und Prüfung der Eingangsübernahme fehlen.");
            Numbers.Require(Array.TrueForAll(ConfirmedInputPartitions!, id => id < 0xffff), "Zykluskontrollpunkt 0xFFFF ist in diesem P-Profil nicht erlaubt.");
            Numbers.Require(SyncTimeout > TimeSpan.Zero && SyncTimeout <= TimeSpan.FromSeconds(30) && MaxVirtualDeltaNs > 0 && MaxVirtualDeltaNs <= 1000000000,
                "Zeit-/Frischegrenzen ungültig.");
            Numbers.Require(QueueCapacity > 0 && QueueCapacity <= 128, "Ereignispuffergröße ungültig.");
        }
    }
    public interface IExternallyTimedPlant
    {
        long TimeNs { get; }
        void AdvanceTo(long timeNs, ActuatorFrame heldCommand, CancellationToken cancellation);
        SensorFrame Inputs(long sequence);
        void Invalidate();
    }
    public interface IAdoptedOutputObserver { void AdoptOutputs(ActuatorFrame command); }
}
