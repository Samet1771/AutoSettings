using System.Diagnostics.Eventing.Reader;
using AutoSettings.Core.Events;

namespace AutoSettings.Platform.Monitoring;

/// <summary>Information about how and when Windows last started.</summary>
public static class BootInfo
{
    /// <summary>When the system last booted (UTC), based on the tick count.</summary>
    public static DateTimeOffset LastBootTime => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// Reads the most recent Kernel-Boot event 27 ("The boot type was ...") written within
    /// <paramref name="maxAge"/>. Returns <see cref="BootTypes.Cold"/>, <see cref="BootTypes.FastStartup"/>,
    /// "hibernate" (resume from hibernation, not a boot), or null when unknown.
    /// </summary>
    public static string? RecentBootType(TimeSpan maxAge)
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and (EventID=27)]]")
            {
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record?.TimeCreated is not { } created || DateTime.Now - created > maxAge)
                return null;
            var bootType = Convert.ToInt32(record.Properties[0].Value);
            return bootType switch
            {
                0 => BootTypes.Cold,
                1 => BootTypes.FastStartup,
                2 => "hibernate",
                _ => null,
            };
        }
        catch (EventLogException)
        {
            return null;
        }
    }
}
