using System;
using System.Collections.Generic;
using Plant.Core;

namespace Plant.Adapter
{
    // Own contracts. Not Siemens SDK types and not an alternate S7 network protocol.
    public interface IPlantIoAdapter : IDisposable
    {
        LinkQuality Quality { get; }
        string Status { get; }
        void PublishSensors(SensorFrame frame);
        ActuatorFrame ReadActuators();
    }
    public sealed record ClockGrant(long TimeUs, long Sequence);
    public interface IControlledClock { ClockGrant NextGrant(); }

    public sealed class OfflineFakeAdapter : IPlantIoAdapter
    {
        private bool disposed;
        private long lastTime = -1, lastSequence = -1;
        public LinkQuality Quality { get; private set; } = LinkQuality.Offline;
        public string Status => "Offline-Fake, kein Siemenskontakt";
        public List<SensorFrame> Published { get; } = new();
        public ActuatorFrame Outputs { private get; set; } = new();
        public void PublishSensors(SensorFrame frame)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OfflineFakeAdapter));
            Numbers.Require(frame.Valid && frame.TimeUs >= lastTime && frame.Sequence > lastSequence, "Ungültiger oder veralteter Eingangssnapshot.");
            lastTime = frame.TimeUs; lastSequence = frame.Sequence;
            if (Published.Count == 1024) Published.RemoveAt(0);
            Published.Add(frame);
        }
        public ActuatorFrame ReadActuators() => Quality == LinkQuality.Offline && !disposed ? Outputs : Outputs with { Valid = false };
        public void Disconnect() { Quality = LinkQuality.Stale; }
        public void Dispose() { disposed = true; Quality = LinkQuality.Disconnected; }
    }
    public sealed class PlcsimAvailability : IPlantIoAdapter
    {
        public LinkQuality Quality => LinkQuality.Unavailable;
        public string Status => "V5.0-Livekopplung gesperrt. SDK-Compileprüfung ist kein Laufzeitnachweis.";
        public void PublishSensors(SensorFrame frame) => throw new InvalidOperationException(Status);
        public ActuatorFrame ReadActuators() => new(Valid: false);
        public void Dispose() { }
    }
}
