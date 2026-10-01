using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;
using AutoSettings.Platform.Plugins;

namespace AutoSettings.Platform.Tests;

// Tests that set process environment variables run one at a time.
[Collection("Environment")]
public sealed class ScriptPluginTests : IDisposable
{
    private static readonly UserInfo Tester = new("tester", "PC");
    private readonly string _temp = Directory.CreateTempSubdirectory("autosettings-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Script(string name, string content)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, content);
        return name;
    }

    [Fact]
    public async Task Runner_passes_the_request_and_environment_as_utf8()
    {
        var script = Script("echo.ps1", "$in = [Console]::In.ReadToEnd() | ConvertFrom-Json\n\"$($in.word)|$env:AUTOSETTINGS_TEST\"");

        var result = await ScriptRunner.RunAsync(_temp, script, """{"word":"Türkçe ğüşı"}""",
            new Dictionary<string, string> { ["AUTOSETTINGS_TEST"] = "çalışıyor" }, null, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Türkçe ğüşı|çalışıyor", result.Output.Trim());
    }

    [Fact]
    public async Task Runner_reports_exit_codes_and_errors()
    {
        var script = Script("fail.ps1", "[Console]::Error.WriteLine('disk is full'); exit 3");

        var result = await ScriptRunner.RunAsync(_temp, script, "{}", new Dictionary<string, string>(), null, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("disk is full", result.FailureMessage);
    }

    [Fact]
    public async Task Runner_treats_an_uncaught_error_as_failure()
    {
        var script = Script("throw.ps1", "throw 'nope'");

        var result = await ScriptRunner.RunAsync(_temp, script, "{}", new Dictionary<string, string>(), null, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("nope", result.FailureMessage);
    }

    [Fact]
    public async Task Runner_stops_scripts_that_take_too_long()
    {
        var script = Script("slow.ps1", "Start-Sleep -Seconds 30");

        var ex = await Assert.ThrowsAsync<ScriptException>(() =>
            ScriptRunner.RunAsync(_temp, script, "{}", new Dictionary<string, string>(), TimeSpan.FromSeconds(2), CancellationToken.None));
        Assert.Contains("did not finish", ex.Message);
    }

    [Theory]
    [InlineData("..\\outside.ps1")]
    [InlineData("C:\\Windows\\System32\\outside.ps1")]
    public async Task Runner_refuses_scripts_outside_the_plugin_folder(string script)
    {
        await Assert.ThrowsAsync<ScriptException>(() =>
            ScriptRunner.RunAsync(_temp, script, "{}", new Dictionary<string, string>(), null, CancellationToken.None));
    }

    /// <summary>Installs the hello-script sample into a temporary plugin folder and loads it.</summary>
    private (PluginRuntime Runtime, HandlerRegistry Handlers, List<SystemEvent> Events, ActivityLog Log) LoadSample()
    {
        var root = Path.Combine(_temp, "plugins");
        CopyDirectory(Path.Combine(RepositoryRoot(), "samples", "plugins", "hello-script", "1.0.0"), Path.Combine(root, "example.hello", "1.0.0"));
        var handlers = new HandlerRegistry();
        var events = new List<SystemEvent>();
        var log = new ActivityLog();
        var runtime = new PluginRuntime(
            new PluginHostContext(ExecutionScope.User, log, Tester, 1, Path.Combine(_temp, "state")),
            [new PluginRoot(root, ExecutionScope.User)],
            handlers,
            [new ScriptBackend()],
            e => { lock (events) events.Add(e); },
            null);
        runtime.Load();
        return (runtime, handlers, events, log);
    }

    [Fact]
    public void Sample_plugin_loads_into_the_catalog()
    {
        var (runtime, handlers, _, log) = LoadSample();
        using (runtime)
        {
            var plugin = Assert.Single(runtime.Plugins);
            Assert.True(plugin.IsActive, string.Join("\n", plugin.Issues) + string.Join("\n", log.Snapshot().Select(e => e.Message)));
            Assert.NotNull(runtime.Catalog.Find(ComponentKind.Action, "example.hello.write_text"));
            Assert.NotNull(runtime.Catalog.Find(ComponentKind.Trigger, "example.hello.file_removed"));
            Assert.IsAssignableFrom<IRevertibleActionHandler>(handlers.FindAction("example.hello.write_text"));
            Assert.NotNull(handlers.FindCondition("example.hello.file_contains"));
        }
    }

    [Fact]
    public async Task Sample_action_condition_capture_and_restore_work()
    {
        var (runtime, handlers, _, _) = LoadSample();
        using (runtime)
        {
            var file = Path.Combine(_temp, "out", "status.txt");
            var action = new ComponentConfig("example.hello.write_text", new Dictionary<string, object?> { ["path"] = file, ["text"] = "busy until 5" });
            var context = new ActionContext(null, "a", "test", new ActivityLog()) { CurrentUser = Tester };
            var write = (IRevertibleActionHandler)handlers.FindAction("example.hello.write_text")!;
            var contains = handlers.FindCondition("example.hello.file_contains")!;
            var conditionContext = new ConditionContext(null, Tester, new ProcessTracker(), TimeProvider.System);

            var snapshot = await write.CaptureAsync(action, context, CancellationToken.None);
            Assert.Equal("missing", snapshot);

            await write.ExecuteAsync(action, context, CancellationToken.None);
            Assert.Equal("busy until 5", File.ReadAllText(file).Trim('\uFEFF'));

            Assert.True(await contains.EvaluateAsync(new ComponentConfig("example.hello.file_contains", new Dictionary<string, object?> { ["path"] = file, ["text"] = "BUSY" }), conditionContext, CancellationToken.None));
            Assert.False(await contains.EvaluateAsync(new ComponentConfig("example.hello.file_contains", new Dictionary<string, object?> { ["path"] = file, ["text"] = "free" }), conditionContext, CancellationToken.None));

            await write.RestoreAsync(action, snapshot, context, CancellationToken.None);
            Assert.False(File.Exists(file));
        }
    }

    [Fact]
    public async Task Sample_trigger_raises_events_for_new_files()
    {
        var watched = Path.Combine(_temp, "watched");
        Directory.CreateDirectory(watched);
        File.WriteAllText(Path.Combine(watched, "old.txt"), "already there");
        Environment.SetEnvironmentVariable("AUTOSETTINGS_HELLO_FOLDER", watched);
        try
        {
            var (runtime, _, events, log) = LoadSample();
            using (runtime)
            {
                var config = ConfigLoader.Load("""
                    version: 1
                    automations:
                      - id: hello
                        triggers:
                          - type: example.hello.file_added
                        actions:
                          - type: notify
                            message: "{{ event.data.name }}"
                    """, ExecutionScope.User, runtime.Catalog);
                Assert.False(config.HasErrors, string.Join("\n", config.Errors));
                runtime.UseConfig(config.Config);

                // The first poll only records old.txt; then a new file must be reported. Wait for the first poll's
                // state file rather than a fixed time: PowerShell can take several seconds to start on a busy machine,
                // and a file written before the first poll counts as already there.
                var stateFile = Path.Combine(_temp, "state", "example.hello", "files.txt");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (!File.Exists(stateFile) && DateTime.UtcNow < deadline)
                    await Task.Delay(250);
                Assert.True(File.Exists(stateFile), "The first poll did not run. Log:\n" + string.Join("\n", log.Snapshot().Select(x => x.Message)));
                File.WriteAllText(Path.Combine(watched, "new.txt"), "hello");
                deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    lock (events)
                    {
                        if (events.Count > 0)
                            break;
                    }
                    await Task.Delay(250);
                }

                SystemEvent e;
                lock (events)
                {
                    Assert.True(events.Count > 0, "No event. Log:\n" + string.Join("\n", log.Snapshot().Select(x => x.Message)));
                    e = events[0];
                }
                Assert.Equal(SystemEventKind.Plugin, e.Kind);
                Assert.Equal("example.hello.file_added", e.PluginEvent);
                Assert.Equal("new.txt", e.Data!["name"]);
                Assert.Equal(1, e.SessionId);
                Assert.DoesNotContain(events, x => x.Data!["name"] == "old.txt");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOSETTINGS_HELLO_FOLDER", null);
        }
    }

    [Fact]
    public void A_broken_plugin_does_not_stop_the_others()
    {
        var root = Path.Combine(_temp, "plugins");
        Directory.CreateDirectory(Path.Combine(root, "acme.broken", "1.0.0"));
        File.WriteAllText(Path.Combine(root, "acme.broken", "1.0.0", "plugin.yaml"), "id: acme.broken\nthis is: [not valid");
        var (runtime, _, _, log) = LoadSample();
        using (runtime)
        {
            Assert.Equal(2, runtime.Plugins.Count);
            Assert.Contains(runtime.Plugins, p => p.Id == "example.hello" && p.IsActive);
            Assert.Contains(runtime.Plugins, p => p.Id == "acme.broken" && !p.IsValid);
            Assert.Contains(log.Snapshot(), e => e.Level == ActivityLevel.Error && e.Message.Contains("acme.broken"));
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoSettings.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
