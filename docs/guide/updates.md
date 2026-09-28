# Updates

AutoSettings keeps itself up to date from its [GitHub releases](https://github.com/Samet1771/WindowsSettingAutomation/releases).
Updates are checked by the AutoSettings service, so they also work for users without administrator rights, and
installing one does not show a Windows permission prompt.

## Choosing what happens

Open **Settings → Updates** in the app. Under *When a new version is released*, choose:

| Choice | What happens |
|---|---|
| **Download it and ask me before installing** (default) | The update is downloaded and checked in the background. A notification says *"AutoSettings x.y.z is available"*. Click it, or **Install x.y.z** in Settings, or **Install update x.y.z** in the tray menu. |
| **Install it automatically** | The update is downloaded, checked and installed without asking. If someone is playing a game or presenting full screen, AutoSettings waits until they stop (at most 24 hours). |
| **Only tell me** | A notification says that a new version exists; **What's new** opens the release page. Nothing is downloaded. |
| **Do nothing (updates off)** | AutoSettings does not check. **Check now** still works. |

**Include beta versions** also offers pre-releases (versions like `0.4.0-beta.1`). They get new features first but
may have more bugs. When the final version comes out, it replaces the beta.

**Check now** checks immediately. Otherwise AutoSettings checks two minutes after the computer starts and then
about every 12 hours. Every user of the computer sees the same settings; any of them can change them, unless an
administrator locked them (see below).

## What happens during an update

1. The service downloads the installer (`AutoSettings-x.y.z-x64.msi`) from the GitHub release into
   `%ProgramData%\AutoSettings\updates`. Only administrators and the system can write to this folder.
2. It checks that the file is exactly the one that was published: its SHA-256 checksum must match
   `SHA256SUMS.txt` of the release and the checksum GitHub reports. If AutoSettings itself is digitally signed, the
   installer must be signed by the same publisher. If anything does not match, the file is deleted and nothing is
   installed.
3. When the update is installed, the AutoSettings window and tray icon close for a moment, the installer replaces the
   program files, and the service and the tray icon start again (usually within a minute). Your automations and
   settings are kept.
4. A notification says *"AutoSettings was updated"*, and the **Activity** page (machine activity) says
   *"AutoSettings was updated from x to y"*.

Profiles that were active are reverted when the app closes for the update, and applied again by their triggers
afterwards, like after a restart.

AutoSettings never installs an older version than the one you have.

## Portable installs

If AutoSettings was installed with `install.ps1` instead of the MSI, it cannot update itself: it only tells you
that a new version exists. Install the MSI once (it replaces the portable install and keeps your automations) to
get automatic updates.

## Company computers

Administrators can set the defaults, or lock them, in `%ProgramData%\AutoSettings\appsettings.json` (create it;
only administrators can write to that folder, and updates never change it):

```json
{
  "Updates": {
    "Enabled": true,
    "Repository": "Samet1771/WindowsSettingAutomation",
    "CheckIntervalHours": 12,
    "DefaultMode": "AskFirst",
    "DefaultIncludePrereleases": false,
    "AllowUserChanges": false
  }
}
```

Only the settings you write there change; the rest keep their defaults.

| Setting | Meaning |
|---|---|
| `Enabled` | `false` turns updates off completely; users cannot turn them on. |
| `Repository` | The GitHub repository whose releases are installed. |
| `CheckIntervalHours` | Hours between checks (at least 1). |
| `DefaultMode` | `AskFirst`, `Automatic`, `Notify` or `Off`: used until someone changes it in the app. |
| `DefaultIncludePrereleases` | Offer beta versions by default. |
| `AllowUserChanges` | `false` locks the settings: the app shows them but they cannot be changed. |

Restart the service after changing the file: `Restart-Service AutoSettings`.

The choices made in the app are stored in `%ProgramData%\AutoSettings\updates.json`.

## Problems

- **"Could not check for updates"**: the computer could not reach `api.github.com`. Check the internet connection
  and any proxy or firewall. The service runs as the system account, so it does not use a proxy that is only set up
  for your user account. GitHub also limits how often a computer can ask (60 times per hour), which a normal
  AutoSettings never reaches.
- **"The file does not match ..." / "not signed"**: the downloaded file was damaged or is not the published one.
  Nothing was installed. Try **Check now** later; if it keeps happening, please report it.
- **"The update to x.y.z did not complete"**: the installer failed and the previous version is still installed.
  The installer log is in `%ProgramData%\AutoSettings\updates\install-x.y.z.log`. You can also download the MSI from
  the release page and run it yourself.
