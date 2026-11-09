using System;
using System.Threading;
using Plant.Core;

namespace Plant.Adapter
{
    // Reuses the existing plant kernel, WITHOUT a ReferenceController or offline wall-time scheduler.
    public sealed class ExternalPlantSimulation : IExternallyTimedPlant, IAdoptedOutputObserver, IInputBoundaryCommands
    {
        private readonly PlantModel model;
        private readonly PlantConfiguration configuration;
        private readonly object gate = new();
        private volatile bool valid = true;
        private ActuatorFrame adopted = new(Valid: false);
        private long generatedEdges;
        private readonly WorkpieceCommandChannel? workpieceCommands;
        private readonly PotentiometerControl? potentiometer;
        public ExternalPlantSimulation(PlantConfiguration configuration, WorkpieceCommandChannel? workpieceCommands = null, PotentiometerControl? potentiometer = null)
        { this.configuration = configuration.Copy(); this.workpieceCommands = workpieceCommands; this.potentiometer = potentiometer; model = new(this.configuration); if (potentiometer != null) model.PotiRaw = potentiometer.RequestedRaw; model.Edge += _ => generatedEdges++; }
        public long TimeNs { get { lock (gate) return model.TimeNs; } }
        public PlantSnapshot Snapshot { get { lock (gate) return model.Snapshot(adopted with { Valid = adopted.Valid && valid }); } }
        public void AdvanceTo(long targetNs, ActuatorFrame heldCommand, CancellationToken cancellation)
        {
            lock (gate)
            {
                Numbers.Require(valid && heldCommand.Valid && targetNs >= model.TimeNs, "Ungültige Zeit-/Befehlsfreigabe.");
                long elapsed = targetNs - model.TimeNs;
                long edgesBefore = generatedEdges;
                double peakSpeed = Math.Abs(model.Snapshot(heldCommand).MotorVelocity);
                adopted = heldCommand;
                while (model.TimeNs < targetNs)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!valid) throw new InvalidOperationException("Externe Anlage ungültig; neue Sitzung erforderlich.");
                    model.StepNanoseconds(heldCommand, Math.Min(configuration.ModelStepUs * 1000, targetNs - model.TimeNs));
                    peakSpeed = Math.Max(peakSpeed, Math.Abs(model.Snapshot(heldCommand).MotorVelocity));
                    if (model.Snapshot(heldCommand).ObjectOffBelt) throw new InvalidOperationException("ObjectOffBelt: lokale Kopplung angehalten.");
                }
                // Strict range based on ACTUAL synchronization gap, not configured offline sampling.
                if (generatedEdges - edgesBefore > 1 || peakSpeed * elapsed / 1000000000.0 > configuration.HallPitchMm / 4)
                    throw new InvalidOperationException("Aliasing: tatsächlicher Synchronisationsabstand verletzt p/(4·|v|); keine Impulsnachlieferung.");
            }
        }
        public SensorFrame Inputs(long sequence)
        { lock (gate) return model.Sensors with { Sequence = sequence, Valid = valid }; }
        public void Invalidate() => valid = false;
        public void AdoptOutputs(ActuatorFrame command) { lock (gate) adopted = command; }
        public void ApplyPendingCommands(long sequence)
        {
            workpieceCommands?.ApplyAtInputBoundary(this, sequence);
            if (potentiometer != null) lock (gate) model.PotiRaw = potentiometer.RequestedRaw;
        }
        public WorkpieceCommandResult ChangeWorkpiece(WorkpieceCommand command, long sequence)
        {
            lock (gate)
            {
                var answer = new WorkpieceCommandResult { Id = command.Id, Sequence = sequence, TimeNs = model.TimeNs };
                try
                {
                    command.Validate();
                    var state = model.Snapshot(adopted);
                    Numbers.Require(valid && adopted.Valid && adopted.Sequence == sequence,
                        "Kein gültiger aktueller SPS-Synchronisationspunkt.");
                    Numbers.Require(WorkpieceHandling.IsStopped(adopted.MotorRight, adopted.MotorLeft, state.MotorVelocity, state.ObjectVelocity),
                        "Prüfling nur bei Stillstand und ausgeschalteten Motorbefehlen ändern.");
                    if (command.Action == "InsertLeft")
                    {
                        Numbers.Require(!state.Present, "Zuerst den vorhandenen Prüfling entfernen.");
                        model.Insert(WorkpieceHandling.LeftPosition(configuration, command.LengthMm), command.LengthMm, configuration.Metallic, configuration.Reflective);
                        answer.Detail = "Prüfling links eingesetzt.";
                    }
                    else
                    {
                        Numbers.Require(state.Present, "Kein Prüfling auf dem Förderband.");
                        model.Remove(); answer.Detail = "Prüfling entfernt.";
                    }
                    answer.Applied = true;
                }
                catch (ArgumentException e) { answer.Detail = e.Message; }
                return answer;
            }
        }
    }
}
