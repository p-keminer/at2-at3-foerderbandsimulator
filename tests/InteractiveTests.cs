using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plant.Adapter;
using Plant.Core;
using Plant.Wpf;

internal static partial class Program
{
    private static DigitalRunLimits InteractiveLimits() => new() { InteractiveSession = true, RequireStationary = false,
        MaxAdvances = 2, MaxVirtualSeconds = .001, WallTimeoutSeconds = 5 };
    private static void RunInteractiveTests()
    {
        Test("I10", "CPU-Anzeige verwirft alte RUN-Bestätigung bei neuer oder gestörter Sitzung und erhält die Rückstellung", () =>
        {
            var view = new CpuStatusIndicator(); var now = new DateTime(2026, 11, 1, 20, 0, 0);
            var idle = new LiveProcessSnapshot(LiveProcessState.Disconnected, "", null, "");
            Assert(view.Describe(idle, false, now).Text.Contains("nicht bestätigt"), "Start behauptet CPU-Zustand");
            view.RecoveryFinished("Run", now);
            var green = view.Describe(idle, false, now);
            Assert(green.Text.Contains("RUN / Default") && green.Text.Contains("20:00:00"), "Bestätigter Zeitpunkt fehlt");
            var active = idle with { IsActive = true, State = LiveProcessState.Streaming, Latest = new() { Sequence = 1 }, ReportPath = "r.json" };
            Assert(view.Describe(active, false, now).Background != green.Background, "Alte RUN/Default-Farbe trotz neuer Kopplung");
            Assert(view.Describe(active with { State = LiveProcessState.Faulted }, false, now).Text.Contains("unbestätigt"), "Störung als gesunde Kopplung angezeigt");
            var frozen = active with { IsActive = false, State = LiveProcessState.Faulted, CleanupConfirmed = true, CpuBeforeDisconnect = "Freeze / SingleStep_C" };
            var warning = view.Describe(frozen, false, now);
            Assert(warning.Text.Contains("FREEZE") && warning.Background != green.Background, "Freeze nicht markiert");
            view.RecoveryFinished("Run", now.AddSeconds(1));
            Assert(view.Describe(frozen, false, now).Text.Contains("RUN / Default"), "Alter Laufbericht überschreibt erfolgreiche Rückstellung");
            view.RecoveryFinished(null, now);
            Assert(view.Describe(frozen, false, now).Text.Contains("nicht bestätigt"), "Fehlgeschlagene Rückstellung bleibt grün");
        });
        Test("I09", "Default-Rückstellung bestätigt STOP getrennt von RUN und lehnt widersprüchliche Berichte ab", () =>
        {
            string Report(string before, string after, bool run, bool stop, bool cleanup = true) => Json.Write(new {
                passed = true, operation = "ExplicitRestoreDefault", instance = "AT2_Kopplung", observedName = "AT2_Kopplung",
                sdkProductVersion = "5.0.0.2", stage = "Completed", cleanupConfirmed = cleanup,
                beforeState = before, afterState = after, afterMode = "Default", defaultAndRunObserved = run, defaultAndStopObserved = stop });
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Stop", "Stop", false, true)) == "Stop", "STOP-Erfolg verloren");
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Freeze", "Run", true, false)) == "Run", "RUN-Erfolg verloren");
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Stop", "Run", true, false)) == null, "STOP muss STOP bleiben");
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Stop", "Stop", false, false)) == null, "STOP nicht zurückgelesen");
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Freeze", "Stop", false, true)) == null, "Unerwarteter STOP akzeptiert");
            Assert(DefaultRecoveryReport.ConfirmedState(Report("Stop", "Stop", false, true, false)) == null, "Cleanup fehlt");
        });
        Test("I01", "Interaktiver Modus ist ausdrücklich gewählt; alte Prüfgrenzen und Freigaben bleiben streng", () =>
        {
            InteractiveLimits().Validate();
            Reject(() => new DigitalRunLimits { InteractiveSession = true }.Validate());
            Reject(() => new DigitalRunLimits { InteractiveSession = true, RequireStationary = false, RequireCompletedTransport = true }.Validate());
            string[] args = { "--freigabe-runtime", "--freigabe-cpu-schritte", "--digital-plant", "--confirm-tia-digital", "--cycle-control-point", "--confirm-ob1-automatic",
                "--map", "map.json", "--profile", "profile.json", "--plant", "plant.json", "--report", "out.json", "--interactive", "--stream", "--allow-movement" };
            Assert(ProbeArguments.Parse(args).Has("--interactive"), "Interaktiver Modus verloren");
            Reject(() => ProbeArguments.Parse(args.Where(a => a != "--stream").ToArray()));
            Reject(() => ProbeArguments.Parse(args.Where(a => a != "--allow-movement").ToArray()));
            Reject(() => ProbeArguments.Parse(args.Concat(new[] { "--require-transport" }).ToArray()));
        });
        AsyncTest("I02", "Verbinden leer, zweimal einsetzen/fahren/entnehmen ohne Neuverbindung, explizit trennen", async () =>
        {
            var endpoint = new DigitalEndpointDouble { Deltas = new[] { 1000000L } };
            var channel = new WorkpieceCommandChannel();
            var config = new PlantConfiguration { InitialPresent = false, WorkpieceLengthMm = 20, HallPitchMm = 1.2 };
            config.Sensor("left").PositionMm = 10; config.Sensor("optical").PositionMm = 50; config.Sensor("right").PositionMm = 100;
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            int phase = 0, changed = 0, completed = 0, idleAtEnd = 0;
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), config, InteractiveLimits(), FakeApproval, cancel.Token, sample =>
            {
                if (sample.WorkpieceChange != null) { Assert(sample.WorkpieceChange.Applied, sample.WorkpieceChange.Detail); changed++; }
                if (phase == 0)
                {
                    Assert(!sample.Plant.Present && !sample.Published.SensorLeftHigh && !sample.Coupling.Applied.MotorRight, "Verbindung setzt Prüfling oder Motor eigenmächtig");
                    if (sample.Coupling.Sequence >= 12) { channel.Enqueue(new() { Id = 1, Action = "InsertLeft", LengthMm = 20 }); phase = 1; }
                }
                else if ((phase == 1 || phase == 3) && sample.Plant.Right.Detected &&
                    WorkpieceHandling.IsStopped(sample.Coupling.Applied.MotorRight, sample.Coupling.Applied.MotorLeft, sample.Plant.MotorVelocity, sample.Plant.ObjectVelocity))
                {
                    if (++idleAtEnd < 10) return; // A completed transport must not close the connection.
                    completed++; idleAtEnd = 0;
                    channel.Enqueue(new() { Id = phase + 1, Action = "Remove" }); phase++;
                }
                else if (phase == 2 && !sample.Plant.Present && sample.WorkpieceChange == null)
                { channel.Enqueue(new() { Id = 3, Action = "InsertLeft", LengthMm = 20 }); phase = 3; }
                else if (phase == 4 && !sample.Plant.Present && sample.WorkpieceChange == null)
                { phase = 5; cancel.Cancel(); }
            }, channel);
            Assert(phase == 5 && completed == 2 && changed == 4, "Interaktive Wiederholung nicht abgeschlossen");
            Assert(result.Passed && result.EndReason == "Disconnected" && result.Error == null && result.CleanupConfirmed, "Normales Trennen als fehlgeschlagener Test gewertet: " + result.Error);
            Assert(endpoint.Opens == 1 && endpoint.Disposals == 1 && result.ContinueCalls > 1000, "Sitzung vorzeitig beendet/neu verbunden");
            Assert(!result.FinalPlant!.Present && result.Samples.Any(s => s.Published.OpticalHigh), "Sensorweg/Entnahme fehlt");
        });
        AsyncTest("I03", "Dauersitzung hält Berichtspeicher begrenzt und ignoriert nur die Grenzen der Abnahmetests", async () =>
        {
            var endpoint = new DigitalEndpointDouble { Automatic = false, Deltas = new[] { 1000L } };
            var channel = new WorkpieceCommandChannel(); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            long commandId = 0;
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new() { InitialPresent = false, HallPitchMm = 1.2 },
                InteractiveLimits(), FakeApproval, cancel.Token, sample =>
                {
                    if (sample.Coupling.Sequence == 6000) { cancel.Cancel(); return; }
                    if (sample.Coupling.Sequence % 4 == 0)
                        channel.Enqueue(new() { Id = ++commandId, Action = sample.Plant.Present ? "Remove" : "InsertLeft", LengthMm = 120 });
                }, channel);
            Assert(result.Passed && result.TotalSamples == 6000 && result.SamplesTruncated && result.Samples.Count <= 4096, "Dauerbericht wächst unbegrenzt oder endet am Testlimit");
            Assert(result.WorkpieceChanges.Count <= 256 && result.OmittedWorkpieceChanges > 0 && result.Samples.Last().Coupling.Sequence == 6000, "Befehlshistorie unbegrenzt oder letzter Wert verloren");
        });
        AsyncTest("I04", "Einsetzen bei bestehendem SPS-Motorbefehl bleibt gesperrt; der Befehl wird nicht überschrieben", async () =>
        {
            var endpoint = new DigitalEndpointDouble { Right = true, OnContinue = e => e.Right = true };
            var channel = new WorkpieceCommandChannel(); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new() { InitialPresent = false, HallPitchMm = 1.2 },
                InteractiveLimits(), FakeApproval, cancel.Token, sample =>
                {
                    if (sample.Coupling.Sequence == 1) channel.Enqueue(new() { Id = 1, Action = "InsertLeft", LengthMm = 120 });
                    if (sample.WorkpieceChange != null) { Assert(!sample.WorkpieceChange.Applied, "Einsetzen auf laufendem Band erlaubt"); cancel.Cancel(); }
                }, channel);
            Assert(result.Passed && !result.FinalPlant!.Present && result.Samples.All(s => s.Coupling.Applied.MotorRight), "SPS-Befehl maskiert oder Prüfling erfunden");
            Assert(LiveStatusText.Outcome(new(LiveProcessState.Streaming, "fixture", DigitalProgress.From(result.Samples.Last()), IsActive: true, InteractiveSession: true)).Contains("SPS befiehlt Motor EIN"), "Ursache der Einsetzsperre nicht sichtbar");
        });
        AsyncTest("I05", "Lebenszeichen, normales Trennen und verlorener Bedienkanal werden unterschieden", async () =>
        {
            using (var alive = new InteractiveControlSession(new(), true, 500))
            {
                for (int i = 0; i < 3; i++) { alive.Accept("PING"); await Task.Delay(50); }
                Assert(!alive.Token.IsCancellationRequested, "Gültiger Bedienkanal beendet");
                alive.RequestStop(); alive.EndInput(); Assert(alive.Token.IsCancellationRequested && alive.Failure == null, "STOP als Kanalfehler behandelt");
            }
            using var lost = new InteractiveControlSession(new(), true, 100);
            await Task.Delay(500);
            Assert(lost.Token.IsCancellationRequested && lost.Failure != null, "Verwaiste Verbindung bleibt aktiv");
            using var eof = new InteractiveControlSession(new(), false); eof.EndInput();
            Assert(eof.Token.IsCancellationRequested && eof.Failure != null, "Kanalende als regulärer Erfolg behandelt");
        });
        Test("I06", "Sauber beendeter eigener Freeze kann auch nach Testlimit freigegeben werden; fremde Zustände bleiben gesperrt", () =>
        {
            var own = new LiveProcessSnapshot(LiveProcessState.Faulted, "limit", new DigitalProgress { Sequence = 12 }, "report.json",
                CleanupConfirmed: true, CpuBeforeDisconnect: "Freeze / SingleStep_C", ExitCode: 1, EndReason: "VirtualTimeLimit", FreezeOwnershipConfirmed: true);
            Assert(LiveSessionPolicy.CanRestoreOwnFreeze(own), "Eigener sauber bereinigter Testlimit-Freeze bleibt hängen");
            Assert(LiveSessionPolicy.CanRestoreOwnFreeze(own with { CpuBeforeDisconnect = "Stop / SingleStep_C", EndReason = "FaultOrCanceled" }), "CPU-STOP lässt eigenen Synchronisierungsmodus zurück");
            Assert(LiveSessionPolicy.CanRestoreOwnFreeze(own with { CpuBeforeDisconnect = "Run / SingleStep_C" }), "Eigener RUN/SingleStep_C wird nicht freigegeben");
            Assert(!LiveSessionPolicy.CanRestoreOwnFreeze(own with { CpuBeforeDisconnect = "Stop / SingleStep_P" }), "Anderer Modus automatisch übernommen");
            foreach (var invalid in new[] { own with { FreezeOwnershipConfirmed = false }, own with { Latest = null }, own with { HardKilled = true },
                own with { CleanupConfirmed = false }, own with { CpuBeforeDisconnect = "Run / Default" }, own with { IsActive = true }, own with { ExitCode = 2 } })
                Assert(!LiveSessionPolicy.CanRestoreOwnFreeze(invalid), "Fremder/unbestätigter Zustand freigegeben");
        });
        AsyncTest("I07", "Bedienkanalfehler bleibt Fehler; leere Startanlage wird vor Endpunktzugriff verlangt", async () =>
        {
            var endpoint = new DigitalEndpointDouble(); var channel = new WorkpieceCommandChannel();
            bool rejected = false;
            try { await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new(), InteractiveLimits(), FakeApproval, workpieceCommands: channel); }
            catch (ArgumentException) { rejected = true; }
            Assert(rejected && endpoint.Opens == 0, "Interaktive Verbindung mit vorbelegtem Prüfling gestartet");
            using var control = new InteractiveControlSession(channel, false);
            var result = await DigitalIntegrationRun.ExecuteAsync(endpoint, DigitalMap(), DigitalProfile(), new() { InitialPresent = false, HallPitchMm = 1.2 },
                InteractiveLimits(), FakeApproval, control.Token, _ => control.Fail("Fixture: Bedienkanal verloren"), channel, () => control.Failure);
            Assert(!result.Passed && result.Error!.Contains("Bedienkanal verloren") && result.CleanupConfirmed, "Bedienkanalfehler als erfolgreiches Trennen behandelt");
        });
    }
}
