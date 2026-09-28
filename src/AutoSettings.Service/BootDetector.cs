using System.Globalization;
using AutoSettings.Core;
using AutoSettings.Platform.Monitoring;

namespace AutoSettings.Service;

/// <summary>
/// Decides whether the service is starting because the computer booted, or merely because the
/// service was restarted, by remembering the boot time it saw last.
/// </summary>
public sealed class BootDetector
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);
    private readonly string _stateFile;

    public BootDetector(string? stateFile = null) =>
        _stateFile = stateFile ?? Path.Combine(Product.MachineDataDirectory, "last-boot.txt");

    /// <summary>Returns true once per boot.</summary>
    public bool IsFirstStartSinceBoot()
    {
        var boot = BootInfo.LastBootTime;
        DateTimeOffset? previous = null;
        try
        {
            if (File.Exists(_stateFile) && DateTimeOffset.TryParse(File.ReadAllText(_stateFile).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var stored))
                previous = stored;
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile, boot.ToString("o", CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return previous is null || (boot - previous.Value).Duration() > Tolerance;
    }
}
