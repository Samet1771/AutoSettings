using AutoSettings.Sdk;

namespace Example.Hello;

/// <summary>
/// A trigger source that reacts instantly (no polling): it watches the "AutoSettings Hello" folder in the user's
/// folder and raises one event when a file appears and another when it disappears.
/// </summary>
[PluginComponent("example.dotnet.file_created",
    Title = "File created (instant)",
    Description = "Fires as soon as a file is created in the \"AutoSettings Hello\" folder in your user folder.",
    OppositeEvent = "example.dotnet.file_deleted")]
[PluginComponent("example.dotnet.file_deleted",
    Title = "File deleted (instant)",
    Description = "Fires as soon as a file is deleted from the \"AutoSettings Hello\" folder.")]
[Localized("tr", "Dosya oluşturuldu (anında)", Type = "example.dotnet.file_created")]
[Localized("tr", "Dosya silindi (anında)", Type = "example.dotnet.file_deleted")]
[Field("name", FieldKind.String, Description = "Only files with this name; wildcards work (*.txt). Leave empty for any file.", Example = "*.txt")]
public sealed class FolderWatcher : IPluginTrigger
{
    public async Task RunAsync(ITriggerSink sink, CancellationToken cancellationToken)
    {
        var folder = Environment.GetEnvironmentVariable("AUTOSETTINGS_HELLO_FOLDER")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AutoSettings Hello");
        Directory.CreateDirectory(folder);

        using var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) => sink.Raise("example.dotnet.file_created", new Dictionary<string, string> { ["name"] = e.Name ?? "" });
        watcher.Deleted += (_, e) => sink.Raise("example.dotnet.file_deleted", new Dictionary<string, string> { ["name"] = e.Name ?? "" });
        watcher.EnableRaisingEvents = true;
        sink.Log.Info($"Watching {folder}.");

        // Keep running until AutoSettings stops the trigger.
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
