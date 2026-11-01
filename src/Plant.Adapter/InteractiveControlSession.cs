using System;
using System.Diagnostics;
using System.Threading;
using Plant.Core;

namespace Plant.Adapter
{
    // No Siemens calls. A bounded command/heartbeat channel keeps an interactive session owned by its UI.
    public sealed class InteractiveControlSession : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly WorkpieceCommandChannel? commands;
        private readonly Timer? lease;
        private readonly int leaseMilliseconds;
        private long lastHeartbeat = Stopwatch.GetTimestamp();
        private string? failure;
        private bool stopped, disposed;
        public CancellationToken Token => cancellation.Token;
        public string? Failure { get { lock (gate) return failure; } }
        public bool StopRequested { get { lock (gate) return stopped; } }
        public InteractiveControlSession(WorkpieceCommandChannel? commands, bool interactive, int leaseMilliseconds = 5000)
        {
            Numbers.Require(!interactive || commands != null, "Interaktive Sitzung benötigt einen Bedienkanal.");
            Numbers.Require(leaseMilliseconds >= 50 && leaseMilliseconds <= 10000, "Ungültige Lebenszeichenfrist.");
            this.commands = commands; this.leaseMilliseconds = leaseMilliseconds;
            if (interactive) lease = new Timer(_ => CheckLease(), null, 25, 25);
        }
        public void Accept(string line)
        {
            lock (gate)
            {
                if (disposed || stopped) return;
                Numbers.Require(line.Length <= 1024, "Überlanger Bedienbefehl.");
                if (line == "STOP") { stopped = true; cancellation.Cancel(); return; }
                if (line == "PING") { lastHeartbeat = Stopwatch.GetTimestamp(); return; }
                Numbers.Require(commands != null && line.StartsWith("WORKPIECE ", StringComparison.Ordinal), "Unbekannter Bedienbefehl.");
                commands!.Enqueue(Json.Read<WorkpieceCommand>(line.Substring(10)));
            }
        }
        public void RequestStop() => Accept("STOP");
        public void EndInput() { lock (gate) { if (!stopped) Fail("Bedienkanal geschlossen; Verbindung wird beendet."); } }
        public void Fail(string reason)
        {
            lock (gate) { if (disposed || stopped) return; failure = reason; stopped = true; cancellation.Cancel(); }
        }
        private void CheckLease()
        {
            lock (gate)
                if (!disposed && !stopped && (Stopwatch.GetTimestamp() - lastHeartbeat) * 1000.0 / Stopwatch.Frequency > leaseMilliseconds)
                    Fail("Kein Lebenszeichen der Bedienoberfläche; Verbindung wird beendet.");
        }
        public void Dispose() { lock (gate) { disposed = true; lease?.Dispose(); cancellation.Dispose(); } }
    }
}
