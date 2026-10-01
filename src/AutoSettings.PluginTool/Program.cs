using AutoSettings.Core.Config;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;

// autosettings-plugin: tools for AutoSettings plugin authors.
const string Usage = """
    autosettings-plugin pack <folder> [--out <folder>]
        Checks the plugin in <folder> (the build output for .NET plugins) and writes <id>-<version>.aspkg.
        For .NET plugins, the components in plugin.yaml are generated from the [PluginComponent] attributes.
    autosettings-plugin validate <folder or .aspkg>
        Checks plugin.yaml and the files without packing.
    autosettings-plugin describe <plugin.dll>
        Prints the components a .NET plugin declares, as plugin.yaml would list them.
    """;

if (args.Length < 2)
{
    Console.WriteLine(Usage);
    return args.Length == 0 || args[0] is "-h" or "--help" or "help" ? 0 : 2;
}

try
{
    return args[0] switch
    {
        "pack" => Pack(args[1], Option("--out") ?? Path.Combine(args[1], "..", "dist")),
        "validate" => Validate(args[1]),
        "describe" => Describe(args[1]),
        _ => Fail($"Unknown command '{args[0]}'.\n\n{Usage}"),
    };
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException)
{
    return Fail(ex.Message);
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static (PluginManifest? Manifest, List<ConfigIssue> Issues) Prepare(string folder)
{
    var manifestPath = Path.Combine(folder, PluginStore.ManifestFileName);
    if (!File.Exists(manifestPath))
        return (null, [new ConfigIssue(IssueSeverity.Error, $"{manifestPath} not found.")]);
    var (manifest, issues) = PluginManifestReader.Read(File.ReadAllText(manifestPath));
    if (issues.Any(i => i.Severity == IssueSeverity.Error))
        return (manifest, issues);

    if (manifest.Kind == PluginKind.Dotnet && manifest.Entry is { } entry)
    {
        var dll = Path.Combine(folder, entry);
        if (!File.Exists(dll))
            return (manifest, [new ConfigIssue(IssueSeverity.Error, $"{entry} not found in {folder}. Build the plugin first and pack the build output.")]);
        // The code is the source of truth for .NET plugins: generate the components from its attributes.
        manifest.Components = SdkDescriber.Describe(PluginLoadContext.LoadPlugin(dll));
        if (File.Exists(Path.Combine(folder, "AutoSettings.Sdk.dll")))
            issues.Add(new ConfigIssue(IssueSeverity.Warning, "AutoSettings.Sdk.dll is in the output; it is left out of the package. Add Private=\"false\" (or ExcludeAssets=\"runtime\") to the SDK reference."));
    }
    issues.AddRange(PluginManifestValidator.Validate(manifest));
    foreach (var script in manifest.Components.SelectMany(c => c.Scripts.All).Distinct())
    {
        if (PluginManifestValidator.IsSafeRelativePath(script) && !File.Exists(Path.Combine(folder, script)))
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"{script} not found in {folder}."));
    }
    return (manifest, issues);
}

static bool Report(List<ConfigIssue> issues)
{
    foreach (var issue in issues)
        (issue.Severity == IssueSeverity.Error ? Console.Error : Console.Out).WriteLine(issue);
    return !issues.Any(i => i.Severity == IssueSeverity.Error);
}

static int Pack(string folder, string output)
{
    var (manifest, issues) = Prepare(folder);
    if (!Report(issues) || manifest is null)
        return 1;
    var package = Path.GetFullPath(Path.Combine(output, PluginPackage.FileName(manifest)));
    var sha = PluginPackage.Create(folder, PluginManifestWriter.Write(manifest), package);

    // Keep SHA256SUMS.txt next to the packages, ready to attach to a GitHub release.
    var sums = Path.Combine(output, "SHA256SUMS.txt");
    var lines = File.Exists(sums) ? File.ReadAllLines(sums).Where(l => !l.EndsWith(" " + Path.GetFileName(package), StringComparison.Ordinal)).ToList() : [];
    lines.Add(Checksums.FormatLine(sha, Path.GetFileName(package)));
    File.WriteAllLines(sums, lines);

    Console.WriteLine($"{package}");
    Console.WriteLine($"SHA-256 {sha}");
    Console.WriteLine($"{manifest.Components.Count} component(s): {string.Join(", ", manifest.Components.Select(c => c.Type))}");
    return 0;
}

static int Validate(string path)
{
    if (File.Exists(path) && path.EndsWith(PluginPackage.Extension, StringComparison.OrdinalIgnoreCase))
    {
        var (manifest, packageIssues) = PluginPackage.Inspect(path);
        var ok = Report(packageIssues);
        Console.WriteLine(ok ? $"OK: {manifest!.Id} {manifest.Version}" : "The package is not valid.");
        return ok ? 0 : 1;
    }
    var (prepared, issues) = Prepare(path);
    var valid = Report(issues) && prepared is not null;
    Console.WriteLine(valid ? $"OK: {prepared!.Id} {prepared.Version}, {prepared.Components.Count} component(s)" : "The plugin is not valid.");
    return valid ? 0 : 1;
}

static int Describe(string dll)
{
    var manifest = new PluginManifest
    {
        Id = "describe", Version = "0.0.0", Sdk = "1.0", Kind = PluginKind.Dotnet,
        Components = SdkDescriber.Describe(PluginLoadContext.LoadPlugin(dll)),
    };
    var yaml = PluginManifestWriter.Write(manifest);
    Console.WriteLine(yaml[yaml.IndexOf("components:", StringComparison.Ordinal)..]);
    return 0;
}
