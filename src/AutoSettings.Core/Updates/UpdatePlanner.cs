namespace AutoSettings.Core.Updates;

/// <summary>What the updater does after a successful check.</summary>
public enum UpdateStep
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>Tell users that a new version exists.</summary>
    Notify,

    /// <summary>Download and verify the installer.</summary>
    Download,

    /// <summary>Run the downloaded installer.</summary>
    Install,
}

/// <summary>Decides what the updater does. Pure logic, so every combination is unit-tested.</summary>
public static class UpdatePlanner
{
    /// <summary>The mode that actually applies: portable installs can only be notified.</summary>
    public static UpdateMode EffectiveMode(UpdateMode mode, InstallMethod method) =>
        method == InstallMethod.Portable && mode is UpdateMode.AskFirst or UpdateMode.Automatic ? UpdateMode.Notify : mode;

    /// <summary>
    /// The next step after a check found <paramref name="release"/> (null: up to date).
    /// <paramref name="downloaded"/> says whether that release's installer is already downloaded and verified.
    /// </summary>
    public static UpdateStep NextStep(UpdateMode mode, InstallMethod method, ReleaseInfo? release, bool downloaded)
    {
        if (release is null)
            return UpdateStep.None;
        return EffectiveMode(mode, method) switch
        {
            UpdateMode.Off => UpdateStep.None,
            UpdateMode.Notify => UpdateStep.Notify,
            UpdateMode.AskFirst => downloaded ? UpdateStep.Notify : UpdateStep.Download,
            UpdateMode.Automatic => downloaded ? UpdateStep.Install : UpdateStep.Download,
            _ => UpdateStep.None,
        };
    }

    /// <summary>Whether a user may start the install of <paramref name="release"/> now.</summary>
    public static bool CanInstall(InstallMethod method, ReleaseInfo? release, UpdateState state) =>
        method == InstallMethod.Msi
        && release is not null
        && state is UpdateState.Available or UpdateState.Ready or UpdateState.Failed;

    /// <summary>
    /// Whether an automatic install may run now. It waits while someone plays or presents full screen,
    /// but not longer than <paramref name="maxDelay"/> after the update was downloaded.
    /// </summary>
    public static bool MayInstallAutomatically(bool anyoneBusy, DateTimeOffset downloadedAt, DateTimeOffset now, TimeSpan maxDelay) =>
        !anyoneBusy || now - downloadedAt >= maxDelay;
}
