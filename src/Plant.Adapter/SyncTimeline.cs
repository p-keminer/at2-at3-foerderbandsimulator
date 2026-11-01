using System;
using System.Collections.Generic;
using System.Linq;

namespace Plant.Adapter
{
    // Time comes solely from documented V5 nanosecond deltas. Wall time only detects stale waits.
    internal sealed class SyncTimeline
    {
        private readonly HashSet<uint> partitions;
        private readonly Dictionary<uint, long> previousPartitionTime = new();
        private readonly long maxDelta;
        private readonly bool establishOriginAtFirstPoint;
        private long sequence, timeNs;
        public SyncTimeline(CouplingOptions options)
            : this(options.ConfirmedInputPartitions, options.MaxVirtualDeltaNs, options.DigitalCycle != null) { }
        internal SyncTimeline(IEnumerable<uint> confirmedPointIds, long maxDeltaNs, bool establishOriginAtFirstPoint = false)
        { partitions = new(confirmedPointIds); maxDelta = maxDeltaNs; this.establishOriginAtFirstPoint = establishOriginAtFirstPoint; }
        public (long TimeNs, long Sequence) Accept(SyncNotification point)
        {
            if (point.Error != null) throw new InvalidOperationException("SDK-Synchronisationsfehler: " + point.Error);
            if (point.Count != 1) throw new InvalidOperationException("Zusammengefasste/fehlende Synchronisationspunkte: Count=" + point.Count);
            if (!partitions.Contains(point.InputPartition)) throw new InvalidOperationException("Unbestätigtes Teilprozessabbild: " + point.InputPartition);
            if (point.SinceAnyNs < 0 || point.SinceSameNs < 0 || point.SinceAnyNs > maxDelta)
                throw new InvalidOperationException("Ungültiges oder zu großes virtuelles Zeitdelta.");
            if (sequence == 0 && ((!establishOriginAtFirstPoint && point.SinceAnyNs != 0) || point.SinceSameNs != 0))
                throw new InvalidOperationException("Erster Synchronisationspunkt benötigt eine eindeutige Nullzeitbasis.");
            // In the observed V5 U2 C callback, first SinceAny describes time before
            // our first held exchange. Start our relative clock THERE, retain raw data
            // in the minimal report, and validate every subsequent SDK delta unchanged.
            long next = sequence == 0 && establishOriginAtFirstPoint ? 0 : checked(timeNs + point.SinceAnyNs);
            if (previousPartitionTime.TryGetValue(point.InputPartition, out long previous))
            {
                if (next == previous || point.SinceSameNs != next - previous)
                    throw new InvalidOperationException("Doppelter/veralteter Punkt oder inkonsistente virtuelle Zeit derselben Partition.");
            }
            else if (point.SinceSameNs != 0)
                throw new InvalidOperationException("Erster Punkt einer neuen Partition benötigt SinceSame=0.");
            previousPartitionTime[point.InputPartition] = next; timeNs = next;
            return (timeNs, ++sequence);
        }
    }
}
