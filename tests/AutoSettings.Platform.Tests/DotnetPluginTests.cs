using System.Diagnostics;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;
using AutoSettings.Platform.Plugins;

namespace AutoSettings.Platform.Tests;

[Collection("Environment")]
public sealed class DotnetPluginTests : IDisposable
{
    private static readonly UserInfo Tester = new("tester", "PC");
    private readonly string _temp = Directory.CreateTempSubdirectory("autosettings-dotnet-").FullName;
    private readonly string _watched;

    public DotnetPluginTests()
    {
        _watched = Path.Combine(_temp, "watched");
        Directory.CreateDirectory(_watched);
        Environment.SetEnvironmentVariable("EXAMPLE_HELLO_PROCESS_SCOPE", "1");
        Environment.SetEnvironmentVariable("AUTOSETTINGS_HELLO_FOLDER", _watched);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EXAMPLE_HELLO_PROCESS_SCOPE", null);
        Environment.SetEnvironmentVariable("AUTOSETTINGS_HELLO_FOLDER", null);
        foreach (var host in Process.GetProcessesByName("AutoSettings.PluginHost"))
        {
            try
            {
                host.Kill();
            }
            catch (Exception)
            {
            }
        }
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    private static string Configuration => AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}") ? "Debug" : "Release";

    private static string HostPath => Path.Combine(ScriptPluginTests.RepositoryRoot(), "src", "AutoSettings.PluginHost", "bin", Configuration, "net10.0-windows10.0.19041.0", "AutoSettings.PluginHost.exe");

    /// <summary>Does what "autosettings-plugin pack" does, into a plugin folder: copy the build output and write plugin.yaml with the components.</summary>
    private string InstallSample()
    {
        var output = Path.Combine(ScriptPluginTests.RepositoryRoot(), "samples", "plugins", "HelloDotnet", "bin", Configuration, "net10.0");
        var target = Path.Combine(_temp, "plugins", "example.dotnet", "1.0.0");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(output))
        {
            if (!Path.GetFileName(file).StartsWith("AutoSettings.Sdk", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        var (manifest, issues) = PluginManifestReader.Read(File.ReadAllText(Path.Combine(output, "plugin.yaml")));
        Assert.Empty(issues);
        manifest.Components = SdkDescriber.Describe(PluginLoadContext.LoadPlugin(Path.Combine(target, manifest.Entry!)));
        File.WriteAllText(Path.Combine(target, "plugin.yaml"), PluginManifestWriter.Write(manifest));
        return Path.Combine(_temp, "plugins");
    }

    private (PluginRuntime Runtime, HandlerRegistry Handlers, List<SystemEvent> Events, ActivityLog Log) Load()
    {
        Assert.True(File.Exists(HostPath), $"{HostPath} was not built.");
        var root = InstallSample();
        var handlers = new HandlerRegistry();
        var events = new List<SystemEvent>();
        var log = new ActivityLog();
        var runtime = new PluginRuntime(
            new PluginHostContext(ExecutionScope.User, log, Tester, 1, Path.Combine(_temp, "state")),
            [new PluginRoot(root, ExecutionScope.User)],
            handlers,
            [new DotnetBackend(HostPath)],
            e => { lock (events) events.Add(e); },
            null);
        runtime.Load();
        return (runtime, handlers, events, log);
    }

    private static string Messages(ActivityLog log) => string.Join("\n", log.Snapshot().Select(e => $"{e.Level}: {e.Message}"));

    [Fact]
    public void Sample_components_come_from_the_attributes()
    {
        var (runtime, _, _, log) = Load();
        using (runtime)
        {
            var plugin = Assert.Single(runtime.Plugins);
            Assert.True(plugin.IsActive, string.Join("\n", plugin.Issues) + Messages(log));
            var set = runtime.Catalog.Find(ComponentKind.Action, "example.dotnet.set_variable")!;
            Assert.True(set.Revertible);
            Assert.Equal(new[] { "name" }, set.KeyFields);
            Assert.Equal("Ortam değişkeni ayarla", set.Localized["tr"].Title);
            var created = runtime.Catalog.Find(ComponentKind.Trigger, "example.dotnet.file_created")!;
            Assert.Equal("example.dotnet.file_deleted", created.OppositeEvent);
            Assert.NotNull(runtime.Catalog.Find(ComponentKind.Trigger, "example.dotnet.file_deleted"));
            Assert.NotNull(runtime.Catalog.Find(ComponentKind.Condition, "example.dotnet.variable_equals"));
        }
    }

    [Fact]
    public async Task Sample_action_condition_capture_and_restore_run_in_the_host()
    {
        var (runtime, handlers, _, log) = Load();
        using (runtime)
        {
            var action = new ComponentConfig("example.dotnet.set_variable", new Dictionary<string, object?> { ["name"] = "AUTOSETTINGS_TEST_VAR", ["value"] = "on" });
            var context = new ActionContext(null, "a", "test", log) { CurrentUser = Tester };
            var set = (IRevertibleActionHandler)handlers.FindAction("example.dotnet.set_variable")!;
            var equals = handlers.FindCondition("example.dotnet.variable_equals")!;
            var conditionContext = new ConditionContext(null, Tester, new ProcessTracker(), TimeProvider.System);
            var isOn = new ComponentConfig("example.dotnet.variable_equals", new Dictionary<string, object?> { ["name"] = "AUTOSETTINGS_TEST_VAR", ["value"] = "ON" });

            var snapshot = await set.CaptureAsync(action, context, CancellationToken.None);
            Assert.False(await equals.EvaluateAsync(isOn, conditionContext, CancellationToken.None), Messages(log));

            await set.ExecuteAsync(action, context, CancellationToken.None);
            Assert.True(await equals.EvaluateAsync(isOn, conditionContext, CancellationToken.None), Messages(log));
            Assert.Contains(log.Snapshot(), e => e.Message.Contains("AUTOSETTINGS_TEST_VAR is now 'on'"));

            await set.RestoreAsync(action, snapshot, context, CancellationToken.None);
            Assert.False(await equals.EvaluateAsync(isOn, conditionContext, CancellationToken.None));

            // Errors thrown by the plugin reach the engine with their message.
            var bad = new ComponentConfig("example.dotnet.set_variable", new Dictionary<string, object?> { ["name"] = "A=B", ["value"] = "x" });
            var error = await Assert.ThrowsAsync<ActionFailedException>(() => set.ExecuteAsync(bad, context, CancellationToken.None));
            Assert.Contains("not a valid variable name", error.Message);
        }
    }

    [Fact]
    public async Task Sample_trigger_raises_events_instantly()
    {
        var (runtime, _, events, log) = Load();
        using (runtime)
        {
            var config = ConfigLoader.Load("""
                version: 1
                automations:
                  - id: hello
                    triggers:
                      - type: example.dotnet.file_created
                    actions:
                      - type: notify
                        message: "{{ event.data.name }}"
                """, ExecutionScope.User, runtime.Catalog);
            Assert.False(config.HasErrors, string.Join("\n", config.Errors));
            runtime.UseConfig(config.Config);

            // Wait until the watcher runs, then create a file.
            await WaitUntil(() => log.Snapshot().Any(e => e.Message.Contains("Watching")), TimeSpan.FromSeconds(30), () => Messages(log));
            File.WriteAllText(Path.Combine(_watched, "new.txt"), "hello");
            await WaitUntil(() => { lock (events) return events.Count > 0; }, TimeSpan.FromSeconds(15), () => Messages(log));

            SystemEvent e;
            lock (events)
                e = events[0];
            Assert.Equal("example.dotnet.file_created", e.PluginEvent);
            Assert.Equal("new.txt", e.Data!["name"]);
            Assert.Equal(1, e.SessionId);
        }
    }

    [Fact]
    public async Task A_crashing_host_restarts_and_is_turned_off_after_three_crashes()
    {
        var (runtime, handlers, _, log) = Load();
        using (runtime)
        {
            var action = new ComponentConfig("example.dotnet.set_variable", new Dictionary<string, object?> { ["name"] = "AUTOSETTINGS_TEST_VAR", ["value"] = "1" });
            var context = new ActionContext(null, "a", "test", log) { CurrentUser = Tester };
            var set = handlers.FindAction("example.dotnet.set_variable")!;

            for (var crash = 1; crash <= DotnetBackend.MaxCrashes; crash++)
            {
                await set.ExecuteAsync(action, context, CancellationToken.None);
                var hosts = Process.GetProcessesByName("AutoSettings.PluginHost");
                Assert.NotEmpty(hosts);
                foreach (var host in hosts)
                {
                    host.Kill();
                    await host.WaitForExitAsync();
                }
                var expected = crash;
                await WaitUntil(() => log.Snapshot().Count(e => e.Message.Contains("stopped unexpectedly")) >= expected, TimeSpan.FromSeconds(10), () => Messages(log));
            }

            var error = await Assert.ThrowsAsync<ActionFailedException>(() => set.ExecuteAsync(action, context, CancellationToken.None));
            Assert.Contains("turned off", error.Message);
            Assert.Contains(log.Snapshot(), e => e.Level == ActivityLevel.Error && e.Message.Contains("turned off"));
        }
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, Func<string> diagnostics)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out. Activity:\n" + diagnostics());
            await Task.Delay(200);
        }
    }
}
