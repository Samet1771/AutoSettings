namespace AutoSettings.Core;

/// <summary>
/// Product-wide constants. To rename the product, change <see cref="Name"/> here and
/// <c>&lt;Product&gt;</c> in <c>Directory.Build.props</c>.
/// </summary>
public static class Product
{
    /// <summary>Display name of the product.</summary>
    public const string Name = "AutoSettings";

    /// <summary>Name of the Windows Service.</summary>
    public const string ServiceName = "AutoSettings";

    /// <summary>Name of the named pipe the service listens on for agents.</summary>
    public const string ServicePipeName = "AutoSettings.Service";

    /// <summary>The configuration file format version this build understands.</summary>
    public const int ConfigVersion = 1;

    /// <summary>File name of an automation configuration file.</summary>
    public const string ConfigFileName = "automations.yaml";

    /// <summary>Executable name of the per-user agent.</summary>
    public const string AgentExecutableName = "AutoSettings.Agent.exe";

    /// <summary>Machine-wide data folder (<c>%ProgramData%\AutoSettings</c>): machine automations, service state and logs.</summary>
    public static string MachineDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);

    /// <summary>Per-user roaming data folder (<c>%AppData%\AutoSettings</c>): the user's automations and profiles.</summary>
    public static string UserDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Per-user local folder (<c>%LocalAppData%\AutoSettings</c>): agent logs and caches.</summary>
    public static string UserLocalDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    /// <summary>Full path of the machine automations file.</summary>
    public static string MachineConfigPath => Path.Combine(MachineDataDirectory, ConfigFileName);

    /// <summary>Full path of the current user's automations file.</summary>
    public static string UserConfigPath => Path.Combine(UserDataDirectory, ConfigFileName);

    /// <summary>Folder for service logs.</summary>
    public static string ServiceLogDirectory => Path.Combine(MachineDataDirectory, "logs");

    /// <summary>Folder for the current user's agent logs.</summary>
    public static string AgentLogDirectory => Path.Combine(UserLocalDirectory, "logs");

    /// <summary>Link to the documentation site.</summary>
    public const string DocumentationUrl = "https://github.com/Samet1771/AutoSettings/tree/main/docs";
}
