namespace AutoSettings.Core.Catalog;

/// <summary>The built-in condition types.</summary>
public static class BuiltInConditions
{
    /// <summary>Category for logic conditions.</summary>
    public const string LogicCategory = "Logic";

    /// <summary>Category for context conditions.</summary>
    public const string ContextCategory = "Context";

    /// <summary>Category for device state conditions.</summary>
    public const string DeviceCategory = "Device";

    /// <summary>Category for app conditions.</summary>
    public const string AppsCategory = "Apps";

    /// <summary>Allowed weekday names.</summary>
    public static readonly IReadOnlyList<string> Weekdays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>All built-in conditions.</summary>
    public static IEnumerable<ComponentDescriptor> All =>
    [
        new()
        {
            Type = "and",
            Kind = ComponentKind.Condition,
            Category = LogicCategory,
            Title = "All of",
            Description = "True when every nested condition is true. (The top-level condition list already works like this; use `and` inside `or`/`not`.)",
            Fields = [Fields.Conditions("conditions", "The conditions that must all be true.")],
            Example = """
                type: and
                conditions:
                  - type: power_source
                    is: battery
                  - type: battery
                    below: 30
                """,
        },
        new()
        {
            Type = "or",
            Kind = ComponentKind.Condition,
            Category = LogicCategory,
            Title = "Any of",
            Description = "True when at least one nested condition is true.",
            Fields = [Fields.Conditions("conditions", "At least one of these must be true.")],
            Example = """
                type: or
                conditions:
                  - type: time
                    after: "22:00"
                  - type: time
                    before: "06:00"
                """,
        },
        new()
        {
            Type = "not",
            Kind = ComponentKind.Condition,
            Category = LogicCategory,
            Title = "None of",
            Description = "True when none of the nested conditions is true.",
            Fields = [Fields.Conditions("conditions", "None of these may be true.")],
            Example = """
                type: not
                conditions:
                  - type: app_running
                    app: obs64.exe
                """,
        },
        new()
        {
            Type = "user",
            Kind = ComponentKind.Condition,
            Category = ContextCategory,
            Title = "User is",
            Description = "True when the user involved is one of the listed users. In machine automations this is the user from the event (who signed in or started the app); in personal automations it is you.",
            Fields = [Fields.Users("users", "User names, DOMAIN\\name, SIDs or wildcards.", required: true, example: "samet")],
            Example = """
                type: user
                users: [samet, guest*]
                """,
        },
        new()
        {
            Type = "time",
            Kind = ComponentKind.Condition,
            Category = ContextCategory,
            Title = "Time of day / weekday",
            Description = "True during a time window and/or on certain weekdays. Windows that cross midnight (after 22:00, before 06:00) are supported.",
            Fields =
            [
                Fields.Time("after", "Start of the window (inclusive), 24-hour clock.", example: "22:00"),
                Fields.Time("before", "End of the window (exclusive), 24-hour clock.", example: "06:00"),
                Fields.List("weekdays", "Only on these days.", Weekdays, example: "[mon, tue, wed, thu, fri]"),
            ],
            Validate = c => ValidationRules.Require(c.Has("after") || c.Has("before") || c.Has("weekdays"),
                "set at least one of 'after', 'before' or 'weekdays'"),
            Example = """
                type: time
                after: "09:00"
                before: "17:30"
                weekdays: [mon, tue, wed, thu, fri]
                """,
            Notes = "Quote times in YAML (\"22:00\") so they are not read as numbers.",
        },
        new()
        {
            Type = "power_source",
            Kind = ComponentKind.Condition,
            Category = DeviceCategory,
            Title = "Power source",
            Description = "True when the computer runs on the given power source.",
            Fields = [Fields.Choice("is", "ac (plugged in) or battery.", ["ac", "battery"], required: true)],
            Example = """
                type: power_source
                is: battery
                """,
            Notes = "Desktop PCs without a battery always report `ac`.",
        },
        new()
        {
            Type = "battery",
            Kind = ComponentKind.Condition,
            Category = DeviceCategory,
            Title = "Battery level",
            Description = "True when the battery charge is within the given range.",
            Fields =
            [
                Fields.Integer("above", "True when the charge is above this percentage.", min: 0, max: 100),
                Fields.Integer("below", "True when the charge is below this percentage.", min: 0, max: 100),
            ],
            Validate = c => ValidationRules.Require(c.Has("above") || c.Has("below"), "set 'above' and/or 'below'"),
            Example = """
                type: battery
                below: 30
                """,
            Notes = "False on computers without a battery.",
        },
        new()
        {
            Type = "app_running",
            Kind = ComponentKind.Condition,
            Category = AppsCategory,
            Title = "App is running",
            Description = "True when one of the apps is running (or, with running: false, when none is).",
            Fields =
            [
                Fields.Apps("app", "App patterns (exe name, path or wildcards).", required: true, example: "obs64.exe"),
                Fields.Boolean("running", "Set to false to require that none of the apps is running.", defaultValue: true),
            ],
            Example = """
                type: app_running
                app: obs64.exe
                running: false
                """,
            Notes = "In personal automations only apps in your own session count; in machine automations apps in any session count.",
        },
        new()
        {
            Type = "app_focused",
            Kind = ComponentKind.Condition,
            Category = AppsCategory,
            Title = "App is focused",
            Description = "True when one of the apps currently has the active window.",
            AvailableIn = ScopeSupport.User,
            Fields = [Fields.Apps("app", "App patterns.", required: true, example: "code.exe")],
            Example = """
                type: app_focused
                app: code.exe
                """,
        },
        new()
        {
            Type = "fullscreen",
            Kind = ComponentKind.Condition,
            Category = AppsCategory,
            Title = "Full-screen app active",
            Description = "True when a full-screen app, game or presentation is in the foreground.",
            AvailableIn = ScopeSupport.User,
            Fields = [Fields.Boolean("active", "Set to false to require that no full-screen app is active.", defaultValue: true)],
            Example = """
                type: fullscreen
                active: true
                """,
        },
        new()
        {
            Type = "wifi",
            Kind = ComponentKind.Condition,
            Category = DeviceCategory,
            Title = "Wi-Fi network",
            Description = "True when connected to one of the Wi-Fi networks (or, with connected: false, when connected to none of them).",
            Fields =
            [
                Fields.List("ssid", "Network names (wildcards allowed).", required: true, example: "HomeWiFi"),
                Fields.Boolean("connected", "Set to false to require that you are not on any of these networks.", defaultValue: true),
            ],
            Example = """
                type: wifi
                ssid: [HomeWiFi, HomeWiFi-5G]
                """,
            Notes = "On Windows 11 24H2 and later, reading the Wi-Fi network name requires Location access for desktop apps (Settings > Privacy & security > Location). Without it this condition is always false.",
        },
        new()
        {
            Type = "monitor_count",
            Kind = ComponentKind.Condition,
            Category = DeviceCategory,
            Title = "Number of monitors",
            Description = "True when the number of connected monitors is within the range. Handy for docked/undocked laptops.",
            Fields =
            [
                Fields.Integer("min", "At least this many monitors.", min: 0),
                Fields.Integer("max", "At most this many monitors.", min: 0),
            ],
            Validate = c => ValidationRules.Require(c.Has("min") || c.Has("max"), "set 'min' and/or 'max'"),
            Example = """
                type: monitor_count
                min: 2
                """,
        },
        new()
        {
            Type = "profile_active",
            Kind = ComponentKind.Condition,
            Category = ContextCategory,
            Title = "Profile is active",
            Description = "True when the profile is currently applied (or, with active: false, when it is not).",
            Fields =
            [
                Fields.Text("profile", "Profile id.", required: true, example: "gaming"),
                Fields.Boolean("active", "Set to false to require that the profile is not applied.", defaultValue: true),
            ],
            Example = """
                type: profile_active
                profile: gaming
                active: false
                """,
        },
    ];
}
