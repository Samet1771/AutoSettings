namespace AutoSettings.Core.Updates;

/// <summary>What AutoSettings does when a new version is released.</summary>
public enum UpdateMode
{
    /// <summary>Never checks for updates.</summary>
    Off,

    /// <summary>Tells the user that a new version exists and links to the release page.</summary>
    Notify,

    /// <summary>Downloads and verifies the update, then asks; it is installed when someone clicks Install.</summary>
    AskFirst,

    /// <summary>Downloads, verifies and installs the update without asking.</summary>
    Automatic,
}

/// <summary>How AutoSettings was installed; only MSI installs can update themselves.</summary>
public enum InstallMethod
{
    /// <summary>Installed with the MSI installer.</summary>
    Msi,

    /// <summary>Installed with install.ps1 or run from a folder.</summary>
    Portable,
}

/// <summary>Where the update process is.</summary>
public enum UpdateState
{
    /// <summary>Updates are turned off.</summary>
    Disabled,

    /// <summary>Not checked yet.</summary>
    Unknown,

    /// <summary>Checking GitHub.</summary>
    Checking,

    /// <summary>The installed version is the newest.</summary>
    UpToDate,

    /// <summary>A newer version exists (not downloaded, or cannot be installed automatically).</summary>
    Available,

    /// <summary>Downloading the installer.</summary>
    Downloading,

    /// <summary>Downloaded and verified; waiting for Install.</summary>
    Ready,

    /// <summary>The installer is running.</summary>
    Installing,

    /// <summary>The last check, download or install failed; see the message.</summary>
    Failed,
}

/// <summary>The user-changeable update settings.</summary>
/// <param name="Mode">What to do when a new version is released.</param>
/// <param name="IncludePrereleases">Also offer beta versions (GitHub pre-releases).</param>
public sealed record UpdateSettings(UpdateMode Mode, bool IncludePrereleases);

/// <summary>A release found on GitHub.</summary>
/// <param name="Version">Version from the tag, for example <c>0.3.0</c>.</param>
/// <param name="Tag">The git tag, for example <c>v0.3.0</c>.</param>
/// <param name="IsPrerelease">Marked as pre-release on GitHub.</param>
/// <param name="PageUrl">The release page ("What's new").</param>
/// <param name="Notes">Release notes (markdown).</param>
/// <param name="MsiUrl">Download URL of the MSI installer.</param>
/// <param name="MsiName">File name of the MSI.</param>
/// <param name="ChecksumsUrl">Download URL of SHA256SUMS.txt, if the release has one.</param>
/// <param name="ApiSha256">SHA-256 of the MSI reported by the GitHub API (asset digest), if available.</param>
public sealed record ReleaseInfo(
    string Version,
    string Tag,
    bool IsPrerelease,
    string PageUrl,
    string Notes,
    string MsiUrl,
    string MsiName,
    string? ChecksumsUrl,
    string? ApiSha256);

/// <summary>The update status shown in the app.</summary>
/// <param name="CurrentVersion">The installed version.</param>
/// <param name="State">Where the update process is.</param>
/// <param name="Latest">The newest release that is newer than the installed version, if any.</param>
/// <param name="LastChecked">When GitHub was last checked successfully.</param>
/// <param name="Message">An explanation, for example why the update failed or cannot be installed.</param>
/// <param name="Settings">The current settings.</param>
/// <param name="CanChangeSettings">False when an administrator locked the settings.</param>
/// <param name="CanInstall">False for portable installs, which can only be notified.</param>
public sealed record UpdateStatus(
    string CurrentVersion,
    UpdateState State,
    ReleaseInfo? Latest,
    DateTimeOffset? LastChecked,
    string? Message,
    UpdateSettings Settings,
    bool CanChangeSettings,
    bool CanInstall);
