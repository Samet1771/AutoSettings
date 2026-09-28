namespace AutoSettings.Core.Config;

/// <summary>Contents written to a new configuration file.</summary>
public static class StarterConfig
{
    /// <summary>Starter file for a user's personal automations.</summary>
    public const string User = """
        # AutoSettings — your personal automations.
        #
        # An automation says: WHEN a trigger fires, IF the conditions are true, THEN run the actions.
        # A profile is a named set of actions that can be undone automatically.
        #
        # The examples below are disabled. Set "enabled: true" (or delete the line) to use them.
        # Full reference: https://github.com/Samet1771/WindowsSettingAutomation/tree/main/docs
        version: 1

        automations:
          - id: welcome
            name: Say hello when I sign in
            enabled: false
            triggers:
              - type: logon
            actions:
              - type: notify
                message: "Welcome back, {{ user }}!"

          - id: presentation-mode
            name: Presentation mode while PowerPoint is focused
            enabled: false
            triggers:
              - type: app_focused
                app: POWERPNT.EXE
            actions:
              # Reverted automatically when PowerPoint loses focus.
              - type: profile.apply
                profile: presentation

          - id: battery-saver
            name: Dim the screen on battery in the evening
            enabled: false
            triggers:
              - type: unlock
              - type: logon
            conditions:
              - type: power_source
                is: battery
              - type: time
                after: "20:00"
            actions:
              - type: display.brightness
                level: 40

        profiles:
          - id: presentation
            name: Presentation
            actions:
              - type: audio.mute
                state: mute
              - type: power.screen_timeout
                minutes: 0
        """;

    /// <summary>Starter file for machine automations (edited by administrators).</summary>
    public const string Machine = """
        # AutoSettings — machine automations (apply to every user; only administrators can edit this file).
        #
        # Machine automations run in the AutoSettings service as SYSTEM. They can use the 'boot' trigger
        # and machine-only actions such as service.control or registry.set on HKLM. Actions that change a
        # user's own settings (theme, audio, notifications, ...) are sent to the agent of the user the
        # event belongs to.
        #
        # Full reference: https://github.com/Samet1771/WindowsSettingAutomation/tree/main/docs
        version: 1

        automations:
          - id: greet-on-logon
            name: Greet whoever signs in
            enabled: false
            triggers:
              - type: logon
            actions:
              - type: notify
                message: "Hello {{ user }}, AutoSettings is running."
        """;
}
