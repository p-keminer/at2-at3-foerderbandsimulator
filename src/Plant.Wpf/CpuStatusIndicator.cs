using System;

namespace Plant.Wpf
{
    internal sealed record CpuStatusView(string Text, string Background, string Foreground);

    // Only reports already received from an owned process are used. No SDK access.
    internal sealed class CpuStatusIndicator
    {
        private string? observed;
        private DateTime observedAt;
        private string completedReport = "";
        internal void Reset() { observed = null; completedReport = ""; }

        internal void RecoveryFinished(string? confirmedState, DateTime now)
        { observed = confirmedState == "Run" || confirmedState == "Stop" ? confirmedState + " / Default" : null; observedAt = now; }

        internal CpuStatusView Describe(LiveProcessSnapshot run, bool recovering, DateTime now)
        {
            if (run.IsActive)
            {
                observed = null;
                if (run.State == LiveProcessState.Faulted)
                    return new("CPU-Zustand unbestätigt · Verbindung gestört", "#FFE1B5", "#703500");
                return run.Latest == null
                    ? new("CPU-Verbindung wird aufgebaut…", "#DCEEF7", "#174764")
                    : new("SPS-Kopplung aktiv · SingleStep_C wird vom Simulator gesteuert", "#DCEEF7", "#174764");
            }
            if (!string.IsNullOrEmpty(run.ReportPath) && run.ReportPath != completedReport)
            {
                completedReport = run.ReportPath;
                observed = run.CleanupConfirmed && !run.HardKilled ? run.CpuBeforeDisconnect : null;
                observedAt = now;
            }
            if (recovering) return new("CPU wird auf Default zurückgestellt…", "#DCEEF7", "#174764");
            string stamp = " · Rückmeldung " + observedAt.ToString("HH:mm:ss");
            return observed switch
            {
                "Freeze / SingleStep_C" => new("CPU: FREEZE / SingleStep_C · Freigabe erforderlich" + stamp, "#FFD39A", "#703500"),
                "Run / SingleStep_C" => new("CPU: RUN / SingleStep_C · Synchronisierung noch aktiv" + stamp, "#FFD39A", "#703500"),
                "Stop / SingleStep_C" => new("CPU: STOP / SingleStep_C · Default fehlt" + stamp, "#FFF0AC", "#624D00"),
                "Run / Default" => new("CPU: RUN / Default" + stamp, "#D7F0DD", "#1D5730"),
                "Stop / Default" => new("CPU: STOP / Default · RUN in TIA oder PLCSIM wählen" + stamp, "#FFF0AC", "#624D00"),
                _ => new("CPU-Zustand nicht bestätigt", "#E7EBEF", "#364653")
            };
        }
    }
}
