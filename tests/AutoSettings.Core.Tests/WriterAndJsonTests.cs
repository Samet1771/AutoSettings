using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Ipc;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Tests;

public class WriterAndJsonTests
{
    private const string Source = """
        version: 1
        automations:
          - id: evening
            name: "Evening: dim"
            cooldown: 90s
            triggers:
              - type: unlock
              - type: app_started
                app: [steam.exe, obs64.exe]
            conditions:
              - type: time
                after: "20:00"
                weekdays: [mon, fri]
            actions:
              - type: notify
                message: "{{ user }}, it is evening"
              - type: command.run
                command: |
                  Write-Output "one"
                  Write-Output "two"
              - type: registry.set
                key: HKCU\Software\Test
                name: Flag
                value: "1"
                kind: dword
          - id: disabled
            enabled: false
            triggers: [lock]
            actions:
              - type: profile.apply
                profile: night
        profiles:
          - id: night
            priority: 3
            actions:
              - type: theme.mode
                mode: dark
        """;

    [Fact]
    public void Writing_and_reading_back_gives_the_same_configuration()
    {
        var original = TestSupport.LoadValid(Source);

        var written = YamlConfigWriter.Write(original, header: "Written by a test");
        var reloaded = TestSupport.LoadValid(written);

        Assert.StartsWith("# Written by a test\n", written);
        Assert.Equal(written, YamlConfigWriter.Write(reloaded, header: "Written by a test"));
        Assert.Equal(2, reloaded.Automations.Count);
        var evening = reloaded.Automations[0];
        Assert.Equal("Evening: dim", evening.Name);
        Assert.Equal(TimeSpan.FromSeconds(90), evening.Cooldown);
        Assert.Equal("{{ user }}, it is evening", evening.Actions[0].GetString("message"));
        Assert.Equal("Write-Output \"one\"\nWrite-Output \"two\"\n", evening.Actions[1].GetString("command"));
        Assert.Equal("1", evening.Actions[2].GetString("value"));
        Assert.False(reloaded.Automations[1].Enabled);
        Assert.Equal(3, reloaded.Profiles[0].Priority);
    }

    [Theory]
    [InlineData("22:00", true)]
    [InlineData("true", true)]
    [InlineData("123", true)]
    [InlineData("{{ user }}", true)]
    [InlineData("a: b", true)]
    [InlineData("chrome.exe", false)]
    [InlineData(@"C:\Program Files\x.exe", false)]
    public void Quotes_only_when_needed(string value, bool quoted) =>
        Assert.Equal(quoted, YamlConfigWriter.NeedsQuotes(value));

    [Fact]
    public void Parameters_survive_a_json_round_trip()
    {
        var action = new ComponentConfig("app.close", new Dictionary<string, object?>
        {
            ["app"] = new List<string> { "a.exe", "b.exe" },
            ["force"] = true,
            ["timeout"] = TimeSpan.FromSeconds(5),
        });

        var json = PlainJson.Serialize(action.Parameters);
        var copy = new ComponentConfig("app.close", PlainJson.Deserialize(json));
        var issues = new ConfigValidator().NormalizeComponent(ComponentKind.Action, copy, ExecutionScope.User);

        Assert.Empty(issues);
        Assert.Equal(new[] { "a.exe", "b.exe" }, copy.GetStringList("app"));
        Assert.Equal(true, copy.GetBoolean("force"));
        Assert.Equal(TimeSpan.FromSeconds(5), copy.GetDuration("timeout"));
    }
}
