using System;
using System.Threading;
using Plant.Core;

namespace Plant.Adapter
{
    // Reuses the existing plant kernel, WITHOUT a ReferenceController or offline wall-time scheduler.
    public sealed class ExternalPlantSimulation : IExternallyTimedPlant, IAdoptedOutputObserver
    {
        private readonly PlantModel model;
        private readonly PlantConfiguration configuration;
        private readonly object gate = new();
        private volatile bool valid = true;
        private ActuatorFrame adopted = new(Valid: false);
        private long generatedEdges;
        public ExternalPlantSimulation(PlantConfiguration configuration)
        { this.configuration = configuration.Copy(); model = new(this.configuration); model.Edge += _ => generatedEdges++; }
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
    }
}
