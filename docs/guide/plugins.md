# Plugins

Plugins add new triggers, conditions and actions to AutoSettings, written by other people. Once a plugin is
installed, its components appear in the editor's **Add** menus, grouped under the plugin's name, and you use them like
the built-in ones.

!!! warning "Only install plugins you trust"
    A plugin runs with your rights. A plugin installed for everyone can also run actions with full control of the
    computer. Before installing, AutoSettings shows what the plugin says it does, but it cannot check it.

## The Plugins page

Open **Plugins** in the app. The list shows every installed plugin:

| Column | Meaning |
|---|---|
| **On** | Turn the plugin off without removing it. Its automations stop running until you turn it on again. |
| **Version**, **Publisher** | From the plugin. |
| **For** | **Me**: installed only for you. **Everyone**: installed for all users of the computer by an administrator. |
| **Kind** | **PowerShell** (a script plugin) or **.NET**, with whether the .NET plugin is digitally signed and by whom. |
| **Status** | **Active**, **Off**, **Error: …** (the plugin is broken; the message says why), or **x.y.z available** when there is an update. |

## Installing

- **Install from file…** installs a plugin package (`.aspkg`) you downloaded.
- **Install from GitHub…** installs the newest release of a plugin from its GitHub repository. Enter `owner/name` or
  paste the link. Turn on **Include beta versions** to also consider pre-releases.

AutoSettings then asks:

1. **For everyone or only for you?** (unless the plugin can only be installed for everyone). Installing for
   everyone needs administrator permission, so Windows asks for it.
2. **Install it?** The dialog shows the name, version, publisher and description, who it is installed for, and the
   [permissions](../plugins/manifest.md#permissions) the plugin declares, such as "Uses the network or the internet".

The plugin is ready a few seconds later, and the Activity page says *Plugins loaded*.

## Updates

AutoSettings looks for updates of plugins that publish them on GitHub when you open the Plugins page, and when you
click **Check for updates**. To install one, select the plugin and click **Update**. Downloads are checked against the
checksums the author published; a file that does not match is not installed.

If an update causes problems, **Previous version** goes back to the version you had before.

## Removing

Select the plugin and click **Remove**. Your automations that use it are kept: they are marked
**⚠ needs plugin …** in the list and do not run until the plugin is installed again. The rest of your automations
keep working.

## Company computers and scripts

The same commands are available on the command line, for example for Intune or a login script:

```powershell
AutoSettings.Agent.exe --plugin list
AutoSettings.Agent.exe --plugin install C:\Deploy\acme.usb-1.2.0.aspkg --scope machine
AutoSettings.Agent.exe --plugin install acme/autosettings-usb --scope machine
AutoSettings.Agent.exe --plugin update --all --scope machine
AutoSettings.Agent.exe --plugin disable acme.usb --scope machine
AutoSettings.Agent.exe --plugin uninstall acme.usb --scope machine
```

`--scope machine` installs for everyone and needs an elevated prompt; without it, the command asks for administrator
permission. Leave it out to install for the current user. The exit code is 0 on success.

Plugins for everyone live in `%ProgramData%\AutoSettings\plugins`, and your own in
`%LocalAppData%\AutoSettings\plugins` (**Open plugin folder**).

## Problems

- **Error: …** in the Status column: the plugin is damaged or needs a newer AutoSettings. Update or reinstall it.
- **Not loaded (see Activity)**: the Activity page says why, for example two plugins use the same names.
- A .NET plugin that crashes is restarted automatically. After three crashes in ten minutes it is turned off until
  AutoSettings restarts or you update it. The Activity page says when this happens.
- Plugins for everyone are only loaded from a folder that only administrators can change. If someone changed its
  permissions, AutoSettings refuses to run them and says so in the Activity page.

Want to write a plugin? See [Plugins for developers](../plugins/index.md).
