using System.Text;
using System.Text.Json;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Ipc;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Plugins;

/// <summary>What a plugin is asked to do.</summary>
public enum PluginOperation
{
    /// <summary>Run an action.</summary>
    Apply,
    /// <summary>Read the current value before a profile changes it.</summary>
    Capture,
    /// <summary>Put back a value read by <see cref="Capture"/>.</summary>
    Restore,
    /// <summary>Check a condition.</summary>
    Evaluate,
    /// <summary>Look for events (script triggers).</summary>
    Poll,
}

/// <summary>
/// Builds the request a plugin gets: a JSON object with the operation, the parameters and what started it. Script
/// plugins read it from standard input; the same values are also in environment variables.
/// </summary>
public static class PluginRequest
{
    /// <summary>The operation name as plugins see it, for example <c>apply</c>.</summary>
    public static string Name(PluginOperation operation) => operation.ToString().ToLowerInvariant();

    /// <summary>Builds the request JSON.</summary>
    public static string Build(
        InstalledPlugin plugin,
        string component,
        PluginOperation operation,
        IReadOnlyDictionary<string, object?> parameters,
        SystemEvent? triggerEvent,
        UserInfo? user,
        string? automationName,
        string? snapshot,
        string stateDirectory,
        bool firstPoll = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", Name(operation));
            writer.WriteString("component", component);
            writer.WriteStartObject("plugin");
            writer.WriteString("id", plugin.Id);
            writer.WriteString("version", plugin.Manifest?.Version);
            writer.WriteString("directory", plugin.Directory);
            writer.WriteString("scope", plugin.Scope.ToString().ToLowerInvariant());
            writer.WriteEndObject();
            writer.WritePropertyName("parameters");
            writer.WriteRawValue(PlainJson.Serialize(parameters));
            if (snapshot is not null)
                writer.WriteString("snapshot", snapshot);
            if (triggerEvent is not null)
            {
                writer.WriteStartObject("event");
                writer.WriteString("name", EventNames.Name(triggerEvent));
                if (triggerEvent.SessionId is { } session)
                    writer.WriteNumber("session", session);
                WriteUser(writer, "user", triggerEvent.User);
                writer.WriteString("app", triggerEvent.Process?.Name);
                writer.WriteString("app_path", triggerEvent.Process?.Path);
                writer.WriteString("window_title", triggerEvent.WindowTitle);
                writer.WriteStartObject("data");
                foreach (var (key, value) in triggerEvent.Data ?? new Dictionary<string, string>())
                    writer.WriteString(key, value);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            WriteUser(writer, "user", user);
            writer.WriteString("automation", automationName);
            writer.WriteString("state_directory", stateDirectory);
            if (operation == PluginOperation.Poll)
                writer.WriteBoolean("first_poll", firstPoll);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The environment variables a script plugin gets besides the JSON on standard input: <c>AUTOSETTINGS_OPERATION</c>,
    /// <c>AUTOSETTINGS_PARAM_&lt;NAME&gt;</c> for every parameter, <c>AUTOSETTINGS_SNAPSHOT</c>, <c>AUTOSETTINGS_STATE_DIR</c>,
    /// <c>AUTOSETTINGS_PLUGIN_DIR</c> and <c>AUTOSETTINGS_FIRST_POLL</c>.
    /// </summary>
    public static Dictionary<string, string> Environment(
        InstalledPlugin plugin,
        PluginOperation operation,
        IReadOnlyDictionary<string, object?> parameters,
        string? snapshot,
        string stateDirectory,
        bool firstPoll = false)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AUTOSETTINGS_OPERATION"] = Name(operation),
            ["AUTOSETTINGS_PLUGIN_DIR"] = plugin.Directory,
            ["AUTOSETTINGS_STATE_DIR"] = stateDirectory,
        };
        foreach (var (name, value) in parameters)
            variables[VariableName("PARAM_" + name)] = ValueConverter.ToText(value) ?? "";
        if (snapshot is not null)
            variables["AUTOSETTINGS_SNAPSHOT"] = snapshot;
        if (operation == PluginOperation.Poll)
            variables["AUTOSETTINGS_FIRST_POLL"] = firstPoll ? "1" : "0";
        return variables;
    }

    /// <summary><c>AUTOSETTINGS_</c> + the name in capitals, with anything but letters and digits turned into underscores.</summary>
    public static string VariableName(string name) =>
        "AUTOSETTINGS_" + new string(name.ToUpperInvariant().Select(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? c : '_').ToArray());

    private static void WriteUser(Utf8JsonWriter writer, string property, UserInfo? user)
    {
        if (user is null)
            return;
        writer.WriteStartObject(property);
        writer.WriteString("name", user.Name);
        writer.WriteString("domain", user.Domain);
        writer.WriteString("sid", user.Sid);
        writer.WriteEndObject();
    }
}

/// <summary>An event a plugin reported.</summary>
/// <param name="Name">The event name, for example <c>acme.usb.connected</c>.</param>
/// <param name="Data">Its values.</param>
/// <param name="User">The user it is about, if the plugin said.</param>
public sealed record PluginEventReport(string Name, IReadOnlyDictionary<string, string> Data, string? User);

/// <summary>Reads what script plugins print.</summary>
public static class PluginOutput
{
    /// <summary>
    /// The result of a condition script: its last non-empty output line, <c>true</c> or <c>false</c> (any case).
    /// Returns <c>null</c> when the script printed neither.
    /// </summary>
    public static bool? ParseCondition(string output)
    {
        var last = Lines(output).LastOrDefault();
        return bool.TryParse(last, out var value) ? value : null;
    }

    /// <summary>The snapshot printed by a capture script: the whole output, trimmed; <c>null</c> when empty.</summary>
    public static string? ParseSnapshot(string output) =>
        string.IsNullOrWhiteSpace(output) ? null : output.Trim();

    /// <summary>
    /// The events printed by a poll script, one per line: either just the event name, or a JSON object such as
    /// <c>{"event":"acme.usb.connected","data":{"drive":"E:"},"user":"samet"}</c>. Events must start with the plugin id;
    /// lines that are not events are returned in <paramref name="ignored"/> with the reason.
    /// </summary>
    public static List<PluginEventReport> ParseEvents(string pluginId, string output, out List<string> ignored)
    {
        var events = new List<PluginEventReport>();
        ignored = [];
        foreach (var line in Lines(output))
        {
            string? name;
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            string? user = null;
            if (line.StartsWith('{'))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    name = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in d.EnumerateObject())
                            data[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
                    }
                    if (root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.String)
                        user = u.GetString();
                }
                catch (JsonException ex)
                {
                    ignored.Add($"'{Shorten(line)}' is not valid JSON: {ex.Message}");
                    continue;
                }
            }
            else
            {
                name = line;
            }

            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(pluginId + ".", StringComparison.OrdinalIgnoreCase) || name.Any(char.IsWhiteSpace))
            {
                ignored.Add($"'{Shorten(line)}' is not an event of {pluginId} (event names start with '{pluginId}.').");
                continue;
            }
            events.Add(new PluginEventReport(name, data, user));
        }
        return events;
    }

    /// <summary>The non-empty lines of <paramref name="output"/>, trimmed.</summary>
    public static IEnumerable<string> Lines(string output) =>
        output.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
