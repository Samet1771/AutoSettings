using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Plugins;
using AutoSettings.Platform.Plugins;

namespace AutoSettings.Agent.Plugins;

/// <summary>The result of a plugin command: an exit code (0 = success) and a message for the user.</summary>
/// <param name="ExitCode">0 when it worked.</param>
/// <param name="Message">What happened, in plain language.</param>
public sealed record PluginCommandResult(int ExitCode, string Message)
{
    /// <summary>Whether it worked.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Plugin commands, used by the Plugins page and from the command line (for scripts and company deployment):
/// <code>
/// AutoSettings.Agent.exe --plugin list
/// AutoSettings.Agent.exe --plugin install &lt;file.aspkg | owner/repo | https://github.com/owner/repo&gt; [--scope machine] [--prerelease]
/// AutoSettings.Agent.exe --plugin uninstall|enable|disable|update|rollback &lt;id&gt; [--scope machine]
/// AutoSettings.Agent.exe --plugin update --all [--scope machine]
/// </code>
/// Machine plugins need administrator rights: without them, the command runs itself again elevated (Windows asks
/// for permission) and reports what the elevated copy did.
/// </summary>
public static class PluginCommands
{
    private const int Failed = 1;
    private const int Cancelled = 3;

    /// <summary>Whether the command line is a plugin command.</summary>
    public static bool IsCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == "--plugin";

    /// <summary>Runs a plugin command from the command line and prints the result to the console it was started from.</summary>
    public static async Task<int> RunFromCommandLineAsync(IReadOnlyList<string> args)
    {
        AttachConsole(-1);
        var result = await RunAsync(args.Skip(1).ToList()).ConfigureAwait(false);
        (result.Succeeded ? Console.Out : Console.Error).WriteLine(result.Message);
        if (Option(args, "--result") is { } resultFile)
        {
            try
            {
                File.WriteAllText(resultFile, $"{result.ExitCode}\n{result.Message}");
            }
            catch (IOException)
            {
            }
        }
        return result.ExitCode;
    }

    /// <summary>
    /// Runs a command such as <c>install E:\x.aspkg --scope machine</c>. Machine commands without administrator rights
    /// run elevated in a new process.
    /// </summary>
    public static async Task<PluginCommandResult> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
            return new(Failed, Usage);
        var verb = args[0].ToLowerInvariant();
        var scope = string.Equals(Option(args, "--scope"), "machine", StringComparison.OrdinalIgnoreCase) ? ExecutionScope.Machine : ExecutionScope.User;
        var target = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !IsOptionValue(args, a));
        var prerelease = args.Contains("--prerelease");

        if (scope == ExecutionScope.Machine && verb != "list" && !PluginManager.IsElevated)
            return await RunElevatedAsync(args, cancellationToken).ConfigureAwait(false);

        using var manager = new PluginManager();
        try
        {
            switch (verb)
            {
                case "list":
                    var lines = PluginManager.List(ExecutionScope.Machine).Concat(PluginManager.List(ExecutionScope.User))
                        .Select(p => $"{p.Id} {p.Manifest?.Version ?? "?"} ({p.Scope.ToString().ToLowerInvariant()}{(p.Enabled ? "" : ", off")}{(p.IsValid ? "" : ", has errors")})")
                        .ToList();
                    return new(0, lines.Count == 0 ? "No plugins are installed." : string.Join(Environment.NewLine, lines));

                case "install" when target is not null:
                {
                    PluginManifest manifest;
                    if (File.Exists(target))
                        manifest = manager.InstallFile(target, scope);
                    else if (PluginReleases.TryParseRepository(target, out var repository))
                        manifest = await manager.InstallFromGitHubAsync(repository, scope, prerelease, cancellationToken: cancellationToken).ConfigureAwait(false);
                    else
                        return new(Failed, $"'{target}' is neither a .aspkg file nor a GitHub repository (owner/repo).");
                    return new(0, $"Installed {manifest.Name} {manifest.Version}.");
                }

                case "uninstall" when target is not null:
                    return PluginInstaller.Uninstall(PluginManager.RootFor(scope), target)
                        ? new(0, $"Removed {target}.")
                        : new(Failed, $"{target} is not installed{(scope == ExecutionScope.User ? " for you (add --scope machine for plugins installed for all users)" : "")}.");

                case "enable" when target is not null:
                case "disable" when target is not null:
                    PluginInstaller.SetEnabled(PluginManager.RootFor(scope), target, verb == "enable");
                    return new(0, $"{target} is now {(verb == "enable" ? "on" : "off")}.");

                case "rollback" when target is not null:
                    return new(0, $"{target} is back to version {PluginInstaller.Rollback(PluginManager.RootFor(scope), target)}.");

                case "update" when target is not null || args.Contains("--all"):
                {
                    var installed = PluginManager.List(scope).Where(p => p.IsValid && (target is null || string.Equals(p.Id, target, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (target is not null && installed.Count == 0)
                        return new(Failed, $"{target} is not installed.");
                    var updates = await manager.CheckForUpdatesAsync(installed, prerelease, cancellationToken).ConfigureAwait(false);
                    if (updates.Count == 0)
                        return new(0, target is null ? "All plugins are up to date." : $"{target} is up to date.");
                    var done = new List<string>();
                    foreach (var update in updates)
                    {
                        var manifest = await manager.UpdateAsync(update.Plugin, prerelease, cancellationToken).ConfigureAwait(false);
                        done.Add($"{manifest.Name} {manifest.Version}");
                    }
                    return new(0, $"Updated {string.Join(", ", done)}.");
                }

                default:
                    return new(Failed, Usage);
            }
        }
        catch (PluginInstallException ex)
        {
            return new(Failed, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            return new(Failed, ex is UnauthorizedAccessException && scope == ExecutionScope.Machine
                ? "Administrator rights are needed for plugins installed for all users."
                : ex.Message);
        }
    }

    /// <summary>The usage text.</summary>
    public const string Usage = """
        AutoSettings.Agent.exe --plugin list
        AutoSettings.Agent.exe --plugin install <file.aspkg | owner/repo | https://github.com/owner/repo> [--scope machine] [--prerelease]
        AutoSettings.Agent.exe --plugin uninstall|enable|disable|rollback <id> [--scope machine]
        AutoSettings.Agent.exe --plugin update <id> | --all [--scope machine] [--prerelease]
        """;

    private static async Task<PluginCommandResult> RunElevatedAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var resultFile = Path.Combine(Path.GetTempPath(), $"autosettings-plugin-{Guid.NewGuid():N}.txt");
        // "runas" needs the shell, which takes the arguments as one string.
        var arguments = new[] { "--plugin" }.Concat(args).Concat(["--result", resultFile]);
        var startInfo = new ProcessStartInfo(Environment.ProcessPath ?? Product.AgentExecutableName, string.Join(' ', arguments.Select(Quote)))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return new(Failed, "Could not ask for administrator rights.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(resultFile))
            {
                var text = await File.ReadAllTextAsync(resultFile, cancellationToken).ConfigureAwait(false);
                var newline = text.IndexOf('\n');
                return new(int.TryParse(newline > 0 ? text[..newline] : text, out var code) ? code : process.ExitCode,
                    newline > 0 ? text[(newline + 1)..] : "");
            }
            return new(process.ExitCode, process.ExitCode == 0 ? "Done." : "The elevated command failed.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(Cancelled, "Administrator permission was not given; nothing was changed.");
        }
        finally
        {
            try
            {
                File.Delete(resultFile);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Quotes an argument the way the Windows command line parser reads it back.</summary>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return argument;
        var sb = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }

    private static bool IsOptionValue(IReadOnlyList<string> args, string value)
    {
        for (var i = 1; i < args.Count; i++)
        {
            if (ReferenceEquals(args[i], value) && args[i - 1] is "--scope" or "--result")
                return true;
        }
        return false;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
