using AutoSettings.Core;

namespace AutoSettings.Service;

/// <summary>Settings from appsettings.json, section "AutoSettings".</summary>
public sealed class ServiceOptions
{
    /// <summary>Start the agent in every user session (only when running as a Windows Service).</summary>
    public bool LaunchAgents { get; set; } = true;

    /// <summary>Path of AutoSettings.Agent.exe. Defaults to the service's folder.</summary>
    public string? AgentPath { get; set; }

    /// <summary>Loop guard limit for machine automations.</summary>
    public int MaxRunsPerMinute { get; set; } = 20;

    /// <summary>Write every event to the activity log.</summary>
    public bool LogAllEvents { get; set; }

    /// <summary>The resolved agent path.</summary>
    public string ResolvedAgentPath => string.IsNullOrWhiteSpace(AgentPath)
        ? Path.Combine(AppContext.BaseDirectory, Product.AgentExecutableName)
        : Environment.ExpandEnvironmentVariables(AgentPath);
}
