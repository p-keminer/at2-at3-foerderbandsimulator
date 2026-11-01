using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plant.Adapter;
using Plant.Core;

internal static partial class Program
{
    private static SignalMap PartialProbeMap()
    {
        var source = SignalMap.Unbound(); source.InstanceName = "TEST_DOUBLE_ONLY"; source.LocalVirtualInstanceConfirmed = true;
        foreach (var id in new[] { "SensorLeftHigh", "LampGreen" })
        {
            var bit = source.Signals.Single(s => s.Id == id);
            bit.ByteOffset = 0; bit.BitOffset = 0; bit.AreaSizeBytes = 4; bit.Confirmed = true;
        }
        return source;
    }
    private static MinimalProbeOptions ProbeOptions() => new() { InputPartition = 0, TiaMinimalProgramConfirmed = true, SyncTimeout = TimeSpan.FromMilliseconds(100), TotalTimeout = TimeSpan.FromSeconds(2) };
    private sealed class MinimalEndpointDouble : IMinimalProbeEndpoint, IMinimalProbeEndpointFactory, IMinimalProbeDiagnosticSource
    {
        public string InstanceName { get; set; } = "TEST_DOUBLE_ONLY";
        public EndpointEvidence Evidence => EndpointEvidence.TestDouble;
        public MinimalSynchronization Synchronization { get; set; }
        public string[] DiagnosticTrace => Calls.ToArray();
        public event Action<SyncNotification>? SyncPoint;
        public event Action<string>? Invalidated;
        public int SyncSubscribers => SyncPoint?.GetInvocationList().Length ?? 0;
        public int InvalidSubscribers => Invalidated?.GetInvocationList().Length ?? 0;
        public int Opens, Disposals, Continues;
        public bool AutomaticPoints = true, HoldLamp, Lamp, Written;
        public Action? OnBegin, OnRead, OnWrite, OnContinue;
        public Exception? OpenError, ReadError, WriteError, ContinueError, DisposeError;
        public readonly List<string> Calls = new();
        public IMinimalProbeEndpoint OpenExisting(MinimalProbeMap map, LiveAuthorization authorization, CancellationToken cancellation)
        { Opens++; Calls.Add("open"); if (OpenError != null) throw OpenError; return this; }
        public void Emit(uint? partition = null, long any = 0, long same = 0, uint count = 1, string? error = null) =>
            SyncPoint?.Invoke(new(partition ?? 0u, any, same, count, error));
        public void Break(string reason) => Invalidated?.Invoke(reason);
        public void BeginSingleStepInputs() { Calls.Add("begin"); if (AutomaticPoints) Emit(); OnBegin?.Invoke(); }
        public bool ReadLampGreen() { Calls.Add("read"); OnRead?.Invoke(); if (ReadError != null) throw ReadError; return Lamp; }
        public void WriteSensorLeftHigh(bool value)
        { Calls.Add("write:" + value); OnWrite?.Invoke(); if (WriteError != null) throw WriteError; Written = value; }
        public void ContinueToNextSyncPoint()
        {
            Calls.Add("continue"); Continues++; OnContinue?.Invoke(); if (ContinueError != null) throw ContinueError;
            if (!HoldLamp) Lamp = Written;
            if (AutomaticPoints) Emit(any: 1000000, same: 1000000);
        }
        public void Dispose() { Disposals++; Calls.Add("dispose"); if (DisposeError != null) throw DisposeError; }
    }
    private static MinimalProbeResult Probe(MinimalEndpointDouble endpoint, MinimalProbeOptions? options = null, CancellationToken token = default) =>
        MinimalProbe.Run(endpoint, MinimalProbeMap.FromConfirmedSignals(PartialProbeMap()), new(true, true), options ?? ProbeOptions(), token);

    private static void RunMinimalProbeTests()
    {
        Test("M01", "Teilmap nur für separates Zweibitprofil; vollständige Validierung bleibt streng", () =>
        {
            var source = PartialProbeMap(); var minimal = MinimalProbeMap.FromConfirmedSignals(source);
            Assert(minimal.InstanceName == source.InstanceName && minimal.SensorLeftHigh.ByteOffset == 0 && minimal.LampGreen.BitOffset == 0, "Auswahl falsch");
            Assert(source.Validate(true).Any(e => e.Contains("SpeedCommandRaw")) && source.Validate(true).Any(e => e.Contains("MotorRight")), "Vollvalidierung gelockert");
            Reject(source.RequireLiveValid);
            source.Signals.Single(s => s.Id == "SensorLeftHigh").ByteOffset = 99; source.InstanceName = "CHANGED";
            Assert(minimal.SensorLeftHigh.ByteOffset == 0 && minimal.InstanceName == "TEST_DOUBLE_ONLY", "Caller konnte Auswahl verändern");
            var two = PartialProbeMap(); two.Signals = two.Signals.Where(s => s.Id == "SensorLeftHigh" || s.Id == "LampGreen").ToArray();
            MinimalProbeMap.FromConfirmedSignals(two); Reject(two.RequireLiveValid);
        });
        Test("M02", "Minimalmap weist fehlende/unbestätigte/falsche Zweibitbindungen zurück", () =>
        {
            Action<SignalBinding>[] invalid = { s => s.Confirmed = false, s => s.ByteOffset = null, s => s.ByteOffset = 1,
                s => s.BitOffset = 1, s => s.BitOffset = null, s => s.AreaSizeBytes = 0, s => s.AreaSizeBytes = 65537,
                s => s.Type = "INT", s => s.Direction = "plc_to_plant", s => s.Area = "Output", s => s.Symbol = "tag" };
            foreach (var mutate in invalid)
            { var source = PartialProbeMap(); mutate(source.Signals.Single(s => s.Id == "SensorLeftHigh")); Reject(() => MinimalProbeMap.FromConfirmedSignals(source)); }
            var duplicate = PartialProbeMap(); duplicate.Signals = duplicate.Signals.Concat(new[] { duplicate.Signals[0] }).ToArray();
            Reject(() => MinimalProbeMap.FromConfirmedSignals(duplicate));
            foreach (var id in new[] { "SensorLeftHigh", "LampGreen" })
            { var source = PartialProbeMap(); source.Signals = source.Signals.Where(s => s.Id != id).ToArray(); Reject(() => MinimalProbeMap.FromConfirmedSignals(source)); }
            var schema = PartialProbeMap(); schema.SchemaVersion = 2; Reject(() => MinimalProbeMap.FromConfirmedSignals(schema));
            var local = PartialProbeMap(); local.LocalVirtualInstanceConfirmed = false; Reject(() => MinimalProbeMap.FromConfirmedSignals(local));
        });
        Test("M03", "Live-/TIA-/Partitions-/Zeitgrenzen vor jedem Factoryzugriff", () =>
        {
            var endpoint = new MinimalEndpointDouble(); var map = MinimalProbeMap.FromConfirmedSignals(PartialProbeMap());
            Reject(() => MinimalProbe.Run(endpoint, map, new(), ProbeOptions()));
            Reject(() => MinimalProbe.Run(endpoint, map, new(true, false), ProbeOptions()));
            foreach (var options in new[] { new MinimalProbeOptions(), new MinimalProbeOptions { TiaMinimalProgramConfirmed = true, InputPartition = 0xffff },
                new MinimalProbeOptions { TiaMinimalProgramConfirmed = true, TotalTimeout = TimeSpan.FromSeconds(21) },
                new MinimalProbeOptions { TiaMinimalProgramConfirmed = true, SyncTimeout = TimeSpan.Zero } })
                Reject(() => MinimalProbe.Run(endpoint, map, new(true, true), options));
            Assert(endpoint.Opens == 0, "Factory trotz Sperre aufgerufen");
        });
        Test("M04", "LOW-HIGH-LOW über genau drei Fortschaltungen; vierter Punkt bleibt eingefroren", () =>
        {
            var endpoint = new MinimalEndpointDouble { Lamp = true }; var result = Probe(endpoint);
            Assert(result.Passed && result.CleanupConfirmed && result.Evidence == EndpointEvidence.TestDouble, "Abschluss falsch");
            Assert(result.Samples.Count == 4 && result.ContinueCalls == 3 && endpoint.Continues == 3, "Unbegrenzte/zusätzliche Fortschaltung");
            Assert(result.LastStage == "Completed" && result.ReceivedSyncEvents == 4, "Abschlussdiagnose falsch");
            Assert(result.Samples.Select(s => s.ExpectedLampGreen).SequenceEqual(new bool?[] { null, false, true, false }), "Erwartungsfolge falsch");
            Assert(result.Samples.Select(s => s.WrittenSensorLeftHigh).SequenceEqual(new bool?[] { false, true, false, null }), "Schreibfolge falsch");
            Assert(result.Samples.Last().VirtualTimeNs == 3000000 && !endpoint.Written, "Zeit/letzter Eingang falsch");
            Assert(endpoint.Calls.SequenceEqual(new[] { "open", "begin", "read", "write:False", "continue", "read", "write:True", "continue",
                "read", "write:False", "continue", "read", "dispose" }), "E/A-Reihenfolge oder Endhalt falsch");
            Assert(endpoint.Disposals == 1 && endpoint.SyncSubscribers == 0 && endpoint.InvalidSubscribers == 0, "Handler/Handle übrig");
            endpoint.Emit(); endpoint.Break("late"); Assert(result.Passed && result.Samples.Count == 4, "Spätes Event wirksam");
        });
        Test("M05", "Konstantes Ausgangsbit besteht den Wechseltest nicht", () =>
        {
            foreach (bool level in new[] { false, true })
            {
                var endpoint = new MinimalEndpointDouble { HoldLamp = true, Lamp = level }; var result = Probe(endpoint);
                Assert(!result.Passed && result.Error!.Contains("LampGreen") && result.CleanupConfirmed, "Statisches Q akzeptiert");
                Assert(endpoint.Continues == (level ? 1 : 2), "Nach fehlgeschlagenem Vergleich weitergelaufen");
                Assert(result.Samples.Last().WrittenSensorLeftHigh == null, "Nach Vergleichsfehler geschrieben");
            }
        });
        Test("M06", "Fehlender P-Punkt endet mit Timeout ohne E/A und ohne Fortschaltung", () =>
        {
            var endpoint = new MinimalEndpointDouble { AutomaticPoints = false }; var options = ProbeOptions(); options.SyncTimeout = TimeSpan.FromMilliseconds(20);
            var result = Probe(endpoint, options);
            Assert(!result.Passed && result.Error!.Contains("Timeout") && result.CleanupConfirmed, "Timeout fehlt");
            Assert(result.Samples.Count == 0 && endpoint.Continues == 0 && !endpoint.Calls.Contains("read"), "Ohne Sync ausgetauscht");
            Assert(result.LastStage == "WaitingForSyncPoint" && result.ReceivedSyncEvents == 0 && result.DiagnosticTrace.Contains("begin"), "Timeoutphase/lokale Diagnose fehlt");
        });
        Test("M07", "Abbruch des wartenden Minimaltests entfernt Handler und verhindert späte Schritte", () =>
        {
            var endpoint = new MinimalEndpointDouble { AutomaticPoints = false }; using var started = new ManualResetEventSlim();
            endpoint.OnBegin = started.Set; using var cancel = new CancellationTokenSource();
            var task = Task.Run(() => Probe(endpoint, token: cancel.Token));
            Assert(started.Wait(1000), "Testdouble nicht gestartet"); cancel.Cancel();
            Assert(task.Wait(1000), "Wartender Test nicht beendet"); var result = task.Result;
            endpoint.Emit(); Assert(!result.Passed && result.CleanupConfirmed && endpoint.Continues == 0 && endpoint.Disposals == 1, "Abbruch wirkungslos");
        });
        Test("M08", "Falsche/zusammengefasste/fehlerhafte Syncpunkte werden ohne I/O verworfen", () =>
        {
            var points = new[] { new SyncNotification(1, 0, 0, 1), new SyncNotification(0, 0, 0, 2), new SyncNotification(0, 1, 1, 1),
                new SyncNotification(0, -1, 0, 1), new SyncNotification(0, 0, 0, 1, "SDK_CODE_DOUBLE"), new SyncNotification(0xffff, 0, 0, 1) };
            foreach (var point in points)
            {
                var endpoint = new MinimalEndpointDouble { AutomaticPoints = false };
                endpoint.OnBegin = () => endpoint.Emit(point.InputPartition, point.SinceAnyNs, point.SinceSameNs, point.Count, point.Error);
                var result = Probe(endpoint); Assert(!result.Passed && result.Samples.Count == 0 && endpoint.Continues == 0, "Ungültiger Punkt verwendet");
            }
        });
        Test("M09", "Invalidierung während Lesen/Schreiben verhindert die nächste CPU-Freigabe", () =>
        {
            foreach (bool read in new[] { true, false })
            {
                var endpoint = new MinimalEndpointDouble();
                if (read) endpoint.OnRead = () => endpoint.Break("STOP_DOUBLE"); else endpoint.OnWrite = () => endpoint.Break("PROJECT_CHANGED_DOUBLE");
                var result = Probe(endpoint);
                Assert(!result.Passed && result.Error!.Contains(read ? "STOP_DOUBLE" : "PROJECT_CHANGED_DOUBLE"), "Originalgrund verloren");
                Assert(endpoint.Continues == 0 && result.CleanupConfirmed, "Nach Invalidierung fortgeschaltet");
            }
        });
        Test("M10", "E/A-/Fortschaltfehler bleiben Fehler mit separatem Cleanupresultat", () =>
        {
            foreach (string operation in new[] { "open", "read", "write", "continue" })
            {
                var endpoint = new MinimalEndpointDouble(); var error = new InvalidOperationException("ORIGINAL_DOUBLE_" + operation);
                if (operation == "open") endpoint.OpenError = error; else if (operation == "read") endpoint.ReadError = error;
                else if (operation == "write") endpoint.WriteError = error; else endpoint.ContinueError = error;
                var result = Probe(endpoint);
                Assert(!result.Passed && result.Error!.Contains("ORIGINAL_DOUBLE_" + operation), "Fehler verschluckt");
                Assert(endpoint.Disposals == (operation == "open" ? 0 : 1), "Falscher Handlebesitz");
                if (operation == "read" || operation == "write") Assert(endpoint.Continues == 0, "Nach E/A-Fehler freigegeben");
                if (operation == "write") Assert(result.Samples.Last().WrittenSensorLeftHigh == false && !result.Samples.Last().InputWriteConfirmed, "Schreibversuch/fehlende Bestätigung nicht getrennt");
                if (operation == "continue") Assert(result.ContinueCalls == 1, "Fehlgeschlagener Fortschaltaufruf nicht protokolliert");
            }
        });
        Test("M11", "Frühe Events, Überlauf und veraltete Punkte dürfen nicht verspätet ausgeführt werden", () =>
        {
            var overflow = new MinimalEndpointDouble { AutomaticPoints = false };
            overflow.OnBegin = () => { for (int i = 0; i < 9; i++) overflow.Emit(); };
            var r = Probe(overflow); Assert(!r.Passed && r.Error!.Contains("übergelaufen") && overflow.Continues == 0, "Überlauf akzeptiert");
            var stale = new MinimalEndpointDouble(); var options = ProbeOptions(); options.SyncTimeout = TimeSpan.FromMilliseconds(10);
            stale.OnBegin = () => Thread.Sleep(30); r = Probe(stale, options);
            Assert(!r.Passed && r.Error!.Contains("veraltet") && !stale.Calls.Contains("read"), "Veraltetes Event verwendet");
            var broken = new MinimalEndpointDouble(); broken.OnBegin = () => broken.Break("BEGIN_FAILED_DOUBLE");
            r = Probe(broken); Assert(!r.Passed && r.Error!.Contains("BEGIN_FAILED_DOUBLE") && broken.Disposals == 1, "Frühe Invalidierung verloren");
        });
        Test("M12", "Cleanupfehler kann keinen erfolgreichen Minimaltest vortäuschen", () =>
        {
            var endpoint = new MinimalEndpointDouble { DisposeError = new InvalidOperationException("DISPOSE_DOUBLE") };
            var result = Probe(endpoint);
            Assert(!result.Passed && !result.CleanupConfirmed && result.CleanupError!.Contains("DISPOSE_DOUBLE"), "Cleanupfehler verborgen");
            Assert(result.Samples.Count == 4 && result.ContinueCalls == 3, "Fachablauf unerwartet verändert");
        });
        Test("M13", "Falsche Instanz und Gesamttimeout verhindern Erfolg", () =>
        {
            var wrong = new MinimalEndpointDouble { InstanceName = "WRONG_DOUBLE" }; var result = Probe(wrong);
            Assert(!result.Passed && !wrong.Calls.Contains("begin") && wrong.Disposals == 1, "Falsche Instanz verwendet");
            var endpoint = new MinimalEndpointDouble(); var options = ProbeOptions();
            options.SyncTimeout = TimeSpan.FromMilliseconds(10); options.TotalTimeout = TimeSpan.FromMilliseconds(20);
            endpoint.OnBegin = () => Thread.Sleep(40); result = Probe(endpoint, options);
            Assert(!result.Passed && result.Error!.Contains("Gesamttimeout") && endpoint.Continues == 0, "Gesamttimeout ignoriert");
        });
        Test("M14", "Launcherargumente strikt; Update 2 erkannt, fremde SDK-Version abgewiesen", () =>
        {
            string[] valid = { "--freigabe-runtime", "--freigabe-cpu-schritte", "--minimal-two-signal", "--confirm-tia-minimal", "--map", "partial.json", "--partition", "0", "--report", "report.json" };
            Assert(ProbeArguments.Parse(valid).Minimal, "Minimalprofil nicht erkannt");
            Reject(() => ProbeArguments.Parse(valid.Where(a => a != "--freigabe-runtime").ToArray()));
            Reject(() => ProbeArguments.Parse(valid.Where(a => a != "--confirm-tia-minimal").ToArray()));
            Reject(() => ProbeArguments.Parse(valid.Concat(new[] { "--steps", "10" }).ToArray()));
            Reject(() => ProbeArguments.Parse(valid.Concat(new[] { "--typo" }).ToArray()));
            Reject(() => ProbeArguments.Parse(valid.Concat(new[] { "--map", "other.json" }).ToArray()));
            Reject(() => ProbeArguments.Parse(valid.Take(valid.Length - 1).ToArray()));
            Assert(ProbeArguments.SupportedSdkVersion("5.0.0.2") && ProbeArguments.SupportedSdkVersion("5.0.0.0"), "Geprüfte Version gesperrt");
            Assert(!ProbeArguments.SupportedSdkVersion("6.0.0.0") && !ProbeArguments.SupportedSdkVersion("5.0.0.99"), "Ungeprüfte Version freigegeben");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Siemens", StringComparison.OrdinalIgnoreCase)), "Siemens im Testprozess geladen");
        });
        Test("M15", "Doppelter Folgepunkt und Invalidierung beim Fortschalten beenden den Versuch", () =>
        {
            var duplicate = new MinimalEndpointDouble { AutomaticPoints = false };
            duplicate.OnBegin = () => duplicate.Emit(); duplicate.OnContinue = () => duplicate.Emit();
            var result = Probe(duplicate);
            Assert(!result.Passed && result.Error!.Contains("Doppelter") && duplicate.Continues == 1 && result.Samples.Count == 1, "Doppelten Punkt ausgeführt");
            var broken = new MinimalEndpointDouble(); broken.OnContinue = () => broken.Break("LOST_DURING_CONTINUE_DOUBLE");
            result = Probe(broken);
            Assert(!result.Passed && result.Error!.Contains("LOST_DURING_CONTINUE_DOUBLE") && broken.Continues == 1, "Fehler nach nativem Fortschaltaufruf verschluckt");
        });
        MinimalProbeOptions CycleOptions()
        {
            var o = ProbeOptions(); o.Synchronization = MinimalSynchronization.CycleControlPoint;
            o.Ob1AutomaticImageConfirmed = true; return o;
        }
        MinimalEndpointDouble CycleEndpoint() => new() { Synchronization = MinimalSynchronization.CycleControlPoint };
        Test("C01", "Explizites C-Minimalprofil: vier beobachtete U2-Punkte ID 0, LOW/HIGH/LOW, drei Fortschaltungen", () =>
        {
            var endpoint = CycleEndpoint(); var r = Probe(endpoint, CycleOptions());
            Assert(r.Passed && r.CleanupConfirmed && r.Samples.Count == 4 && r.ContinueCalls == 3, "C-Ablauf falsch");
            Assert(r.SynchronizationMode == "SingleStep_C" && r.ExpectedSyncPointId == 0 && r.InputPartition == 0, "C-Profil falsch protokolliert");
            Assert(r.Samples.Skip(1).Select(s => s.LampGreen).SequenceEqual(new[] { false, true, false }), "C-Rücklesung falsch");
            Assert(r.Samples.Last().WrittenSensorLeftHigh == null && r.Samples.Last().VirtualTimeNs == 3000000, "Nach letztem Punkt geschrieben/Zeit erfunden");
            Assert(endpoint.Calls.Count(s => s.StartsWith("write:")) == 3 && endpoint.SyncSubscribers == 0, "Zusätzliche Zugriffe/Handler");
        });
        Test("C02", "C erfordert OB1-Automatik; P- und Vollvalidierung bleiben strikt", () =>
        {
            var endpoint = CycleEndpoint(); var options = CycleOptions(); options.Ob1AutomaticImageConfirmed = false;
            Reject(() => Probe(endpoint, options)); Assert(endpoint.Opens == 0, "Vor Bestätigung geöffnet");
            options = CycleOptions(); options.InputPartition = 1; Reject(() => Probe(endpoint, options));
            options = ProbeOptions(); options.InputPartition = 0xffff; Reject(() => Probe(new(), options));
            options = ProbeOptions(); options.Ob1AutomaticImageConfirmed = true; Reject(() => Probe(new(), options));
            Reject(() => new CouplingOptions { InputAdoptionReviewed = true, ConfirmedInputPartitions = new[] { 0xffffu } }.Validate());
            Reject(() => PartialProbeMap().RequireLiveValid());
        });
        Test("C03", "Endpunkt-/Profilverwechslung wird vor der Modusanforderung erkannt", () =>
        {
            foreach (bool cycle in new[] { true, false })
            {
                var endpoint = cycle ? new MinimalEndpointDouble() : CycleEndpoint();
                var r = Probe(endpoint, cycle ? CycleOptions() : ProbeOptions());
                Assert(!r.Passed && !endpoint.Calls.Contains("begin") && endpoint.Disposals == 1, "Falsches Profil benutzt");
            }
        });
        Test("C04", "Fremde ID 1/0xFFFF wird nicht statt beobachteter C-ID 0 akzeptiert", () =>
        {
            foreach (uint id in new[] { 1u, 0xffffu })
            {
                var endpoint = CycleEndpoint(); endpoint.AutomaticPoints = false; endpoint.OnBegin = () => endpoint.Emit(id);
                var r = Probe(endpoint, CycleOptions());
                Assert(!r.Passed && r.ReceivedSyncEvents == 1 && r.Samples.Count == 0 && !endpoint.Calls.Contains("read") && r.ContinueCalls == 0, "Fremde ID verwendet");
            }
        });
        Test("C05", "C-Nullzeit am Folgepunkt oder zusammengefasste Zyklen werden abgewiesen", () =>
        {
            foreach (bool duplicate in new[] { true, false })
            {
                var endpoint = CycleEndpoint(); endpoint.AutomaticPoints = false;
                endpoint.OnBegin = () => endpoint.Emit();
                endpoint.OnContinue = () => { if (duplicate) endpoint.Emit(); else endpoint.Emit(any: 1000000, same: 1000000, count: 2); };
                var r = Probe(endpoint, CycleOptions());
                Assert(!r.Passed && r.Samples.Count == 1 && r.ContinueCalls == 1, "Ungültige C-Zeitbasis angenommen");
            }
        });
        Test("C06", "C-Timeout und fehlender Lampenwechsel bleiben Fehler, kein Modusfallback", () =>
        {
            var endpoint = CycleEndpoint(); endpoint.AutomaticPoints = false; var r = Probe(endpoint, CycleOptions());
            Assert(!r.Passed && r.Error!.Contains("SingleStep_C") && r.ContinueCalls == 0 && r.Samples.Count == 0, "C-Timeout verborgen");
            endpoint = CycleEndpoint(); endpoint.HoldLamp = true; r = Probe(endpoint, CycleOptions());
            Assert(!r.Passed && r.Samples.Count == 3 && r.ContinueCalls == 2, "Konstante Ausgabe als Erfolg ausgegeben");
        });
        Test("C07", "C-CLI verlangt eigenes Profil und Bestätigung; keine Partitionsnummer und kein Vollprofil", () =>
        {
            string[] args = { "--freigabe-runtime", "--freigabe-cpu-schritte", "--minimal-two-signal", "--confirm-tia-minimal", "--cycle-control-point", "--confirm-ob1-automatic", "--map", "partial.json", "--report", "c.json" };
            Assert(ProbeArguments.Parse(args).CycleControlPoint, "C-Profil fehlt");
            Reject(() => ProbeArguments.Parse(args.Where(a => a != "--confirm-ob1-automatic").ToArray()));
            Reject(() => ProbeArguments.Parse(args.Where(a => a != "--cycle-control-point").ToArray()));
            Reject(() => ProbeArguments.Parse(args.Where(a => a != "--minimal-two-signal").ToArray()));
            Reject(() => ProbeArguments.Parse(args.Concat(new[] { "--partition", "65535" }).ToArray()));
            Assert(ProbeArguments.SupportedCycleSdkVersion("5.0.0.2", "50.43.04.00") && !ProbeArguments.SupportedCycleSdkVersion("5.0.0.0", "50.42.10.00") &&
                !ProbeArguments.SupportedCycleSdkVersion("5.0.0.2", "other"), "C-Regel außerhalb beobachteter Runtime erlaubt");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Siemens", StringComparison.OrdinalIgnoreCase)), "Siemenscode in Offlinetests geladen");
        });
        Test("C08", "Echter U2-Erstpunkt setzt lokalen Ursprung; rohe SDK-Zeit bleibt erhalten", () =>
        {
            var endpoint = CycleEndpoint(); endpoint.AutomaticPoints = false;
            endpoint.OnBegin = () => endpoint.Emit(any: 1029096, same: 0);
            endpoint.OnContinue = () => endpoint.Emit(any: 1030000, same: 1030000);
            var r = Probe(endpoint, CycleOptions());
            Assert(r.Passed && r.Samples[0].VirtualTimeNs == 0 && r.Samples[0].RawSinceAnyNs == 1029096, "Erstzeit verloren/als Modellschritt verbucht");
            Assert(r.Samples.Last().VirtualTimeNs == 3090000 && r.Samples.All(s => s.RawSyncPointId == 0 && s.RawSyncPointCount == 1), "SDK-Folgezeit verändert");
            foreach (var bad in new[] { new SyncNotification(0, -1, 0, 1), new SyncNotification(0, 1, 1, 1), new SyncNotification(0, 100000001, 0, 1) })
            {
                endpoint = CycleEndpoint(); endpoint.AutomaticPoints = false; endpoint.OnBegin = () => endpoint.Emit(bad.InputPartition, bad.SinceAnyNs, bad.SinceSameNs, bad.Count);
                r = Probe(endpoint, CycleOptions()); Assert(!r.Passed && r.Samples.Count == 0, "Ungültige Anfangszeit akzeptiert");
            }
        });
        Test("C09", "SDK-Trialwarnung ist kein Zustandsfehler; Lizenzfehler/STOP bleiben gesperrt", () =>
        {
            Assert(RuntimeEventPolicy.IsUsableState(44, "Freeze") && RuntimeEventPolicy.IsUsableState(44, "Run") && RuntimeEventPolicy.IsUsableState(0, "Run"), "Dokumentierte Warnung als Fehler behandelt");
            foreach (int code in new[] { -30, -31, -11, 20, 49, 999 })
                Assert(!RuntimeEventPolicy.IsUsableState(code, "Freeze"), "Unbestätigten Code freigegeben");
            Assert(!RuntimeEventPolicy.IsUsableState(44, "Stop") && !RuntimeEventPolicy.IsUsableState(0, "Off"), "CPU-Zustandswechsel ignoriert");
        });
    }
}
