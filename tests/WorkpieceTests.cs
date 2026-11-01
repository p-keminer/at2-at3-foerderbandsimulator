using System;
using System.Linq;
using System.Threading;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static void RunWorkpieceTests()
    {
        Test("WP01", "Leerer Start ist serialisierbar; bestehende Profile bleiben mit Prüfling", () =>
        {
            Assert(new PlantModel(new()).Snapshot(new()).Present, "Altprofil hat keinen Prüfling");
            var configuration = Json.Read<PlantConfiguration>(Json.Write(new PlantConfiguration { InitialPresent = false }));
            Assert(!new PlantModel(configuration).Snapshot(new()).Present, "Leerer Start ignoriert");
            foreach (double length in new[] { 60.0, 120, 1000 })
            {
                double left = WorkpieceHandling.LeftPosition(configuration, length);
                configuration.ValidateWorkpiece(left, length);
                Assert(left <= configuration.Sensor("left").PositionMm && left + length > configuration.Sensor("left").PositionMm, "Einsetzen nicht am linken Sensor");
            }
            foreach (double bad in new[] { 0, -1, 1001, double.NaN, double.PositiveInfinity }) Reject(() => WorkpieceHandling.LeftPosition(configuration, bad));
        });
        Test("WP02", "Entnehmen links, mittig und rechts; Sensoren folgen, Hall und Modellzeit bleiben erhalten", () =>
        {
            foreach (double position in new[] { 50.0, 450, 850 })
            {
                var plant = new ExternalPlantSimulation(new() { InitialLeftMm = position, HallInitialPhase = .75 });
                plant.AdoptOutputs(new(Sequence: 1));
                var before = plant.Snapshot;
                var removed = plant.ChangeWorkpiece(new() { Id = 1, Action = "Remove" }, 1);
                var after = plant.Snapshot;
                Assert(removed.Applied && !after.Present && !after.Left.Detected && !after.Optical.Detected && !after.Right.Detected, "Entnahme/Sensoren falsch");
                Assert(plant.TimeNs == 0 && after.MotorMm == before.MotorMm && after.BeltMm == before.BeltMm &&
                    after.GeneratedRisingEdges == before.GeneratedRisingEdges && after.Sensors.HallHigh == before.Sensors.HallHigh, "Ein-/Entnahme hat Bewegung oder Hall erzeugt");
                Assert(!plant.ChangeWorkpiece(new() { Id = 2, Action = "Remove" }, 1).Applied, "Leeres Band erneut entnommen");
                var inserted = plant.ChangeWorkpiece(new() { Id = 3, Action = "InsertLeft", LengthMm = 120 }, 1);
                Assert(inserted.Applied && plant.Snapshot.Present && plant.Snapshot.Left.Detected, "Wiedereinsetzen links fehlt");
                Equal(plant.Snapshot.LeftEdgeMm, 40);
                Assert(!plant.ChangeWorkpiece(new() { Id = 4, Action = "InsertLeft", LengthMm = 60 }, 1).Applied, "Vorhandenes Objekt heimlich versetzt");
            }
        });
        Test("WP03", "Motorbefehl, Nachlauf, falsche Sequenz und ungültige Kopplung sperren Prüflingsänderungen", () =>
        {
            foreach (bool right in new[] { true, false })
            {
                var plant = new ExternalPlantSimulation(new() { DynamicMotor = true });
                var drive = new ActuatorFrame(right, !right, Analog.Nominal, Sequence: 1);
                plant.AdoptOutputs(drive);
                Assert(!plant.ChangeWorkpiece(new() { Id = 1, Action = "Remove" }, 1).Applied, "Aktiver Befehl bei noch null Geschwindigkeit durchgelassen");
                plant.AdvanceTo(1000000, drive, CancellationToken.None);
                plant.AdoptOutputs(new(Sequence: 2, TimeUs: 1000));
                Assert(!plant.ChangeWorkpiece(new() { Id = 2, Action = "Remove" }, 2).Applied && plant.Snapshot.Present, "Nachlauf ignoriert");
            }
            Assert(!WorkpieceHandling.IsStopped(true, false, 100, 0), "Blockierter Prüfling ist kein stehendes Band");
            var stationary = new ExternalPlantSimulation(new());
            Assert(!stationary.ChangeWorkpiece(new() { Id = 1, Action = "Remove" }, 1).Applied, "Unbestätigter Anfangsbefehl erlaubt Änderung");
            stationary.AdoptOutputs(new(Sequence: 2));
            Assert(!stationary.ChangeWorkpiece(new() { Id = 2, Action = "Remove" }, 1).Applied, "Alte Sequenz erlaubt Änderung");
            stationary.Invalidate();
            Assert(!stationary.ChangeWorkpiece(new() { Id = 3, Action = "Remove" }, 2).Applied, "Invalidierte Kopplung erlaubt Änderung");
        });
        AsyncTest("WP04", "Neuer SPS-Fahrbefehl zwischen Klick und Synchronisation weist Entnahme ab", async () =>
        {
            var channel = new WorkpieceCommandChannel();
            var endpoint = new DigitalEndpointDouble { Automatic = false, OnContinue = e => e.Right = true };
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new() { HallPitchMm = 1.2 },
                new() { RequireStationary = false, MaxAdvances = 2 }, FakeApproval, progress: sample =>
                { if (sample.Coupling.Sequence == 1) channel.Enqueue(new() { Id = 1, Action = "Remove" }); }, workpieceCommands: channel);
            Assert(result.Passed && result.WorkpieceChanges.Count == 1 && !result.WorkpieceChanges[0].Applied, "Neue Motorausgabe nicht vor Entnahme geprüft");
            Assert(result.Samples.All(s => s.Plant.Present) && result.ContinueCalls == 2, "Abgewiesene Aktion veränderte Objekt oder Schrittgrenze");
        });
        AsyncTest("WP05", "Einsetzen auf leerem Band veröffentlicht Sensoren vor CPU-Fortschaltung und ermöglicht echte Modellfahrt", async () =>
        {
            var channel = new WorkpieceCommandChannel(); channel.Enqueue(new() { Id = 1, Action = "InsertLeft", LengthMm = 120 });
            var endpoint = new DigitalEndpointDouble();
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new() { InitialPresent = false, HallPitchMm = 1.2 },
                new() { RequireStationary = false, RequireCompletedTransport = true, MaxAdvances = 15000, MaxVirtualSeconds = 15, WallTimeoutSeconds = 30 },
                FakeApproval, workpieceCommands: channel);
            Assert(result.Passed && result.TransportCompleted && result.WorkpieceChanges.Single().Applied, "Fahrt nach Einsetzen nicht abgeschlossen");
            var first = result.Samples.First();
            Assert(first.Plant.Present && first.Published.SensorLeftHigh && first.WorkpieceChange?.Sequence == first.Coupling.Sequence &&
                first.WorkpieceChange.TimeNs == first.Coupling.VirtualTimeNs && first.Plant.MotorMm == 0, "Bestätigung/Sensoren nicht am selben Synchronisationspunkt");
            Assert(!result.PlcMeasurementVerified, "Testdouble wurde als nativer Siemens-Nachweis ausgegeben");
        });
        AsyncTest("WP06", "Fehlgeschriebene SPS-Eingänge liefern keine erfolgreiche Prüflingsbestätigung", async () =>
        {
            var channel = new WorkpieceCommandChannel(); channel.Enqueue(new() { Id = 1, Action = "Remove" });
            var endpoint = new DigitalEndpointDouble { Automatic = false, FailWrite = true };
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new(), new(), FakeApproval, workpieceCommands: channel);
            Assert(!result.Passed && result.WorkpieceChanges.Count == 0 && result.Samples.Count == 0 && result.ContinueCalls == 0,
                "Aktion trotz fehlgeschlagener Eingangsübernahme bestätigt");
            Assert(result.CleanupConfirmed, "Fehlgeschlagener Austausch nicht bereinigt");
        });
        Test("WP07", "Befehle sind begrenzt, einmalig und laufen ohne spätes Einsetzen ab", () =>
        {
            var channel = new WorkpieceCommandChannel();
            Reject(() => channel.Enqueue(new() { Id = 0, Action = "Remove" }));
            Reject(() => channel.Enqueue(new() { Id = 1, Action = "MotorRight" }));
            channel.Enqueue(new() { Id = 1, Action = "Remove" });
            Reject(() => channel.Enqueue(new() { Id = 2, Action = "Remove" }));
            var plant = new ExternalPlantSimulation(new(), channel); plant.AdoptOutputs(new(Sequence: 1));
            Thread.Sleep(2100); plant.ApplyPendingCommands(1);
            var expired = channel.TakePublishedResult();
            Assert(expired != null && !expired.Applied && plant.Snapshot.Present && channel.TakePublishedResult() == null, "Veralteter Befehl ausgeführt oder doppelt bestätigt");
            Reject(() => channel.Enqueue(new() { Id = 1, Action = "Remove" }));
        });
    }
}
