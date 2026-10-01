using System.IO.Compression;
using System.Security.Cryptography;
using AutoSettings.Core.Config;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Plugins;

/// <summary>
/// A plugin package (<c>.aspkg</c>): a zip file with <c>plugin.yaml</c> at its root and the plugin's files next to it.
/// Reading a package never trusts it: paths that leave the folder, very large packages and invalid manifests are
/// refused before anything is extracted.
/// </summary>
public static class PluginPackage
{
    /// <summary>The file extension of plugin packages.</summary>
    public const string Extension = ".aspkg";

    /// <summary>The largest package AutoSettings unpacks (all files together, uncompressed).</summary>
    public const long MaxUncompressedBytes = 200L * 1024 * 1024;

    /// <summary>The most files a package may have.</summary>
    public const int MaxEntries = 5000;

    /// <summary>Files that must never be in a package because AutoSettings provides them.</summary>
    public static readonly IReadOnlyList<string> ProvidedFiles = ["AutoSettings.Sdk.dll"];

    /// <summary>The package file name for a manifest, for example <c>acme.usb-1.2.0.aspkg</c>.</summary>
    public static string FileName(PluginManifest manifest) => $"{manifest.Id}-{manifest.Version}{Extension}";

    /// <summary>
    /// Zips <paramref name="folder"/> into <paramref name="packagePath"/>, with <paramref name="manifestYaml"/> as its
    /// <c>plugin.yaml</c> (replacing any <c>plugin.yaml</c> in the folder). Debug symbols and files AutoSettings provides
    /// are left out. Returns the SHA-256 of the package.
    /// </summary>
    public static string Create(string folder, string manifestYaml, string packagePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(packagePath))!);
        var temporary = packagePath + ".tmp";
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(PluginStore.ManifestFileName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(manifestYaml);

            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                var name = Path.GetFileName(file);
                if (relative.Equals(PluginStore.ManifestFileName, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                    || ProvidedFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        }
        File.Move(temporary, packagePath, overwrite: true);
        return Sha256(packagePath);
    }

    /// <summary>The SHA-256 of a file as lower-case hex.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Reads and checks a package without extracting it: the file list (no paths that leave the folder, size and count
    /// limits) and the manifest (<see cref="PluginManifestReader.Load"/>).
    /// </summary>
    public static (PluginManifest? Manifest, List<ConfigIssue> Issues) Inspect(string packagePath, SemVersion? appVersion = null)
    {
        var issues = new List<ConfigIssue>();
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            if (!CheckEntries(archive, issues))
                return (null, issues);
            var entry = archive.GetEntry(PluginStore.ManifestFileName);
            if (entry is null)
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The package has no {PluginStore.ManifestFileName} at its root."));
                return (null, issues);
            }
            string yaml;
            using (var reader = new StreamReader(entry.Open()))
                yaml = reader.ReadToEnd();
            var (manifest, manifestIssues) = PluginManifestReader.Load(yaml, appVersion);
            issues.AddRange(manifestIssues);
            if (manifest.Kind == PluginKind.Dotnet && manifest.Entry is { } dll && archive.GetEntry(dll.Replace('\\', '/')) is null)
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{dll} is missing from the package."));
            foreach (var script in manifest.Components.SelectMany(c => c.Scripts.All).Distinct())
            {
                if (archive.GetEntry(script.Replace('\\', '/')) is null)
                    issues.Add(new ConfigIssue(IssueSeverity.Error, $"{script} is missing from the package."));
            }
            return (manifest, issues);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"Not a valid plugin package: {ex.Message}"));
            return (null, issues);
        }
    }

    /// <summary>
    /// Extracts a package that <see cref="Inspect"/> accepted into <paramref name="targetFolder"/>, which must not exist
    /// yet. Every path is checked again while extracting.
    /// </summary>
    public static void Extract(string packagePath, string targetFolder)
    {
        var root = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(packagePath);
        var issues = new List<ConfigIssue>();
        if (!CheckEntries(archive, issues))
            throw new InvalidDataException(issues[0].Message);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{entry.FullName} leaves the plugin folder.");
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static bool CheckEntries(ZipArchive archive, List<ConfigIssue> issues)
    {
        if (archive.Entries.Count > MaxEntries)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"The package has more than {MaxEntries} files."));
            return false;
        }
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var path = name.TrimEnd('/');
            if (path.Length > 0 && !PluginManifestValidator.IsSafeRelativePath(path))
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The package contains '{name}', which would be written outside the plugin folder."));
                return false;
            }
            if (ProvidedFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The package contains {Path.GetFileName(path)}, which {Product.Name} provides; remove it (Private=\"false\" on the SDK reference)."));
                return false;
            }
            total += entry.Length;
            if (total > MaxUncompressedBytes)
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The package is larger than {MaxUncompressedBytes / 1024 / 1024} MB when unpacked."));
                return false;
            }
        }
        return true;
    }
}
