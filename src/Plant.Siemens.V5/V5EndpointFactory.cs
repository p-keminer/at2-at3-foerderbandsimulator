using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Plant.Adapter;
using Plant.Core;
using Siemens.Simatic.Simulation.Runtime;

namespace Plant.Siemens.V5
{
    // Optional. The offline EXE never references/instantiates this class.
    // Full P-profile remains unverified. The separate two-bit C probe passed on 2026-11-01.
    public sealed class V5EndpointFactory : IPlcEndpointFactory
    {
        private readonly DigitalCycleProfile? digital;
        public V5EndpointFactory() { }
        public V5EndpointFactory(DigitalCycleProfile profile) { profile.Validate(); digital = profile.Copy(); }
        public IPlcEndpoint OpenExisting(SignalMap map, LiveAuthorization authorization, CancellationToken cancellation)
        {
            authorization.RequireApproved();
            if (digital == null) map.RequireLiveValid(); else digital.RequireMap(map);
            cancellation.ThrowIfCancellationRequested();
            return OpenApproved(map, cancellation, digital);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IPlcEndpoint OpenApproved(SignalMap map, CancellationToken cancellation, DigitalCycleProfile? digital)
        {
            var session = V5Session.Open(map.InstanceName!, cancellation);
            try { return new V5Endpoint(session, map, digital); }
            catch (Exception original)
            {
                try { session.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("E/A-Aufbau und Handlebereinigung fehlgeschlagen.", original, cleanup); }
                throw;
            }
        }
    }

    // Shared lifecycle only. The full and minimal E/A contracts remain separate.
    internal sealed class V5Session : IDisposable
    {
        internal static V5Session Open(string instanceName, CancellationToken cancellation)
        {
            // The FIRST use of this static Siemens class initializes its API (V5 manual p.116).
            // Thus it must remain behind explicit approval, even for opening an existing instance.
            IInstance? instance = null;
            try
            {
                instance = SimulationRuntimeManager.CreateInterface(instanceName);
                if (instance == null) throw new InvalidOperationException("Die ausdrücklich gewählte lokale virtuelle Instanz existiert nicht.");
                cancellation.ThrowIfCancellationRequested();
                return new V5Session(instance);
            }
            catch (Exception original)
            {
                try { instance?.Dispose(); } // Interface only. NEVER UnregisterInstance/Shutdown/PowerOff/STOP.
                catch (Exception cleanup)
                {
                    string Describe(Exception e) => e is SimulationRuntimeException sdk ? "Originalcode=" + sdk.RuntimeErrorCode + "; " + e.Message : e.Message;
                    throw new AggregateException("Aufbau fehlgeschlagen: " + Describe(original) + "; Handlebereinigung unbestätigt: " + Describe(cleanup), original, cleanup);
                }
                if (original is SimulationRuntimeException sdk)
                    throw new InvalidOperationException("V5 CreateInterface: Originalcode=" + sdk.RuntimeErrorCode + "; " + sdk.Message, sdk);
                throw;
            }
        }
        internal readonly IInstance Instance;
        private readonly Delegate_II_EREC_DT_UINT32_INT64_INT64_UINT32 syncHandler;
        private readonly Delegate_II_EREC_DT_EOS_EOS stateHandler;
        private readonly Delegate_II_EREC_DT hardwareHandler;
        private readonly Delegate_SoftwareConfigurationChanged softwareHandler;
        private readonly Delegate_Void lostHandler;
        private bool subscribed;
        private EOperatingMode requestedMode = EOperatingMode.SingleStep_P;
        private int disposed;
        private readonly object traceGate = new();
        private readonly List<string> trace = new();
        internal string[] DiagnosticTrace { get { lock (traceGate) return trace.ToArray(); } }
        private void Trace(string message)
        {
            lock (traceGate) { if (trace.Count == 32) trace.RemoveAt(0); trace.Add(message); }
        }
        public string InstanceName => Sdk(() => Instance.Name);
        public EndpointEvidence Evidence => EndpointEvidence.SiemensLiveUnverified;
        public event Action<SyncNotification>? SyncPoint;
        public event Action<string>? Invalidated;
        private V5Session(IInstance instance)
        {
            Instance = instance;
            syncHandler = (_, code, date, id, same, any, count) =>
            {
                Trace($"OnSyncPointReached: code={code}, id={id}, sinceAnyNs={any}, sinceSameNs={same}, count={count}");
                SyncPoint?.Invoke(new(id, any, same, count, code == ERuntimeErrorCode.OK ? null : code.ToString()));
            };
            stateHandler = (_, code, date, previous, next) =>
            {
                Trace($"OnOperatingStateChanged: {previous}->{next}, code={code}");
                // Keep the warning visible in Trace. Do not activate/renew/bypass a license.
                if (!RuntimeEventPolicy.IsUsableState((int)code, next.ToString()))
                    Invalidated?.Invoke("CPU-Zustand " + next + "; Originalcode=" + code);
            };
            hardwareHandler = (_, code, date) => Invalidated?.Invoke("Hardwareprojekt geändert; Originalcode=" + code);
            softwareHandler = (_, change) => Invalidated?.Invoke("Softwareprojekt geändert; Bindungen und Zeitbasis neu prüfen: " + change);
            lostHandler = () => Invalidated?.Invoke("Lokaler Runtime-Manager verloren.");
        }
        internal static T Sdk<T>(Func<T> call)
        {
            try { return call(); }
            catch (SimulationRuntimeException e) { throw new InvalidOperationException("Siemens V5: Originalcode=" + e.RuntimeErrorCode + "; " + e.Message, e); }
        }
        internal static void Sdk(Action call) => Sdk(() => { call(); return 0; });
        public void BeginSingleStepInputs() => Sdk(() => BeginCore(EOperatingMode.SingleStep_P));
        internal void BeginSingleStepCycle() => Sdk(() => BeginCore(EOperatingMode.SingleStep_C));
        private void BeginCore(EOperatingMode mode)
        {
            var instance = Instance;
            var beforeState = instance.OperatingState;
            var beforeMode = instance.OperatingMode;
            Trace($"Before request: OperatingState={beforeState}, OperatingMode={beforeMode}");
            if (beforeState != EOperatingState.Run || beforeMode != EOperatingMode.Default)
                throw new InvalidOperationException("Erwartet wird eine bereits laufende, bewusst ausgewählte CPU im Default-Modus. Kein Start oder Fremd-Freeze wird übernommen.");
            if (instance.IsSendSyncEventInDefaultModeEnabled)
                throw new InvalidOperationException("Bereits aktivierte Default-Synchronisationsereignisse zuerst separat prüfen; keine fremde Synchronisation übernehmen.");
            if (mode != EOperatingMode.SingleStep_P && mode != EOperatingMode.SingleStep_C)
                throw new InvalidOperationException("Unbekanntes Synchronisationsprofil.");
            requestedMode = mode;
            // Subscribe BEFORE switching mode; callbacks do no I/O and no model work.
            // Mark first, so a partial failure can still attempt removal of every handler.
            subscribed = true;
            instance.OnSyncPointReached += syncHandler;
            instance.OnOperatingStateChanged += stateHandler;
            instance.OnHardwareConfigChanged += hardwareHandler;
            instance.OnSoftwareConfigurationChanged += softwareHandler;
            SimulationRuntimeManager.OnRunTimemanagerLost += lostHandler;
            Trace("Handlers attached; requesting " + mode + ".");
            instance.OperatingMode = mode;
            Trace(mode + " setter returned; effective mode can be deferred until a synchronization point (V5 manual p235).");
            Trace($"After request: OperatingMode={instance.OperatingMode}, OperatingState={instance.OperatingState}");
        }
        internal void RequireFrozenInputs() => RequireFrozen(EOperatingMode.SingleStep_P);
        internal void RequireFrozenCycle() => RequireFrozen(EOperatingMode.SingleStep_C);
        private void RequireFrozen(EOperatingMode mode)
        {
            var instance = Instance;
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(V5Session));
            if (requestedMode != mode || instance.OperatingMode != mode || instance.OperatingState != EOperatingState.Freeze)
                throw new InvalidOperationException("Kein bestätigter Freeze im " + mode + "-Modus; kein E/A-Austausch.");
        }
        public void ContinueToNextSyncPoint() => Sdk(ContinueCore);
        private void ContinueCore()
        {
            RequireFrozen(requestedMode);
            Instance.RunToNextSyncPoint(); // Requires explicit advance approval; no CPU start command.
        }
        public void Dispose()
        {
            var instance = Instance;
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var errors = new List<Exception>();
            void Try(Action a) { try { Sdk(a); } catch (Exception e) { errors.Add(e); } }
            if (subscribed)
            {
                Try(() => instance.OnSyncPointReached -= syncHandler);
                Try(() => instance.OnOperatingStateChanged -= stateHandler);
                Try(() => instance.OnHardwareConfigChanged -= hardwareHandler);
                Try(() => instance.OnSoftwareConfigurationChanged -= softwareHandler);
                Try(() => SimulationRuntimeManager.OnRunTimemanagerLost -= lostHandler);
            }
            Try(instance.Dispose);
            // Never restore Default automatically: that would release Freeze and may resume the CPU.
            if (errors.Count > 0) throw new AggregateException("SDK-Handles konnten nicht vollständig bereinigt werden; CPU-Zustand unbestätigt.", errors);
        }
    }

    internal sealed class V5Endpoint : IPlcEndpoint, IEndpointInspection
    {
        private readonly V5Session session;
        private readonly V5IoExchange io;
        public bool UsesDigitalCycleProfile { get; }
        public V5Endpoint(V5Session session, SignalMap map, DigitalCycleProfile? digital)
        { this.session = session; UsesDigitalCycleProfile = digital != null; io = new(session.Instance, map, true, digital); }
        public EndpointInspection Inspect() => V5Session.Sdk(() => new EndpointInspection(session.InstanceName,
            session.Instance.OperatingState.ToString(), session.Instance.OperatingMode.ToString(), session.DiagnosticTrace));
        public string InstanceName => session.InstanceName;
        public EndpointEvidence Evidence => session.Evidence;
        public event Action<SyncNotification> SyncPoint { add => session.SyncPoint += value; remove => session.SyncPoint -= value; }
        public event Action<string> Invalidated { add => session.Invalidated += value; remove => session.Invalidated -= value; }
        public void BeginSingleStepInputs() { if (UsesDigitalCycleProfile) session.BeginSingleStepCycle(); else session.BeginSingleStepInputs(); }
        private void RequireFrozen() { if (UsesDigitalCycleProfile) session.RequireFrozenCycle(); else session.RequireFrozenInputs(); }
        public PlcReadback ReadOutputs(ClockGrant grant) => V5Session.Sdk(() =>
        {
            RequireFrozen(); io.BeginFrozenExchange(grant);
            var actuators = io.ReadActuators(); var telemetry = io.ReadTelemetry();
            return new PlcReadback(actuators, telemetry.MeasuredLengthMm, telemetry.PulseCount, telemetry.MeasurementActive);
        });
        public void WriteInputs(SensorFrame frame) => V5Session.Sdk(() => { RequireFrozen(); io.PublishSensors(frame); });
        public void ContinueToNextSyncPoint() => session.ContinueToNextSyncPoint();
        public void Dispose() { io.Dispose(); session.Dispose(); }
    }
}
