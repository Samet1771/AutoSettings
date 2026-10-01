using AutoSettings.DocGen;

var check = args.Contains("--check");
var outIndex = Array.IndexOf(args, "--out");
var docs = outIndex >= 0 && outIndex + 1 < args.Length ? Path.GetFullPath(args[outIndex + 1]) : FindDocsFolder();
if (docs is null)
{
    Console.Error.WriteLine("Cannot find the docs folder. Run from inside the repository or pass --out <docs folder>.");
    return 2;
}

// Paths relative to docs/: the component reference and the plugin SDK API reference.
var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var (relative, content) in ReferenceGenerator.Generate())
    files["reference/" + relative] = content;
foreach (var (relative, content) in ApiReferenceGenerator.Generate())
    files["plugins/api/" + relative] = content;
var referenceRoot = docs;

if (check)
{
    var stale = new List<string>();
    foreach (var (relative, content) in files)
    {
        var path = Path.Combine(referenceRoot, relative);
        if (!File.Exists(path) || Normalize(File.ReadAllText(path)) != content)
            stale.Add(relative);
    }
    foreach (var existing in GeneratedFilesOnDisk(referenceRoot))
    {
        if (!files.ContainsKey(existing))
            stale.Add(existing + " (no longer generated)");
    }
    if (stale.Count == 0)
    {
        Console.WriteLine($"Generated docs are up to date ({files.Count} files).");
        return 0;
    }
    Console.Error.WriteLine("Generated docs are out of date. Run: dotnet run --project src/AutoSettings.DocGen");
    foreach (var file in stale)
        Console.Error.WriteLine("  " + file);
    return 1;
}

foreach (var existing in GeneratedFilesOnDisk(referenceRoot).Where(f => !files.ContainsKey(f)))
    File.Delete(Path.Combine(referenceRoot, existing));
foreach (var (relative, content) in files)
{
    var path = Path.Combine(referenceRoot, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}
Console.WriteLine($"Wrote {files.Count} files to {referenceRoot}.");
return 0;

static string Normalize(string text) => text.Replace("\r\n", "\n");

static IEnumerable<string> GeneratedFilesOnDisk(string referenceRoot)
{
    foreach (var folder in new[] { "reference/triggers", "reference/conditions", "reference/actions", "plugins/api" })
    {
        var directory = Path.Combine(referenceRoot, folder);
        if (!Directory.Exists(directory))
            continue;
        foreach (var file in Directory.GetFiles(directory, "*.md"))
            yield return Path.Combine(folder, Path.GetFileName(file)).Replace('\\', '/');
    }
}

static string? FindDocsFolder()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoSettings.slnx")))
        directory = directory.Parent;
    return directory is null ? null : Path.Combine(directory.FullName, "docs");
}
