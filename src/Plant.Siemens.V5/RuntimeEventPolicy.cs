// Pure policy, linked into offline tests without a Siemens assembly reference.
// Installed V5 manual p.302/329: WarningTrialModeActive=44 is a warning about
// the existing runtime. LicenseNotFound=-30 / NoLicenseAvailable=-31 remain errors.
internal static class RuntimeEventPolicy
{
    internal static bool IsUsableState(int code, string state) =>
        (code == 0 || code == 44) && (state == "Run" || state == "Freeze");
}
