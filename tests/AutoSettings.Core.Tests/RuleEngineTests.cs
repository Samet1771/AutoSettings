using AutoSettings.Core.Catalog;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using static AutoSettings.Core.Tests.TestSupport;

namespace AutoSettings.Core.Tests;

public class RuleEngineTests
{
    private readonly RecordingAction _notify = new("notify");
    private readonly FakeVolume _volume = new();
    private readonly HandlerRegistry _handlers = new();

    public RuleEngineTests()
    {
        _handlers.Add(_notify).Add(_volume).Add(new FailingAction("open"));
    }

    [Fact]
    public async Task Focus_trigger_runs_actions_with_placeholders()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers:
                  - type: app_focused
                    app: code
                actions:
                  - type: notify
                    message: "{{ user }} is using {{ app.name }}"
            """, _handlers);

        await engine.SendAsync(App(SystemEventKind.AppFocused, "notepad.exe"), App(SystemEventKind.AppFocused, "Code.exe"));

        var call = Assert.Single(_notify.Calls);
        Assert.Equal("samet is using Code", call.GetString("message"));
        Assert.Equal("AutoSettings", call.GetString("title"));
    }

    [Fact]
    public async Task Conditions_must_all_be_true()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 28, 23, 0, 0, TimeSpan.Zero));
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers: [unlock]
                conditions:
                  - type: time
                    after: "22:00"
                    before: "06:00"
                  - type: user
                    users: samet
                actions:
                  - type: notify
                    message: night
            """, _handlers, time);

        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Single(_notify.Calls);

        time.Set(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Single(_notify.Calls);
        Assert.Contains(engine.Log.Snapshot(), e => e.Message.Contains("condition 1 (time") && e.Message.Contains("was not met"));
    }

    [Fact]
    public async Task Disabled_and_paused_automations_do_not_run()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: on
                triggers: [lock]
                actions:
                  - type: notify
                    message: on
              - id: off
                enabled: false
                triggers: [lock]
                actions:
                  - type: notify
                    message: off
            """, _handlers);

        engine.Pause();
        await engine.SendAsync(Session(SystemEventKind.Lock));
        Assert.Empty(_notify.Calls);

        engine.Resume();
        await engine.SendAsync(Session(SystemEventKind.Lock));
        Assert.Equal("on", Assert.Single(_notify.Calls).GetString("message"));
    }

    [Fact]
    public async Task Timed_pause_expires()
    {
        var time = new ManualTime();
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers: [lock]
                actions:
                  - type: notify
                    message: hi
            """, _handlers, time);

        engine.Pause(TimeSpan.FromHours(1));
        await engine.SendAsync(Session(SystemEventKind.Lock));
        time.Advance(TimeSpan.FromMinutes(61));
        await engine.SendAsync(Session(SystemEventKind.Lock));

        Assert.Single(_notify.Calls);
        Assert.False(engine.IsPaused);
    }

    [Fact]
    public async Task Cooldown_ignores_triggers_that_come_too_soon()
    {
        var time = new ManualTime();
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                cooldown: 1m
                triggers: [unlock]
                actions:
                  - type: notify
                    message: hi
            """, _handlers, time);

        await engine.SendAsync(Session(SystemEventKind.Unlock));
        time.Advance(TimeSpan.FromSeconds(30));
        await engine.SendAsync(Session(SystemEventKind.Unlock));
        time.Advance(TimeSpan.FromSeconds(31));
        await engine.SendAsync(Session(SystemEventKind.Unlock));

        Assert.Equal(2, _notify.Calls.Count);
    }

    [Fact]
    public async Task Loop_guard_suspends_runaway_automations()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: loop
                triggers: [unlock]
                actions:
                  - type: notify
                    message: hi
            """, _handlers, options: new EngineOptions { MaxRunsPerMinute = 3 });

        for (var i = 0; i < 6; i++)
            await engine.SendAsync(Session(SystemEventKind.Unlock));

        Assert.Equal(3, _notify.Calls.Count);
        Assert.Contains("loop", engine.SuspendedAutomations);

        engine.ResumeAutomation("loop");
        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Equal(4, _notify.Calls.Count);
    }

    [Fact]
    public async Task App_started_and_closed_default_to_first_and_last_instance()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: started
                triggers:
                  - type: app_started
                    app: chrome.exe
                actions:
                  - type: notify
                    message: started
              - id: closed
                triggers:
                  - type: app_closed
                    app: chrome.exe
                actions:
                  - type: notify
                    message: closed
            """, _handlers);

        await engine.SendAsync(
            App(SystemEventKind.AppStarted, "chrome.exe", pid: 1),
            App(SystemEventKind.AppStarted, "chrome.exe", pid: 2),
            App(SystemEventKind.AppClosed, "chrome.exe", pid: 1));
        Assert.Equal(new[] { "started" }, _notify.Calls.Select(c => c.GetString("message")));

        await engine.SendAsync(App(SystemEventKind.AppClosed, "chrome.exe", pid: 2));
        Assert.Equal(new[] { "started", "closed" }, _notify.Calls.Select(c => c.GetString("message")));
    }

    [Fact]
    public async Task Instance_any_fires_for_every_process()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: started
                triggers:
                  - type: app_started
                    app: chrome
                    instance: any
                actions:
                  - type: notify
                    message: started
            """, _handlers);

        await engine.SendAsync(App(SystemEventKind.AppStarted, "chrome.exe", pid: 1), App(SystemEventKind.AppStarted, "chrome.exe", pid: 2));

        Assert.Equal(2, _notify.Calls.Count);
    }

    [Fact]
    public async Task Logon_trigger_filters_by_user()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers:
                  - type: logon
                    user: [guest, "PC\\sam*"]
                actions:
                  - type: notify
                    message: "hello {{ user.full }}"
            """, _handlers, scope: ExecutionScope.Machine);

        await engine.SendAsync(Session(SystemEventKind.Logon, new UserInfo("admin", "PC")), Session(SystemEventKind.Logon));

        Assert.Equal("hello PC\\samet", Assert.Single(_notify.Calls).GetString("message"));
    }

    [Fact]
    public async Task Profile_applied_on_focus_is_reverted_on_unfocus_of_the_same_app()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: quiet-while-focused
                triggers:
                  - type: app_focused
                    app: POWERPNT.EXE
                actions:
                  - type: profile.apply
                    profile: quiet
            profiles:
              - id: quiet
                actions:
                  - type: audio.volume
                    level: 10
            """, _handlers);

        await engine.SendAsync(App(SystemEventKind.AppFocused, "POWERPNT.EXE"));
        Assert.Equal(10, _volume.Level);
        Assert.True(engine.IsProfileActive("quiet"));

        await engine.SendAsync(App(SystemEventKind.AppUnfocused, "notepad.exe"));
        Assert.Equal(10, _volume.Level);

        await engine.SendAsync(App(SystemEventKind.AppUnfocused, "POWERPNT.EXE"));
        Assert.Equal(50, _volume.Level);
        Assert.False(engine.IsProfileActive("quiet"));
    }

    [Fact]
    public async Task Higher_priority_profile_wins_and_baseline_returns_last()
    {
        var engine = CreateEngine(StackedProfiles, _handlers);

        await engine.ApplyProfileAsync("low");
        Assert.Equal(20, _volume.Level);
        await engine.ApplyProfileAsync("high");
        Assert.Equal(80, _volume.Level);

        await engine.RevertProfileAsync("low");
        Assert.Equal(80, _volume.Level);
        await engine.RevertProfileAsync("high");
        Assert.Equal(50, _volume.Level);
    }

    [Fact]
    public async Task Lower_priority_profile_waits_for_the_higher_one()
    {
        var engine = CreateEngine(StackedProfiles, _handlers);

        await engine.ApplyProfileAsync("high");
        await engine.ApplyProfileAsync("low");
        Assert.Equal(80, _volume.Level);
        Assert.Equal(new[] { 80L }, _volume.History);

        await engine.RevertProfileAsync("high");
        Assert.Equal(20, _volume.Level);
        await engine.RevertProfileAsync("low");
        Assert.Equal(50, _volume.Level);
    }

    [Fact]
    public async Task Failed_action_stops_the_automation_unless_continue_on_error()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: stops
                triggers: [lock]
                actions:
                  - type: open
                    target: https://example.com
                  - type: notify
                    message: not reached
              - id: continues
                triggers: [unlock]
                actions:
                  - type: open
                    target: https://example.com
                    continue_on_error: true
                  - type: notify
                    message: reached
            """, _handlers);

        await engine.SendAsync(Session(SystemEventKind.Lock), Session(SystemEventKind.Unlock));

        Assert.Equal("reached", Assert.Single(_notify.Calls).GetString("message"));
        Assert.Contains(engine.Log.Snapshot(), e => e.Level == ActivityLevel.Error && e.Message.Contains("boom"));
    }

    [Fact]
    public async Task Dry_run_executes_nothing()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers: [lock]
                actions:
                  - type: notify
                    message: hi
            """, _handlers);
        engine.DryRun = true;

        await engine.SendAsync(Session(SystemEventKind.Lock));

        Assert.Empty(_notify.Calls);
        Assert.Contains(engine.Log.Snapshot(), e => e.Message.StartsWith("[dry run]"));
    }

    [Fact]
    public async Task Test_button_runs_disabled_automation()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                enabled: false
                triggers: [lock]
                actions:
                  - type: notify
                    message: hi
            """, _handlers);

        Assert.True(await engine.RunAutomationAsync("a", checkConditions: true));
        Assert.Single(_notify.Calls);
    }

    [Fact]
    public async Task App_running_condition_uses_tracked_processes()
    {
        var engine = CreateEngine("""
            version: 1
            automations:
              - id: a
                triggers: [unlock]
                conditions:
                  - type: app_running
                    app: obs64
                    running: false
                actions:
                  - type: notify
                    message: hi
            """, _handlers);
        engine.Processes.Seed([(1, new ProcessInfo(5, "obs64.exe"))]);

        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Empty(_notify.Calls);

        await engine.SendAsync(App(SystemEventKind.AppClosed, "obs64.exe", pid: 5), Session(SystemEventKind.Unlock));
        Assert.Single(_notify.Calls);
    }

    private const string StackedProfiles = """
        version: 1
        profiles:
          - id: low
            actions:
              - type: audio.volume
                level: 20
          - id: high
            priority: 10
            actions:
              - type: audio.volume
                level: 80
        """;
}
