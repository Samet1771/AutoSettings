using System.IO;
using System.Text.Json;
using AutoSettings.Core;

namespace AutoSettings.Agent;

/// <summary>Per-user preferences of the agent (<c>%AppData%\AutoSettings\settings.json</c>).</summary>
public sealed class AgentSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>auto (follow Windows), en or tr.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>system, light or dark.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>The user acknowledged that the visual editor rewrites the file (comments are not kept).</summary>
    public bool RewriteNoticeAccepted { get; set; }

    /// <summary>The newest version the user was told about, so each update is announced once.</summary>
    public string? LastNotifiedUpdate { get; set; }

    /// <summary>The version that ran last time, to say "AutoSettings was updated" after an update.</summary>
    public string? LastRunVersion { get; set; }

    /// <summary>Where the settings are stored.</summary>
    public static string FilePath => Path.Combine(Product.UserDataDirectory, "settings.json");

    /// <summary>Loads the settings, or defaults when missing or unreadable.</summary>
    public static AgentSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(FilePath)) ?? new AgentSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AgentSettings();
    }

    /// <summary>Saves the settings.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
