namespace Plant.Wpf
{
    internal static class LiveSessionPolicy
    {
        internal static bool IsReleasableMode(string cpu) => cpu == "Freeze / SingleStep_C" ||
            cpu == "Stop / SingleStep_C" || cpu == "Run / SingleStep_C";

        // CPU STOP ends the exchange but does not clear the synchronization mode.
        // A failed acceptance test may also have cleanly released its own interface.
        // Native/process/protocol failures without this evidence never authorize a reset.
        internal static bool CanRestoreOwnFreeze(LiveProcessSnapshot run) =>
            (run.State == LiveProcessState.Completed || run.State == LiveProcessState.Faulted) &&
            run.FreezeOwnershipConfirmed && !run.IsActive && run.Latest != null && run.Latest.Sequence > 0 &&
            run.CleanupConfirmed && !run.HardKilled && (run.ExitCode == 0 || run.ExitCode == 1) &&
            IsReleasableMode(run.CpuBeforeDisconnect) && !string.IsNullOrWhiteSpace(run.ReportPath);
    }
}
