using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Monitoring;

namespace AutoSettings.Platform.Actions;

/// <summary>Shows notifications to the signed-in user. Implemented by the agent's tray icon.</summary>
public interface INotifier
{
    /// <summary>Shows a notification.</summary>
    void Notify(string title, string message);
}

/// <summary><c>notify</c>: shows a notification.</summary>
public sealed class NotifyAction(INotifier notifier) : SyncActionHandler
{
    /// <inheritdoc />
    public override string Type => "notify";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        notifier.Notify(action.GetString("title") ?? "AutoSettings", action.GetString("message") ?? "");
}

/// <summary><c>open</c>: opens a URL, file, folder or ms-settings: page.</summary>
public sealed class OpenAction : SyncActionHandler
{
    /// <inheritdoc />
    public override string Type => "open";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var target = Environment.ExpandEnvironmentVariables(action.GetString("target") ?? "");
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw new ActionFailedException($"cannot open '{target}': {ex.Message}", ex);
        }
    }
}

/// <summary><c>app.launch</c>: starts a program or opens a file.</summary>
public sealed class AppLaunchAction : SyncActionHandler
{
    /// <inheritdoc />
    public override string Type => "app.launch";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var path = Environment.ExpandEnvironmentVariables(action.GetString("path") ?? "");
        if (action.GetBoolean("skip_if_running") != false && IsRunning(path))
        {
            context.Log.Info(ActivitySources.Action, $"{Path.GetFileName(path)} is already running.", context.AutomationId);
            return;
        }

        var workingDirectory = action.GetString("working_directory") is { Length: > 0 } wd
            ? Environment.ExpandEnvironmentVariables(wd)
            : File.Exists(path) ? Path.GetDirectoryName(path) ?? "" : "";

        var startInfo = new ProcessStartInfo(path)
        {
            Arguments = action.GetString("arguments") ?? "",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = action.GetString("window") switch
            {
                "minimized" => ProcessWindowStyle.Minimized,
                "maximized" => ProcessWindowStyle.Maximized,
                "hidden" => ProcessWindowStyle.Hidden,
                _ => ProcessWindowStyle.Normal,
            },
        };
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw new ActionFailedException($"cannot start '{path}': {ex.Message}", ex);
        }
    }

    private static bool IsRunning(string path)
    {
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        var session = Sessions.CurrentSessionId();
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        try
        {
            return processes.Any(p => p.SessionId == session);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}

/// <summary><c>app.close</c>: asks apps to close, optionally ending them.</summary>
public sealed class AppCloseAction : IActionHandler
{
    /// <inheritdoc />
    public string Type => "app.close";

    /// <inheritdoc />
    public async Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var patterns = action.GetStringList("app");
        var force = action.GetBoolean("force") == true;
        var timeout = action.GetDuration("timeout") ?? TimeSpan.FromSeconds(10);
        var targets = FindProcesses(patterns);
        try
        {
            if (targets.Count == 0)
            {
                context.Log.Info(ActivitySources.Action, $"{string.Join(", ", patterns)} is not running.", context.AutomationId);
                return;
            }

            foreach (var process in targets)
            {
                try
                {
                    process.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await Task.WhenAll(targets.Select(p => p.WaitForExitAsync(timeoutSource.Token))).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out; handled below.
            }

            var remaining = targets.Where(p => !HasExited(p)).ToList();
            if (remaining.Count == 0)
                return;
            if (!force)
            {
                throw new ActionFailedException(
                    $"{string.Join(", ", remaining.Select(p => p.ProcessName).Distinct())} did not close within {ValueConverter.FormatDuration(timeout)}. Set force: true to end it.");
            }
            foreach (var process in remaining)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Exited meanwhile or access denied; nothing more we can do.
                }
            }
        }
        finally
        {
            foreach (var process in targets)
                process.Dispose();
        }
    }

    private static List<Process> FindProcesses(IReadOnlyList<string> patterns)
    {
        var session = Sessions.CurrentSessionId();
        var needsPath = patterns.Any(p => p.Contains('\\') || p.Contains('/'));
        var result = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            var keep = false;
            try
            {
                if (process.SessionId == session && process.Id != Environment.ProcessId)
                {
                    var info = new ProcessInfo(process.Id, process.ProcessName + ".exe", needsPath ? ProcessQuery.TryGetPath(process.Id) : null);
                    keep = AppPattern.MatchesAny(patterns, info);
                }
            }
            catch (InvalidOperationException)
            {
            }
            if (keep)
                result.Add(process);
            else
                process.Dispose();
        }
        return result;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }
}

/// <summary><c>command.run</c>: runs PowerShell, cmd or a program.</summary>
public sealed class CommandRunAction : IActionHandler
{
    private const int MaxOutput = 500;

    /// <inheritdoc />
    public string Type => "command.run";

    /// <inheritdoc />
    public async Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var command = action.GetString("command") ?? "";
        var wait = action.GetBoolean("wait") == true;
        var hidden = action.GetBoolean("hidden") != false;
        var timeout = action.GetDuration("timeout") ?? TimeSpan.FromMinutes(1);

        var startInfo = BuildStartInfo(action.GetString("shell") ?? "powershell", command);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = hidden;
        startInfo.WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal;
        startInfo.RedirectStandardOutput = wait;
        startInfo.RedirectStandardError = wait;

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ActionFailedException("the command could not be started");
        }
        catch (Win32Exception ex)
        {
            throw new ActionFailedException($"cannot start {startInfo.FileName}: {ex.Message}", ex);
        }

        using (process)
        {
            if (!wait)
                return;

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new ActionFailedException($"the command did not finish within {ValueConverter.FormatDuration(timeout)} and was stopped");
            }

            var stdout = await output.ConfigureAwait(false);
            var stderr = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new ActionFailedException($"the command exited with code {process.ExitCode}{(string.IsNullOrWhiteSpace(stderr) ? "" : ": " + Tail(stderr))}");
            if (!string.IsNullOrWhiteSpace(stdout))
                context.Log.Info(ActivitySources.Action, $"Command output: {Tail(stdout)}", context.AutomationId);
        }
    }

    /// <summary>Builds the process start info for a shell and command.</summary>
    public static ProcessStartInfo BuildStartInfo(string shell, string command)
    {
        switch (shell)
        {
            case "powershell":
            case "pwsh":
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                return new ProcessStartInfo(shell == "pwsh" ? "pwsh.exe" : "powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}");

            case "cmd":
                var lines = command.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new ProcessStartInfo("cmd.exe", "/d /c " + string.Join(" & ", lines));

            default:
                var text = command.Trim();
                string file;
                string arguments;
                if (text.StartsWith('"'))
                {
                    var end = text.IndexOf('"', 1);
                    file = end > 0 ? text[1..end] : text.Trim('"');
                    arguments = end > 0 ? text[(end + 1)..].Trim() : "";
                }
                else
                {
                    var space = text.IndexOf(' ');
                    file = space > 0 ? text[..space] : text;
                    arguments = space > 0 ? text[(space + 1)..].Trim() : "";
                }
                return new ProcessStartInfo(Environment.ExpandEnvironmentVariables(file), arguments);
        }
    }

    private static string Tail(string text)
    {
        text = text.Trim();
        return text.Length <= MaxOutput ? text : "…" + text[^MaxOutput..];
    }
}
