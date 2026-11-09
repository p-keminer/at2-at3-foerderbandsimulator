namespace Plant.Wpf
{
    // Pure presentation: no CPU access, no inferred marker values, no change to run limits.
    internal static class LiveStatusText
    {
        internal static string StartButton(bool transport, bool interactive = false) => interactive ? "Mit SPS verbinden" : transport ? "Förderbanddurchlauf starten" : "Stationären E/A-Test starten";
        internal static string Hint(bool transport, bool interactive = false) => interactive
            ? "Leer verbinden, dann Prüfling einsetzen. Die SPS steuert Motor und Lampen."
            : transport ? "Begrenzter Durchlauf bis zum rechten Sensor. Betriebsfreigaben im SPS-Programm setzen."
            : "Kurzer E/A-Test bei ausgeschalteten Motoren.";
        internal static string Outcome(LiveProcessSnapshot run)
        {
            if (run.InteractiveSession)
            {
                if (run.IsActive)
                {
                    if (run.State == LiveProcessState.Faulted) return "Verbindung gestört: " + run.Error;
                    if (run.State == LiveProcessState.Stopping || run.State == LiveProcessState.Finalizing) return "Verbindung wird getrennt…";
                    if (run.Latest == null) return "Verbindung zur SPS wird aufgebaut…";
                    if (!run.Latest.Present && (run.Latest.MotorRight || run.Latest.MotorLeft))
                        return "Verbunden · Band leer, aber SPS befiehlt Motor EIN. Vor dem Einsetzen Motor im SPS-Programm stoppen; gegebenenfalls gespeicherten Messzustand prüfen.";
                    return run.Latest.Present ? "Verbunden · SPS steuert den Prüfling. Die Verbindung bleibt nach dem Durchlauf bestehen."
                        : "Verbunden · Band leer. Bei Stillstand kannst du jetzt einen Prüfling einsetzen.";
                }
                if (run.Passed && run.EndReason == "Disconnected") return "SPS-Verbindung kontrolliert getrennt.";
            }
            if (run.IsActive) return run.StationaryTest ? "Stationärer E/A-Test läuft · keine Bandfahrt." : "SPS-Lauf aktiv · SPS steuert das Band.";
            if (run.State == LiveProcessState.Disconnected) return "Noch kein SPS-Test gestartet.";
            if (run.Passed && run.StationaryTest)
                return "Stationärer E/A-Test beendet. Kein Förderbanddurchlauf ausgeführt. Für eine Fahrt die Testart Förderbanddurchlauf wählen.";
            if (run.Passed && run.TransportCompleted) return "Förderbanddurchlauf bestanden · Endabschaltung bestätigt.";
            if (run.Passed) return "Begrenzter SPS-Austausch beendet · vollständiger Transport nicht bestätigt.";
            if (run.StopRequested && run.State == LiveProcessState.Completed) return "SPS-Test abgebrochen und getrennt · kein bestandener Durchlauf.";
            return "SPS-Test nicht bestanden. " + (run.Error ?? (run.EndReason == "AdvanceLimit" || run.EndReason == "VirtualTimeLimit"
                ? "Laufgrenze erreicht, vollständige Fahrt nicht bestätigt. SPS-Motorbefehle und Betriebsfreigaben in TIA prüfen."
                : "Abschlussbericht prüfen: " + run.ReportPath));
        }
        internal static string AfterRecovery(LiveProcessSnapshot run, string cpuDetail) => Outcome(run) + "\n" + cpuDetail;
    }
}
