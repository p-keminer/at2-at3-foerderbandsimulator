using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Plant.Core;

namespace Plant.Adapter
{
    public sealed class DigitalRunLimits
    {
        public int MaxAdvances { get; set; } = 12;
        public double MaxVirtualSeconds { get; set; } = 1;
        public int WallTimeoutSeconds { get; set; } = 15;
        public bool RequireStationary { get; set; } = true;
        public bool RequireCompletedTransport { get; set; }
        public void Validate()
        {
            Numbers.Require(MaxAdvances >= 1 && MaxAdvances <= 50000 && Numbers.Finite(MaxVirtualSeconds) && MaxVirtualSeconds > 0 && MaxVirtualSeconds <= 60,
                "Digitaltest benötigt feste Grenzen: 1…50000 Fortschaltungen, maximal 60 s virtuelle Zeit.");
            Numbers.Require(WallTimeoutSeconds >= 5 && WallTimeoutSeconds <= 300 && !(RequireStationary && RequireCompletedTransport), "Ungültige Digitaltest-/Timeoutgrenzen.");
        }
    }
    public sealed record DigitalSample(CouplingSnapshot Coupling, PlantSnapshot Plant, SensorFrame Published, SyncNotification RawSync);
    public sealed class DigitalRunResult
    {
        public string Profile { get; } = "AT2_Kopplung_7_digital_bits_SingleStep_C";
        public DateTime UtcStarted { get; } = DateTime.UtcNow;
        public bool Passed { get; internal set; }
        public bool TransportCompleted { get; internal set; }
        public bool PlcMeasurementVerified => false; // Optimized DB deliberately not mapped/read here.
        public string MeasurementNote => "SPS-Zähler und Länge nicht per SDK gelesen. Diagnosezählung ist kein SPS-Messwert; getrennten TIA-Nachweis verwenden.";
        public int ContinueCalls { get; internal set; }
        public long DiagnosticSampledOpticalPulses { get; internal set; }
        public long? FirstMotorRightNs { get; internal set; }
        public long? OpticalEnterNs { get; internal set; }
        public long? OpticalLeaveNs { get; internal set; }
        public long? RightSensorNs { get; internal set; }
        public long? MotorStoppedNs { get; internal set; }
        public string EndReason { get; internal set; } = "NotStarted";
        public string? Error { get; internal set; }
        public string? CleanupError { get; internal set; }
        public bool CleanupConfirmed { get; internal set; }
        public EndpointInspection? BeforeDisconnect { get; internal set; }
        public string? InspectionError { get; internal set; }
        public PlantSnapshot? FinalPlant { get; internal set; }
        public CouplingSnapshot? FinalCoupling { get; internal set; }
        public string CpuAfterDispose => "Nicht erneut verbunden; kein automatischer Default-/RUN-/STOP-Wechsel.";
        public List<DigitalSample> Samples { get; } = new();
    }
    // Reuses PlcCouplingController, SyncTimeline and the actual PlantModel. No second communication adapter.
    public static class DigitalIntegrationRun
    {
        public static async Task<DigitalRunResult> ExecuteAsync(IPlcEndpointFactory factory, SignalMap map,
            DigitalCycleProfile profile, PlantConfiguration configuration, DigitalRunLimits limits,
            LiveAuthorization authorization, CancellationToken cancellation = default, Action<DigitalSample>? progress = null)
        {
            authorization.RequireApproved(); profile.RequireMap(map); limits.Validate();
            // Snapshot mutable caller configuration before any asynchronous work.
            profile = profile.Copy(); configuration = profile.ConfigurePlant(configuration);
            limits = Json.Read<DigitalRunLimits>(Json.Write(limits));
            var result = new DigitalRunResult();
            var plant = new ExternalPlantSimulation(configuration);
            var options = new CouplingOptions { DigitalCycle = profile, ConfirmedInputPartitions = new[] { 0u }, InputAdoptionReviewed = true };
            var controller = new PlcCouplingController(factory, plant, options);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(limits.WallTimeoutSeconds));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
            bool gatedHall = false, seenLeft = false; int stoppedPoints = 0;
            try
            {
                await controller.ConnectAsync(map, authorization, stop.Token).ConfigureAwait(false);
                bool next;
                do
                {
                    next = false;
                    await controller.StepAsync(stop.Token, snapshot =>
                    {
                        var physical = plant.Snapshot; var published = plant.Inputs(snapshot.Sequence);
                        var sample = new DigitalSample(snapshot, physical, published, controller.LastNotification!);
                        result.Samples.Add(sample);
                        seenLeft |= physical.Left.Detected;
                        if (snapshot.Applied.MotorRight) result.FirstMotorRightNs ??= snapshot.VirtualTimeNs;
                        if (physical.Optical.Detected) result.OpticalEnterNs ??= snapshot.VirtualTimeNs;
                        if (result.OpticalEnterNs.HasValue && !physical.Optical.Detected) result.OpticalLeaveNs ??= snapshot.VirtualTimeNs;
                        if (physical.Right.Detected) result.RightSensorNs ??= snapshot.VirtualTimeNs;
                        bool level = published.OpticalHigh && published.HallHigh;
                        if (level && !gatedHall) result.DiagnosticSampledOpticalPulses++;
                        gatedHall = level;
                        bool stopped = !snapshot.Applied.MotorLeft && !snapshot.Applied.MotorRight && Math.Abs(physical.MotorVelocity) < 1e-9;
                        if (physical.Right.Detected && stopped) stoppedPoints++; else stoppedPoints = 0;
                        if (stoppedPoints >= 3) result.MotorStoppedNs ??= snapshot.VirtualTimeNs;
                        result.TransportCompleted = seenLeft && result.FirstMotorRightNs.HasValue && result.OpticalEnterNs.HasValue &&
                            result.OpticalLeaveNs.HasValue && result.RightSensorNs.HasValue && result.MotorStoppedNs.HasValue &&
                            result.FirstMotorRightNs <= result.OpticalEnterNs && result.OpticalEnterNs < result.OpticalLeaveNs &&
                            result.OpticalLeaveNs <= result.RightSensorNs && result.RightSensorNs <= result.MotorStoppedNs;
                        progress?.Invoke(sample);
                        if (limits.RequireStationary && (snapshot.Applied.MotorRight || snapshot.Applied.MotorLeft))
                            throw new InvalidOperationException("Motoranforderung im stationären Digitaltest; keine weitere Fortschaltung.");
                        if (limits.RequireCompletedTransport && snapshot.Applied.MotorLeft)
                            throw new InvalidOperationException("Unerwarteter Linksbefehl während der Rechtslauf-Abnahme; keine weitere Fortschaltung.");
                        if (limits.RequireCompletedTransport && result.TransportCompleted) result.EndReason = "TransportCompleted";
                        else if (result.ContinueCalls >= limits.MaxAdvances) result.EndReason = "AdvanceLimit";
                        else if (snapshot.VirtualTimeNs >= limits.MaxVirtualSeconds * 1000000000) result.EndReason = "VirtualTimeLimit";
                        else next = true;
                        return next;
                    }).ConfigureAwait(false);
                    result.ContinueCalls = controller.ContinueCalls;
                } while (next);
                result.Passed = !limits.RequireCompletedTransport || result.TransportCompleted;
                if (!result.Passed) result.Error = "Begrenzung erreicht, ohne vollständigen Förderbanddurchlauf. Kein Erfolg der Messung behauptet.";
            }
            catch (Exception e) { result.Error = (deadline.IsCancellationRequested ? "Wandzeitlimit: " : "") + e; result.EndReason = "FaultOrCanceled"; }
            finally
            {
                try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await controller.DisconnectAsync(cleanup.Token).ConfigureAwait(false); result.CleanupConfirmed = true; }
                catch (Exception e) { result.CleanupError = e.ToString(); }
                result.BeforeDisconnect = controller.BeforeDisconnect; result.InspectionError = controller.InspectionError;
                result.ContinueCalls = controller.ContinueCalls;
                result.FinalPlant = plant.Snapshot; result.FinalCoupling = controller.Snapshot;
                result.Passed &= result.CleanupConfirmed && result.Error == null && result.InspectionError == null;
            }
            return result;
        }
    }
}
