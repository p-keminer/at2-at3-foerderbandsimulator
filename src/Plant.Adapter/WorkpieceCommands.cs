using System;
using System.Diagnostics;
using Plant.Core;

namespace Plant.Adapter
{
    public sealed class WorkpieceCommand
    {
        public long Id { get; set; }
        public string Action { get; set; } = "";
        public double LengthMm { get; set; }
        public void Validate()
        {
            Numbers.Require(Id > 0 && (Action == "InsertLeft" || Action == "Remove"), "Ungültiger Prüflingsbefehl.");
            Numbers.Require(Action != "InsertLeft" || (Numbers.Finite(LengthMm) && LengthMm > 0), "Positive Prüflingslänge erforderlich.");
        }
    }
    public sealed class WorkpieceCommandResult
    {
        public long Id { get; set; }
        public long Sequence { get; set; }
        public long TimeNs { get; set; }
        public bool Applied { get; set; }
        public string Detail { get; set; } = "";
        public WorkpieceCommandResult Copy() => (WorkpieceCommandResult)MemberwiseClone();
    }
    public static class WorkpieceHandling
    {
        // A zero object velocity alone is insufficient: a blocked object can sit on a moving belt.
        public static bool IsStopped(bool motorRight, bool motorLeft, double motorVelocity, double objectVelocity) =>
            !motorRight && !motorLeft && Numbers.Finite(motorVelocity) && Numbers.Finite(objectVelocity) &&
            Math.Abs(motorVelocity) < 1e-9 && Math.Abs(objectVelocity) < 1e-9;
        public static double LeftPosition(PlantConfiguration configuration, double length)
        {
            configuration.ValidateWorkpiece(0, length);
            return Math.Min(configuration.BeltLengthMm - length, Math.Max(0, configuration.Sensor("left").PositionMm - length / 2));
        }
    }
    // One bounded pending command, scoped to one owned LiveProbe process. No SDK calls or model
    // changes on the stdin thread; consumption happens at the fresh input synchronization boundary.
    public sealed class WorkpieceCommandChannel
    {
        private readonly object gate = new();
        private WorkpieceCommand? pending;
        private WorkpieceCommandResult? result;
        private long received, lastId;
        public void Enqueue(WorkpieceCommand command)
        {
            command.Validate();
            lock (gate)
            {
                Numbers.Require(pending == null && result == null && command.Id > lastId, "Prüflingsbefehl bereits offen oder doppelt.");
                pending = new() { Id = command.Id, Action = command.Action, LengthMm = command.LengthMm };
                lastId = command.Id; received = Stopwatch.GetTimestamp();
            }
        }
        internal void ApplyAtInputBoundary(ExternalPlantSimulation plant, long sequence)
        {
            lock (gate)
            {
                if (pending == null) return;
                var command = pending; pending = null;
                result = (Stopwatch.GetTimestamp() - received) / (double)Stopwatch.Frequency > 2
                    ? new() { Id = command.Id, Sequence = sequence, TimeNs = plant.TimeNs, Detail = "Prüflingsbefehl veraltet; nicht übernommen." }
                    : plant.ChangeWorkpiece(command, sequence);
            }
        }
        public WorkpieceCommandResult? TakePublishedResult()
        { lock (gate) { var value = result; result = null; return value; } }
    }
    public interface IInputBoundaryCommands { void ApplyPendingCommands(long sequence); }
}
