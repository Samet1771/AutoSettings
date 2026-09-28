using AutoSettings.Core.Model;

namespace AutoSettings.Core.Catalog;

/// <summary>The built-in action types.</summary>
public static class BuiltInActions
{
    /// <summary>Action categories, in the order the editor and docs show them.</summary>
    public static class Categories
    {
        /// <summary>Profiles, delays.</summary>
        public const string Flow = "Flow";
        /// <summary>Theme, wallpaper, taskbar.</summary>
        public const string Personalization = "Personalization";
        /// <summary>Brightness, resolution.</summary>
        public const string Display = "Display";
        /// <summary>Power plans and timeouts.</summary>
        public const string Power = "Power";
        /// <summary>Volume, mute, default devices.</summary>
        public const string Audio = "Audio";
        /// <summary>Radios, mouse.</summary>
        public const string Devices = "Network & devices";
        /// <summary>Launch/close apps, scripts, notifications.</summary>
        public const string Apps = "Apps & scripts";
        /// <summary>Notification banners, Do Not Disturb.</summary>
        public const string Notifications = "Notifications";
        /// <summary>Registry and services.</summary>
        public const string Advanced = "Advanced";
    }

    /// <summary>Type id of the built-in action that applies a profile.</summary>
    public const string ProfileApply = "profile.apply";

    /// <summary>Type id of the built-in action that reverts a profile.</summary>
    public const string ProfileRevert = "profile.revert";

    /// <summary>Type id of the built-in wait action.</summary>
    public const string Delay = "delay";

    /// <summary>Values accepted by <c>profile.apply</c>'s <c>revert_on</c>.</summary>
    public static readonly IReadOnlyList<string> RevertOnValues = ["auto", "app_unfocused", "app_closed", "logoff", "lock", "never"];

    private static readonly IReadOnlyList<string> OnOffToggle = ["on", "off", "toggle"];

    /// <summary>Pages accepted by <c>settings.open</c>, mapped to their ms-settings: links.</summary>
    public static readonly IReadOnlyDictionary<string, string> SettingsPages = new Dictionary<string, string>
    {
        ["display"] = "ms-settings:display",
        ["night_light"] = "ms-settings:nightlight",
        ["sound"] = "ms-settings:sound",
        ["notifications"] = "ms-settings:notifications",
        ["focus"] = "ms-settings:quiethours",
        ["power"] = "ms-settings:powersleep",
        ["battery"] = "ms-settings:batterysaver",
        ["bluetooth"] = "ms-settings:bluetooth",
        ["wifi"] = "ms-settings:network-wifi",
        ["network"] = "ms-settings:network-status",
        ["personalization"] = "ms-settings:personalization",
        ["background"] = "ms-settings:personalization-background",
        ["colors"] = "ms-settings:colors",
        ["taskbar"] = "ms-settings:taskbar",
        ["mouse"] = "ms-settings:mousetouchpad",
        ["keyboard"] = "ms-settings:keyboard",
        ["language"] = "ms-settings:regionlanguage",
        ["date_time"] = "ms-settings:dateandtime",
        ["apps"] = "ms-settings:appsfeatures",
        ["default_apps"] = "ms-settings:defaultapps",
        ["storage"] = "ms-settings:storagesense",
        ["privacy"] = "ms-settings:privacy",
        ["windows_update"] = "ms-settings:windowsupdate",
        ["about"] = "ms-settings:about",
    };

    private const string MonitorHelp = "all, primary, a monitor number (2) or a device name such as \\\\.\\DISPLAY2.";

    private const string BestEffort = "This setting has no official programming interface; AutoSettings changes it the way Windows stores it internally. It works on current Windows 10 and 11 builds but may stop working after a Windows update. If it fails, the error says so; use `settings.open` to open the Settings page instead.";

    private static FieldDescriptor AudioDevice => Fields.Text(
        "device",
        "Part of the device name as shown in Sound settings (e.g. Headphones, Speakers, USB). Leave empty for the current default device.",
        example: "Headphones");

    /// <summary>Registry hives accepted by <c>registry.set</c>, mapped to whether they are per-user.</summary>
    public static readonly IReadOnlyDictionary<string, bool> RegistryHives = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCU"] = true,
        ["HKEY_CURRENT_USER"] = true,
        ["HKLM"] = false,
        ["HKEY_LOCAL_MACHINE"] = false,
    };

    /// <summary>Splits <c>HKCU\Software\X</c> into hive and sub key. Returns false for unknown hives.</summary>
    public static bool TrySplitRegistryKey(string? key, out string hive, out string subKey)
    {
        hive = "";
        subKey = "";
        if (string.IsNullOrWhiteSpace(key))
            return false;
        var normalized = key.Replace('/', '\\').Trim().TrimEnd('\\');
        var slash = normalized.IndexOf('\\');
        hive = slash < 0 ? normalized : normalized[..slash];
        subKey = slash < 0 ? "" : normalized[(slash + 1)..];
        return RegistryHives.ContainsKey(hive);
    }

    /// <summary>All built-in actions.</summary>
    public static IEnumerable<ComponentDescriptor> All =>
    [
        // ---------------------------------------------------------------- Flow
        new()
        {
            Type = ProfileApply,
            Kind = ComponentKind.Action,
            Category = Categories.Flow,
            Title = "Apply profile",
            Description = "Applies a profile. The settings it changes are remembered and restored when the profile is reverted, automatically or with profile.revert.",
            Fields =
            [
                Fields.Text("profile", "Id of the profile to apply.", required: true, example: "gaming"),
                Fields.Choice("revert_on",
                    "When to undo the profile automatically. 'auto' pairs it with the trigger: app_focused → app_unfocused, app_started → app_closed, logon → logoff, unlock → lock; other triggers never revert automatically.",
                    RevertOnValues, defaultValue: "auto"),
            ],
            Example = """
                type: profile.apply
                profile: gaming
                revert_on: auto
                """,
            Notes = "When the automatic revert is tied to an app (app_unfocused, app_closed) it waits for the same app that triggered the automation. If several active profiles change the same setting, the one with the highest priority wins; when it is reverted, the next one's value is applied, and the original value comes back only when no profile changes that setting any more.",
        },
        new()
        {
            Type = ProfileRevert,
            Kind = ComponentKind.Action,
            Category = Categories.Flow,
            Title = "Revert profile",
            Description = "Undoes an active profile, restoring the settings it changed. Does nothing if the profile is not active.",
            Fields = [Fields.Text("profile", "Id of the profile to revert.", required: true, example: "gaming")],
            Example = """
                type: profile.revert
                profile: gaming
                """,
        },
        new()
        {
            Type = Delay,
            Kind = ComponentKind.Action,
            Category = Categories.Flow,
            Title = "Wait",
            Description = "Waits before running the next action.",
            Fields = [Fields.Duration("duration", "How long to wait, e.g. 10s, 5m, 1h.", required: true, example: "30s")],
            Example = """
                type: delay
                duration: 30s
                """,
        },

        // ---------------------------------------------------------------- Personalization
        new()
        {
            Type = "theme.mode",
            Kind = ComponentKind.Action,
            Category = Categories.Personalization,
            Title = "Light / dark mode",
            Description = "Switches Windows and/or apps between light and dark mode.",
            Revertible = true,
            KeyFields = ["target"],
            Fields =
            [
                Fields.Choice("mode", "light, dark, or toggle.", ["light", "dark", "toggle"], required: true),
                Fields.Choice("target", "What to change: both, apps only, or system (taskbar, Start) only.", ["both", "apps", "system"], defaultValue: "both"),
            ],
            Example = """
                type: theme.mode
                mode: dark
                """,
        },
        new()
        {
            Type = "theme.transparency",
            Kind = ComponentKind.Action,
            Category = Categories.Personalization,
            Title = "Transparency effects",
            Description = "Turns transparency effects on or off.",
            Revertible = true,
            Fields = [Fields.Boolean("enabled", "true to enable transparency effects.")],
            Validate = c => ValidationRules.Require(c.Has("enabled"), "'enabled' is required"),
            Example = """
                type: theme.transparency
                enabled: false
                """,
        },
        new()
        {
            Type = "wallpaper.set",
            Kind = ComponentKind.Action,
            Category = Categories.Personalization,
            Title = "Set wallpaper",
            Description = "Sets the desktop background image.",
            Revertible = true,
            Fields =
            [
                Fields.Path("path", "Image file (jpg, png, bmp).", required: true, example: "C:\\Users\\samet\\Pictures\\night.jpg"),
                Fields.Choice("style", "How the image fits the screen.", ["fill", "fit", "stretch", "tile", "center", "span"], defaultValue: "fill"),
            ],
            Example = """
                type: wallpaper.set
                path: C:\Users\samet\Pictures\night.jpg
                style: fill
                """,
        },
        new()
        {
            Type = "taskbar.autohide",
            Kind = ComponentKind.Action,
            Category = Categories.Personalization,
            Title = "Auto-hide taskbar",
            Description = "Turns taskbar auto-hide on or off.",
            Revertible = true,
            Fields = [Fields.Boolean("enabled", "true to hide the taskbar automatically.")],
            Validate = c => ValidationRules.Require(c.Has("enabled"), "'enabled' is required"),
            Example = """
                type: taskbar.autohide
                enabled: true
                """,
        },

        // ---------------------------------------------------------------- Display
        new()
        {
            Type = "display.brightness",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "Screen brightness",
            Description = "Sets screen brightness. Works for laptop screens and for external monitors that support DDC/CI.",
            Revertible = true,
            KeyFields = ["monitor"],
            Fields =
            [
                Fields.Integer("level", "Brightness in percent.", required: true, min: 0, max: 100, example: "40"),
                Fields.Choice("monitor", "Which screens: all, internal (laptop panel) or external.", ["all", "internal", "external"], defaultValue: "all"),
            ],
            Example = """
                type: display.brightness
                level: 40
                """,
            Notes = "External monitors need DDC/CI enabled in their on-screen menu. Some monitors take a second to react.",
        },
        new()
        {
            Type = "display.resolution",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "Resolution / refresh rate",
            Description = "Changes a monitor's resolution and/or refresh rate.",
            Revertible = true,
            KeyFields = ["monitor"],
            Fields =
            [
                Fields.Integer("width", "Width in pixels.", min: 320, example: "1920"),
                Fields.Integer("height", "Height in pixels.", min: 200, example: "1080"),
                Fields.Integer("refresh_rate", "Refresh rate in Hz.", min: 1, example: "144"),
                Fields.Text("monitor", "primary, or a device name such as \\\\.\\DISPLAY2.", defaultValue: "primary"),
            ],
            Validate = ValidateResolution,
            Example = """
                type: display.resolution
                refresh_rate: 60
                """,
            Notes = "The mode must be supported by the monitor; unsupported modes are rejected by Windows and reported in the activity log.",
        },

        // ---------------------------------------------------------------- Power
        new()
        {
            Type = "power.plan",
            Kind = ComponentKind.Action,
            Category = Categories.Power,
            Title = "Power plan",
            Description = "Activates a power plan.",
            Revertible = true,
            Fields =
            [
                Fields.Text("plan",
                    "balanced, high_performance, power_saver, ultimate_performance, a plan name as shown in Control Panel, or a plan GUID.",
                    required: true, example: "high_performance"),
            ],
            Example = """
                type: power.plan
                plan: high_performance
                """,
            Notes = "Some laptops (Modern Standby) only expose the Balanced plan; use the Windows power mode slider for those.",
        },
        new()
        {
            Type = "power.screen_timeout",
            Kind = ComponentKind.Action,
            Category = Categories.Power,
            Title = "Screen turn-off time",
            Description = "Sets how long before the screen turns off when idle, on the active power plan.",
            Revertible = true,
            KeyFields = ["power_source"],
            Fields =
            [
                Fields.Integer("minutes", "Minutes of inactivity; 0 means never.", required: true, min: 0, example: "10"),
                Fields.Choice("power_source", "Change the setting for ac (plugged in), battery, or both.", ["both", "ac", "battery"], defaultValue: "both"),
            ],
            Example = """
                type: power.screen_timeout
                minutes: 0
                """,
        },
        new()
        {
            Type = "power.sleep_timeout",
            Kind = ComponentKind.Action,
            Category = Categories.Power,
            Title = "Sleep time",
            Description = "Sets how long before the computer sleeps when idle, on the active power plan.",
            Revertible = true,
            KeyFields = ["power_source"],
            Fields =
            [
                Fields.Integer("minutes", "Minutes of inactivity; 0 means never.", required: true, min: 0, example: "30"),
                Fields.Choice("power_source", "Change the setting for ac (plugged in), battery, or both.", ["both", "ac", "battery"], defaultValue: "both"),
            ],
            Example = """
                type: power.sleep_timeout
                minutes: 0
                power_source: ac
                """,
        },

        // ---------------------------------------------------------------- Audio
        new()
        {
            Type = "audio.volume",
            Kind = ComponentKind.Action,
            Category = Categories.Audio,
            Title = "Set volume",
            Description = "Sets the master volume of an output device.",
            Revertible = true,
            KeyFields = ["device"],
            Fields =
            [
                Fields.Integer("level", "Volume in percent.", required: true, min: 0, max: 100, example: "30"),
                AudioDevice,
            ],
            Example = """
                type: audio.volume
                level: 30
                """,
        },
        new()
        {
            Type = "audio.mute",
            Kind = ComponentKind.Action,
            Category = Categories.Audio,
            Title = "Mute / unmute",
            Description = "Mutes, unmutes or toggles an output device.",
            Revertible = true,
            KeyFields = ["device"],
            Fields =
            [
                Fields.Choice("state", "mute, unmute, or toggle.", ["mute", "unmute", "toggle"], required: true),
                AudioDevice,
            ],
            Example = """
                type: audio.mute
                state: mute
                """,
        },
        new()
        {
            Type = "audio.default_device",
            Kind = ComponentKind.Action,
            Category = Categories.Audio,
            Title = "Default audio device",
            Description = "Makes a device the default speaker/headphones or microphone.",
            Revertible = true,
            KeyFields = ["flow", "role"],
            Fields =
            [
                Fields.Text("device", "Part of the device name as shown in Sound settings.", required: true, example: "Headphones"),
                Fields.Choice("flow", "output (playback) or input (recording).", ["output", "input"], defaultValue: "output"),
                Fields.Choice("role", "Which default to change: all, multimedia (default device) or communications (default communication device).",
                    ["all", "multimedia", "communications"], defaultValue: "all"),
            ],
            Example = """
                type: audio.default_device
                device: Headphones
                """,
        },

        // ---------------------------------------------------------------- Network & devices
        new()
        {
            Type = "radio.set",
            Kind = ComponentKind.Action,
            Category = Categories.Devices,
            Title = "Wi-Fi / Bluetooth on or off",
            Description = "Turns a wireless radio on, off, or toggles it.",
            Revertible = true,
            KeyFields = ["radio"],
            Fields =
            [
                Fields.Choice("radio", "Which radio.", ["wifi", "bluetooth", "mobile_broadband"], required: true),
                Fields.Choice("state", "on, off, or toggle.", OnOffToggle, required: true),
            ],
            Example = """
                type: radio.set
                radio: bluetooth
                state: off
                """,
            Notes = "Windows may deny radio access if 'Let apps control device radios' is turned off in Privacy settings.",
        },
        new()
        {
            Type = "mouse.speed",
            Kind = ComponentKind.Action,
            Category = Categories.Devices,
            Title = "Mouse pointer speed",
            Description = "Sets the mouse pointer speed.",
            Revertible = true,
            Fields = [Fields.Integer("speed", "1 (slowest) to 20 (fastest); Windows' default is 10.", required: true, min: 1, max: 20, example: "10")],
            Example = """
                type: mouse.speed
                speed: 14
                """,
        },

        // ---------------------------------------------------------------- Apps & scripts
        new()
        {
            Type = "app.launch",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Launch app",
            Description = "Starts a program or opens a file.",
            Fields =
            [
                Fields.Path("path", "Program or file to open.", required: true, example: "C:\\Program Files\\OBS Studio\\bin\\64bit\\obs64.exe"),
                Fields.Text("arguments", "Command-line arguments.", example: "--startreplaybuffer"),
                Fields.Path("working_directory", "Folder to start in. Defaults to the program's folder."),
                Fields.Choice("window", "Initial window state.", ["normal", "minimized", "maximized", "hidden"], defaultValue: "normal"),
                Fields.Boolean("skip_if_running", "Do nothing if the program is already running.", defaultValue: true),
            ],
            Example = """
                type: app.launch
                path: C:\Program Files\OBS Studio\bin\64bit\obs64.exe
                working_directory: C:\Program Files\OBS Studio\bin\64bit
                window: minimized
                """,
        },
        new()
        {
            Type = "app.close",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Close app",
            Description = "Asks an app to close, like clicking its X button. With force: true it is ended if it does not close in time.",
            Fields =
            [
                Fields.Apps("app", "App patterns to close.", required: true, example: "Discord.exe"),
                Fields.Boolean("force", "End the app if it has not closed after the timeout. Unsaved work is lost.", defaultValue: false),
                Fields.Duration("timeout", "How long to wait for the app to close before forcing it.", defaultValue: "10s"),
            ],
            Example = """
                type: app.close
                app: Discord.exe
                force: true
                """,
        },
        new()
        {
            Type = "command.run",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Run command / script",
            Description = "Runs a PowerShell script or a command line.",
            RunsAsResolver = c => string.Equals(c.GetString("run_as"), "system", StringComparison.OrdinalIgnoreCase)
                ? ExecutionScope.Machine
                : ExecutionScope.User,
            Fields =
            [
                Fields.Multiline("command",
                    "The script or command line. Details of the event are available as environment variables, e.g. $env:AUTOSETTINGS_USER and $env:AUTOSETTINGS_APP_PATH in PowerShell or %AUTOSETTINGS_USER% in cmd ({{ placeholders }} are deliberately not replaced in scripts).",
                    required: true, example: "Write-Output \"Hello $env:AUTOSETTINGS_USER\"") with { AllowPlaceholders = false },
                Fields.Choice("shell", "powershell (Windows PowerShell 5.1), pwsh (PowerShell 7), cmd, or none (run the command line directly).",
                    ["powershell", "pwsh", "cmd", "none"], defaultValue: "powershell"),
                Fields.Choice("run_as", "user runs as the signed-in user; system runs as the local SYSTEM account (machine automations only).",
                    ["user", "system"], defaultValue: "user"),
                Fields.Boolean("wait", "Wait for the command to finish before the next action. A non-zero exit code then counts as a failure.", defaultValue: false),
                Fields.Duration("timeout", "When waiting, stop the command after this long.", defaultValue: "1m"),
                Fields.Boolean("hidden", "Run without showing a console window.", defaultValue: true),
            ],
            Example = """
                type: command.run
                shell: powershell
                command: |
                  Stop-Process -Name OneDrive -ErrorAction SilentlyContinue
                """,
            Notes = "Every [placeholder](../placeholders.md) is available to the script as an environment variable named AUTOSETTINGS_ followed by the placeholder name in capitals with dots replaced by underscores: {{ user }} → AUTOSETTINGS_USER, {{ app.path }} → AUTOSETTINGS_APP_PATH. Placeholders are not replaced inside the script text itself, because window titles and file names are chosen by other programs and could otherwise inject commands. Scripts in machine automations can only be edited by administrators, because they may run as SYSTEM.",
        },
        new()
        {
            Type = "notify",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Show notification",
            Description = "Shows a Windows notification.",
            Fields =
            [
                Fields.Text("title", "Notification title.", defaultValue: "AutoSettings"),
                Fields.Text("message", "Notification text. Placeholders such as {{ user }} are replaced.", required: true, example: "Welcome back, {{ user }}!"),
            ],
            Example = """
                type: notify
                message: "Gaming mode on: {{ app }}"
                """,
            Notes = "Notifications respect Windows Do Not Disturb.",
        },
        new()
        {
            Type = "open",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Open URL / settings page",
            Description = "Opens a website, a file, a folder, or a Windows Settings page (ms-settings: links).",
            Fields = [Fields.Text("target", "What to open.", required: true, example: "ms-settings:display")],
            Example = """
                type: open
                target: https://calendar.google.com
                """,
        },

        // ---------------------------------------------------------------- Milestone 5 settings
        new()
        {
            Type = "theme.accent_color",
            Kind = ComponentKind.Action,
            Category = Categories.Personalization,
            Title = "Accent color",
            Description = "Sets the Windows accent color, and optionally shows it on Start/taskbar and title bars.",
            Revertible = true,
            Fields =
            [
                Fields.Text("color", "Color as #RRGGBB.", required: true, example: "#2563EB"),
                Fields.Boolean("show_on_taskbar", "Show the accent color on Start and the taskbar."),
                Fields.Boolean("show_on_title_bars", "Show the accent color on title bars and window borders."),
            ],
            Validate = c => ValidationRules.Require(IsHexColor(c.GetString("color")), "'color' must look like #2563EB"),
            Example = """
                type: theme.accent_color
                color: "#E11D48"
                show_on_title_bars: true
                """,
            Notes = "Quote the color in YAML (\"#E11D48\"), because # starts a comment. Also turns off 'automatically pick an accent color from my background'.",
        },
        new()
        {
            Type = "display.hdr",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "HDR",
            Description = "Turns HDR (Windows HD Color) on or off for HDR-capable displays.",
            Revertible = true,
            KeyFields = ["monitor"],
            Fields =
            [
                Fields.Choice("state", "on, off, or toggle.", OnOffToggle, required: true),
                Fields.Text("monitor", "Which display: " + MonitorHelp, defaultValue: "all"),
            ],
            Example = """
                type: display.hdr
                state: on
                """,
            Notes = "Displays that do not support HDR are skipped; the action fails if none does.",
        },
        new()
        {
            Type = "display.primary",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "Main display",
            Description = "Makes a monitor the main display (the one with the taskbar clock and where new windows open).",
            Revertible = true,
            Fields = [Fields.Text("monitor", "A monitor number (2) or a device name such as \\\\.\\DISPLAY2.", required: true, example: "2")],
            Example = """
                type: display.primary
                monitor: "2"
                """,
            Notes = "Monitor numbers are the ones shown by Settings > System > Display > Identify.",
        },
        new()
        {
            Type = "display.scaling",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "Display scaling",
            Description = "Changes the scale (text, apps and other items size) of a display.",
            Revertible = true,
            KeyFields = ["monitor"],
            Fields =
            [
                Fields.Integer("percent", "Scale in percent: 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450 or 500.", required: true, min: 100, max: 500, example: "125"),
                Fields.Text("monitor", "Which display: " + MonitorHelp, defaultValue: "primary"),
            ],
            Validate = c => ValidationRules.Require(c.GetInteger("percent") is not { } p || ScaleSteps.Contains((int)p),
                "'percent' must be one of 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500"),
            Example = """
                type: display.scaling
                percent: 150
                """,
            Notes = "Each display only allows the steps up to its maximum (shown in Settings > Display > Scale). " + BestEffort,
        },
        new()
        {
            Type = "display.night_light",
            Kind = ComponentKind.Action,
            Category = Categories.Display,
            Title = "Night light",
            Description = "Turns Night light (warmer screen colors) on or off.",
            Revertible = true,
            Fields = [Fields.Choice("state", "on, off, or toggle.", OnOffToggle, required: true)],
            Example = """
                type: display.night_light
                state: on
                """,
            Notes = BestEffort,
        },
        new()
        {
            Type = "power.mode",
            Kind = ComponentKind.Action,
            Category = Categories.Power,
            Title = "Power mode",
            Description = "Sets the Windows power mode (the Settings > Power slider): best power efficiency, balanced or best performance.",
            Revertible = true,
            Fields = [Fields.Choice("mode", "The power mode.", ["best_efficiency", "balanced", "best_performance"], required: true)],
            Example = """
                type: power.mode
                mode: best_performance
                """,
            Notes = "Available on computers that show the Power mode setting (most Windows 10/11 PCs with the Balanced plan active).",
        },
        new()
        {
            Type = "notifications.banners",
            Kind = ComponentKind.Action,
            Category = Categories.Notifications,
            Title = "Notification banners",
            Description = "Turns app notification banners on or off (Settings > System > Notifications).",
            Revertible = true,
            Fields = [Fields.Boolean("enabled", "true to show notifications.")],
            Validate = c => ValidationRules.Require(c.Has("enabled"), "'enabled' is required"),
            Example = """
                type: notifications.banners
                enabled: false
                """,
            Notes = "AutoSettings' own notifications are hidden too while banners are off.",
        },
        new()
        {
            Type = "notifications.do_not_disturb",
            Kind = ComponentKind.Action,
            Category = Categories.Notifications,
            Title = "Do Not Disturb",
            Description = "Turns Do Not Disturb (Focus assist) on or off.",
            Revertible = true,
            Fields =
            [
                Fields.Choice("state", "on or off.", ["on", "off"], required: true),
                Fields.Choice("level", "When on: priority (priority notifications still show) or alarms (only alarms).", ["priority", "alarms"], defaultValue: "priority"),
            ],
            Example = """
                type: notifications.do_not_disturb
                state: on
                """,
            Notes = BestEffort,
        },
        new()
        {
            Type = "keyboard.layout",
            Kind = ComponentKind.Action,
            Category = Categories.Devices,
            Title = "Keyboard layout",
            Description = "Switches the keyboard layout (input language) of the active app and the default for new apps.",
            Revertible = true,
            Fields = [Fields.Text("layout", "A language tag (tr-TR, en-US, de-DE) or a keyboard layout id (0000041F).", required: true, example: "tr-TR")],
            Example = """
                type: keyboard.layout
                layout: en-US
                """,
            Notes = "The layout must be installed (Settings > Time & language > Language & region).",
        },
        new()
        {
            Type = "radio.airplane_mode",
            Kind = ComponentKind.Action,
            Category = Categories.Devices,
            Title = "Airplane mode",
            Description = "Turns all wireless radios (Wi-Fi, Bluetooth, mobile broadband) off (on) or back on (off).",
            Revertible = true,
            Fields = [Fields.Choice("state", "on turns every radio off; off turns them on.", ["on", "off"], required: true)],
            Example = """
                type: radio.airplane_mode
                state: on
                """,
            Notes = "Reverting restores each radio's previous state.",
        },
        new()
        {
            Type = "settings.open",
            Kind = ComponentKind.Action,
            Category = Categories.Apps,
            Title = "Open a Settings page",
            Description = "Opens a Windows Settings page, for settings that should be changed by hand.",
            Fields = [Fields.Choice("page", "Which page.", SettingsPages.Keys.ToList(), required: true)],
            Example = """
                type: settings.open
                page: display
                """,
        },

        // ---------------------------------------------------------------- Advanced
        new()
        {
            Type = "registry.set",
            Kind = ComponentKind.Action,
            Category = Categories.Advanced,
            Title = "Set registry value",
            Description = "Writes a registry value. Use this for any Windows setting that has no dedicated action.",
            Revertible = true,
            KeyFields = ["key", "name"],
            RunsAsResolver = c => TrySplitRegistryKey(c.GetString("key"), out var hive, out _) && RegistryHives[hive]
                ? ExecutionScope.User
                : ExecutionScope.Machine,
            Fields =
            [
                Fields.Text("key", "Registry key starting with HKCU\\ (your settings) or HKLM\\ (machine settings, machine automations only).", required: true,
                    example: "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"),
                Fields.Text("name", "Value name. Leave empty for the key's default value.", example: "HideFileExt"),
                Fields.Text("value", "The data. Numbers for dword/qword; separate multi_string items with |.", required: true, example: "0"),
                Fields.Choice("kind", "Value type.", ["string", "expand_string", "dword", "qword", "multi_string"], defaultValue: "string"),
                Fields.Boolean("broadcast", "Tell running apps that settings changed (WM_SETTINGCHANGE). Some settings need this to apply without signing out.", defaultValue: false),
            ],
            Validate = ValidateRegistry,
            Example = """
                type: registry.set
                key: HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
                name: HideFileExt
                value: "0"
                kind: dword
                broadcast: true
                """,
            Notes = "When reverted, a value that did not exist before is deleted again. Wrong registry edits can break Windows; prefer a dedicated action when one exists.",
        },
        new()
        {
            Type = "service.control",
            Kind = ComponentKind.Action,
            Category = Categories.Advanced,
            Title = "Start / stop a service",
            Description = "Starts or stops a Windows service.",
            Revertible = true,
            RunsAs = ExecutionScope.Machine,
            KeyFields = ["name"],
            Fields =
            [
                Fields.Text("name", "Service name (as in services.msc > Properties > Service name).", required: true, example: "wuauserv"),
                Fields.Choice("state", "running or stopped.", ["running", "stopped"], required: true),
                Fields.Duration("timeout", "How long to wait for the service to reach the state.", defaultValue: "30s"),
            ],
            Example = """
                type: service.control
                name: wuauserv
                state: stopped
                """,
            Notes = "Machine automations only (requires administrator rights).",
        },
    ];

    /// <summary>Display scaling steps Windows supports.</summary>
    public static readonly IReadOnlyList<int> ScaleSteps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static IEnumerable<string> ValidateResolution(ComponentConfig c)
    {
        var hasWidth = c.Has("width");
        var hasHeight = c.Has("height");
        if (hasWidth != hasHeight)
            yield return "set both 'width' and 'height', or neither";
        if (!hasWidth && !hasHeight && !c.Has("refresh_rate"))
            yield return "set 'width' and 'height' and/or 'refresh_rate'";
    }

    private static IEnumerable<string> ValidateRegistry(ComponentConfig c)
    {
        if (!TrySplitRegistryKey(c.GetString("key"), out _, out var subKey))
            yield return "'key' must start with HKCU\\ or HKLM\\";
        else if (subKey.Length == 0)
            yield return "'key' must include a sub key, e.g. HKCU\\Software\\MyApp";

        var kind = c.GetString("kind") ?? "string";
        var value = c.GetString("value");
        if (value is not null && kind is "dword" or "qword" && !long.TryParse(value, out _) && !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            yield return $"'value' must be a number for kind {kind}";
    }
}
