using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Plant.Adapter;
using Plant.Core;

namespace Plant.Siemens.V5
{
    public sealed class V5MinimalEndpointFactory : IMinimalProbeEndpointFactory
    {
        private readonly MinimalSynchronization synchronization;
        public V5MinimalEndpointFactory(MinimalSynchronization synchronization = MinimalSynchronization.ProcessImagePartition)
        {
            Numbers.Require(Enum.IsDefined(typeof(MinimalSynchronization), synchronization), "Unbekanntes Minimalprofil.");
            this.synchronization = synchronization;
        }
        public IMinimalProbeEndpoint OpenExisting(MinimalProbeMap map, LiveAuthorization authorization, CancellationToken cancellation)
        {
            authorization.RequireApproved();
            if (map == null) throw new ArgumentNullException(nameof(map));
            cancellation.ThrowIfCancellationRequested();
            return OpenApproved(map, synchronization, cancellation);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IMinimalProbeEndpoint OpenApproved(MinimalProbeMap map, MinimalSynchronization synchronization, CancellationToken cancellation) =>
            new V5MinimalEndpoint(V5Session.Open(map.InstanceName, cancellation), map, synchronization);
    }
    internal sealed class V5MinimalEndpoint : IMinimalProbeEndpoint, IMinimalProbeDiagnosticSource
    {
        private readonly V5Session session;
        private readonly MinimalProbeMap map;
        public V5MinimalEndpoint(V5Session session, MinimalProbeMap map, MinimalSynchronization synchronization)
        { this.session = session; this.map = map; Synchronization = synchronization; }
        public MinimalSynchronization Synchronization { get; }
        public string InstanceName => session.InstanceName;
        public EndpointEvidence Evidence => session.Evidence;
        public string[] DiagnosticTrace => session.DiagnosticTrace;
        public event Action<SyncNotification> SyncPoint { add => session.SyncPoint += value; remove => session.SyncPoint -= value; }
        public event Action<string> Invalidated { add => session.Invalidated += value; remove => session.Invalidated -= value; }
        public void BeginSingleStepInputs()
        {
            if (Synchronization == MinimalSynchronization.CycleControlPoint) session.BeginSingleStepCycle();
            else session.BeginSingleStepInputs();
        }
        private void RequireAreas()
        {
            if (Synchronization == MinimalSynchronization.CycleControlPoint) session.RequireFrozenCycle();
            else session.RequireFrozenInputs();
            Numbers.Require(session.InstanceName == map.InstanceName, "Minimaltest: falsche SDK-Instanz.");
            Numbers.Require(map.SensorLeftHigh.ConfirmedAreaSizeBytes <= session.Instance.InputArea.AreaSize &&
                map.LampGreen.ConfirmedAreaSizeBytes <= session.Instance.OutputArea.AreaSize, "Minimaltest: bestätigte Bereichsgröße überschreitet SDK-Area.");
        }
        public bool ReadLampGreen() => V5Session.Sdk(() =>
        {
            RequireAreas();
            return session.Instance.OutputArea.ReadBit(map.LampGreen.ByteOffset, map.LampGreen.BitOffset);
        });
        public void WriteSensorLeftHigh(bool high) => V5Session.Sdk(() =>
        {
            RequireAreas();
            // WriteBit preserves the other seven bits. NO output/marker/other sensor writes.
            session.Instance.InputArea.WriteBit(map.SensorLeftHigh.ByteOffset, map.SensorLeftHigh.BitOffset, high);
        });
        public void ContinueToNextSyncPoint() => session.ContinueToNextSyncPoint();
        public void Dispose() => session.Dispose();
    }
}
