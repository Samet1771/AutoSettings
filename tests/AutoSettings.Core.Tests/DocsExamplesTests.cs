using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;

namespace AutoSettings.Core.Tests;

/// <summary>Every example YAML file in docs/examples must load without errors or warnings.</summary>
public class DocsExamplesTests
{
    public static TheoryData<string> ExampleFiles()
    {
        var data = new TheoryData<string>();
        var root = TestSupport.FindRepositoryRoot();
        var folder = root is null ? null : Path.Combine(root, "docs", "examples");
        if (folder is not null && Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.yaml").Order())
                data.Add(Path.GetFileName(file));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_is_valid(string fileName)
    {
        var path = Path.Combine(TestSupport.FindRepositoryRoot()!, "docs", "examples", fileName);
        var scope = fileName.Contains(".machine.", StringComparison.OrdinalIgnoreCase) ? ExecutionScope.Machine : ExecutionScope.User;

        var result = ConfigLoader.Load(File.ReadAllText(path), scope);

        Assert.True(result.Issues.Count == 0, $"{fileName}:\n{string.Join("\n", result.Issues)}");
    }

    [Fact]
    public void Starter_files_are_valid()
    {
        Assert.Empty(ConfigLoader.Load(StarterConfig.User, ExecutionScope.User).Issues);
        Assert.Empty(ConfigLoader.Load(StarterConfig.Machine, ExecutionScope.Machine).Issues);
    }
}
