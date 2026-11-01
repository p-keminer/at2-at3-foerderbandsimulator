using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Plant.Core;

namespace Plant.Wpf
{
    public sealed class SessionHost : IDisposable
    {
        private readonly object gate = new();
        private readonly Thread worker;
        private bool closing;
        private SimulationSession session;
        public SessionHost(PlantConfiguration config)
        {
            session = new(config);
            worker = new Thread(Work) { IsBackground = true, Name = "Deterministischer Anlagen-Scheduler" };
            worker.Start();
        }
        public T Read<T>(Func<SimulationSession, T> action) { lock (gate) return action(session); }
        public void Change(Action<SimulationSession> action) { lock (gate) action(session); }
        public void Reset(PlantConfiguration config)
        {
            config.Validate(); lock (gate) session = new(config);
        }
        private void Work()
        {
            var clock = Stopwatch.StartNew(); long previous = clock.ElapsedTicks, budget = 0;
            while (true)
            {
                long now = clock.ElapsedTicks;
                long elapsedUs = (now - previous) * 1000000 / Stopwatch.Frequency;
                previous += elapsedUs * Stopwatch.Frequency / 1000000;
                lock (gate)
                {
                    if (closing) return;
                    if (session.Paused) budget = 0;
                    else
                    {
                        budget += elapsedUs;
                        if (budget > 250000)
                        { session.Pause("Schedulerüberlast >250 ms: sichtbar angehalten; keine Physikschritte übersprungen."); budget = 0; }
                        else
                            while (budget >= session.ModelStepUs && !session.Paused)
                            { session.Step(); budget -= session.ModelStepUs; }
                    }
                }
                Thread.Sleep(1);
            }
        }
        public void Dispose()
        {
            lock (gate) closing = true;
            if (!worker.Join(2000)) throw new InvalidOperationException("Scheduler wurde nicht rechtzeitig beendet.");
        }
    }
}
