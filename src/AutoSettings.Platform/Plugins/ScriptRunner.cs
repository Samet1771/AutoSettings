using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;

namespace AutoSettings.Platform.Plugins;

/// <summary>What a script printed and how it ended.</summary>
/// <param name="ExitCode">The exit code (0 = success).</param>
/// <param name="Output">Standard output.</param>
/// <param name="Error">Standard error.</param>
public sealed record ScriptResult(int ExitCode, string Output, string Error)
{
    /// <summary>A short message for the activity log when the script failed: the end of standard error, or of the output.</summary>
    public string FailureMessage
    {
        get
        {
            var text = (string.IsNullOrWhiteSpace(Error) ? Output : Error).Trim();
            var detail = text.Length <= 400 ? text : "…" + text[^400..];
            return $"the script exited with code {ExitCode}{(detail.Length > 0 ? ": " + detail : "")}";
        }
    }
}

/// <summary>Thrown when a plugin script cannot be started or takes too long.</summary>
public sealed class ScriptException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Runs a script plugin's PowerShell file with Windows PowerShell (<c>-NoProfile -NonInteractive -ExecutionPolicy Bypass</c>),
/// in the plugin folder, with the request as UTF-8 JSON on standard input and the same values in environment variables.
/// </summary>
public static class ScriptRunner
{
    /// <summary>How long a script may run when the manifest does not say.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The PowerShell to use. Windows PowerShell 5.1 is on every supported Windows.</summary>
    public static string PowerShellPath { get; set; } =
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Runs <paramref name="script"/> (relative to <paramref name="pluginDirectory"/>).</summary>
    /// <exception cref="ScriptException">The script is outside the plugin folder, cannot start, or timed out.</exception>
    public static async Task<ScriptResult> RunAsync(
        string pluginDirectory,
        string script,
        string requestJson,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(pluginDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, script));
        if (!PluginManifestValidator.IsSafeRelativePath(script) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ScriptException($"{script} is outside the plugin folder");
        if (!File.Exists(path))
            throw new ScriptException($"{script} is missing from the plugin folder");

        var startInfo = new ProcessStartInfo(PowerShellPath)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Bootstrap(path) })
            startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ScriptException("PowerShell could not be started");
        }
        catch (Win32Exception ex)
        {
            throw new ScriptException($"cannot start PowerShell: {ex.Message}", ex);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.StandardInput.WriteAsync(requestJson).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The script ended without reading its input; that is fine.
            }

            var limit = timeout ?? DefaultTimeout;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(limit);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                if (cancellationToken.IsCancellationRequested)
                    throw;
                throw new ScriptException($"{script} did not finish within {ValueConverter.FormatDuration(limit)} and was stopped");
            }
            return new ScriptResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Windows PowerShell reads and writes the console code page by default, which garbles non-English text. The
    /// bootstrap switches standard input and output to UTF-8, runs the script and passes its exit code on.
    /// </summary>
    private static string Bootstrap(string scriptPath)
    {
        var command =
            "[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); " +
            "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
            "$OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
            $"& '{scriptPath.Replace("'", "''")}'; " +
            "if ($LASTEXITCODE) { exit $LASTEXITCODE }";
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
