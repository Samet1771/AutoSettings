using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Tests;

public class ConfigLoaderTests
{
    private const string Valid = """
        version: 1
        automations:
          - id: gaming
            name: Gaming mode
            cooldown: 30s
            triggers:
              - type: app_started
                app: [steam.exe, "*game*"]
            conditions:
              - type: power_source
                is: AC
              - type: or
                conditions:
                  - type: time
                    after: "22:00"
                  - type: time
                    before: "06:00"
            actions:
              - type: audio.volume
                level: "30"
              - type: profile.apply
                profile: quiet
        profiles:
          - id: quiet
            priority: 5
            actions:
              - type: audio.mute
                state: mute
        """;

    [Fact]
    public void Loads_a_valid_file_and_normalizes_values()
    {
        var result = ConfigLoader.Load(Valid, ExecutionScope.User);

        Assert.False(result.HasErrors, string.Join("\n", result.Issues));
        var automation = Assert.Single(result.Config.Automations);
        Assert.Equal("gaming", automation.Id);
        Assert.Equal(TimeSpan.FromSeconds(30), automation.Cooldown);
        Assert.Equal(new[] { "steam.exe", "*game*" }, automation.Triggers[0].GetStringList("app"));
        Assert.Equal("ac", automation.Conditions[0].Parameters["is"]);
        Assert.Equal(30L, automation.Actions[0].Parameters["level"]);
        var nested = automation.Conditions[1].GetComponents("conditions");
        Assert.Equal(2, nested.Count);
        Assert.Equal(new TimeOnly(22, 0), nested[0].Parameters["after"]);
        Assert.Equal(5, Assert.Single(result.Config.Profiles).Priority);
    }

    [Fact]
    public void Empty_file_is_valid()
    {
        var result = ConfigLoader.Load("", ExecutionScope.User);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Config.Automations);
    }

    [Fact]
    public void Reports_syntax_errors_with_a_line_number()
    {
        var result = ConfigLoader.Load("version: 1\nautomations:\n  - id: [unclosed\n", ExecutionScope.User);

        var error = Assert.Single(result.Errors);
        Assert.StartsWith("YAML syntax error", error.Message);
        Assert.NotNull(error.Location);
    }

    [Fact]
    public void Suggests_the_closest_trigger_type()
    {
        var result = Load("""
            triggers:
              - type: app_focus
                app: code.exe
            actions:
              - type: notify
                message: hi
            """);

        var error = Assert.Single(result.Errors);
        Assert.Contains("unknown trigger type 'app_focus'", error.Message);
        Assert.Contains("Did you mean 'app_focused'?", error.Message);
        Assert.Equal(5, error.Location?.Line);
    }

    [Fact]
    public void Reports_missing_required_fields_and_bad_values()
    {
        var result = Load("""
            triggers:
              - type: app_started
            actions:
              - type: audio.volume
                level: 150
              - type: theme.mode
                mode: purple
            """);

        var messages = result.Errors.Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("'app' is required"));
        Assert.Contains(messages, m => m.Contains("invalid 'level': must be at most 100"));
        Assert.Contains(messages, m => m.Contains("'purple' is not one of: light, dark, toggle"));
    }

    [Fact]
    public void Warns_about_unknown_fields_and_keys()
    {
        var result = Load("""
            triggers:
              - type: logon
                usr: samet
            actions:
              - type: notify
                message: hi
            colour: blue
            """);

        Assert.False(result.HasErrors);
        var warnings = result.Warnings.Select(w => w.Message).ToList();
        Assert.Contains(warnings, m => m.Contains("unknown field 'usr'") && m.Contains("Did you mean 'user'?"));
        Assert.Contains(warnings, m => m.Contains("Unknown key 'colour'"));
    }

    [Fact]
    public void Machine_only_actions_are_rejected_in_personal_automations()
    {
        var result = Load("""
            triggers:
              - type: logon
            actions:
              - type: service.control
                name: wuauserv
                state: stopped
              - type: registry.set
                key: HKLM\Software\Test
                name: X
                value: "1"
                kind: dword
              - type: command.run
                command: whoami
                run_as: system
            """);

        Assert.Equal(3, result.Errors.Count(e => e.Message.Contains("only be used in machine automations")));
    }

    [Fact]
    public void Personal_registry_and_commands_are_allowed()
    {
        var result = Load("""
            triggers:
              - type: logon
            actions:
              - type: registry.set
                key: HKCU\Software\Test
                name: X
                value: "1"
                kind: dword
              - type: command.run
                command: whoami
            """);

        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
    }

    [Fact]
    public void Focus_triggers_are_personal_and_boot_is_machine_only()
    {
        var machine = ConfigLoader.Load("""
            version: 1
            automations:
              - id: a
                triggers:
                  - type: app_focused
                    app: code.exe
                actions:
                  - type: notify
                    message: hi
            """, ExecutionScope.Machine);
        Assert.Contains(machine.Errors, e => e.Message.Contains("can only be used in personal automations"));

        var user = Load("""
            triggers:
              - type: boot
            actions:
              - type: notify
                message: hi
            """);
        Assert.Contains(user.Errors, e => e.Message.Contains("can only be used in machine automations"));
    }

    [Fact]
    public void Profile_references_must_exist()
    {
        var result = Load("""
            triggers:
              - type: logon
            actions:
              - type: profile.apply
                profile: gamng
            """, profiles: """
            profiles:
              - id: gaming
                actions:
                  - type: audio.mute
                    state: mute
            """);

        var error = Assert.Single(result.Errors);
        Assert.Contains("no profile with the id 'gamng'", error.Message);
        Assert.Contains("Did you mean 'gaming'?", error.Message);
    }

    [Fact]
    public void Profiles_cannot_apply_profiles()
    {
        var result = ConfigLoader.Load("""
            version: 1
            profiles:
              - id: a
                actions:
                  - type: profile.apply
                    profile: a
            """, ExecutionScope.User);

        Assert.Contains(result.Errors, e => e.Message.Contains("a profile cannot apply or revert other profiles"));
    }

    [Fact]
    public void Generates_missing_ids_and_rejects_duplicates()
    {
        var result = ConfigLoader.Load("""
            version: 1
            automations:
              - name: Dark mode at night!
                triggers: [unlock]
                actions:
                  - type: theme.mode
                    mode: dark
              - id: x
                triggers: [lock]
                actions:
                  - type: theme.mode
                    mode: light
              - id: X
                triggers: [lock]
                actions:
                  - type: theme.mode
                    mode: light
            """, ExecutionScope.User);

        Assert.Equal("dark-mode-at-night", result.Config.Automations[0].Id);
        Assert.Contains(result.Errors, e => e.Message.Contains("already uses the id 'X'"));
    }

    [Fact]
    public void Custom_validation_runs_after_field_validation()
    {
        var result = Load("""
            triggers:
              - type: logon
            conditions:
              - type: time
            actions:
              - type: display.resolution
                width: 1920
            """);

        var messages = result.Errors.Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("set at least one of 'after', 'before' or 'weekdays'"));
        Assert.Contains(messages, m => m.Contains("set both 'width' and 'height', or neither"));
    }

    [Fact]
    public void Errors_in_nested_conditions_are_reported()
    {
        var result = Load("""
            triggers:
              - type: logon
            conditions:
              - type: or
                conditions:
                  - type: battery
                    below: lots
            actions:
              - type: notify
                message: hi
            """);

        var error = Assert.Single(result.Errors);
        Assert.Contains("condition 1 (or), condition 1 (battery): invalid 'below'", error.Message);
    }

    [Fact]
    public void Unsupported_version_is_an_error()
    {
        var result = ConfigLoader.Load("version: 2\n", ExecutionScope.User);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unsupported file version 2"));
    }

    private static ConfigLoadResult Load(string automationBody, string profiles = "", ExecutionScope scope = ExecutionScope.User)
    {
        var body = string.Join("\n", automationBody.Split('\n').Select(line => "    " + line));
        var yaml = "version: 1\nautomations:\n  - id: test\n" + body + "\n" + profiles;
        return ConfigLoader.Load(yaml, scope);
    }
}
