using System.Xml.Linq;
using AutoSettings.Core.Catalog;

namespace AutoSettings.Core.Tests;

/// <summary>The agent's English and Turkish resources must stay in sync.</summary>
public class LocalizationTests
{
    private static HashSet<string> Keys(string fileName)
    {
        var path = Path.Combine(TestSupport.FindRepositoryRoot()!, "src", "AutoSettings.Agent", "Resources", fileName);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    [Fact]
    public void Turkish_ui_strings_match_english()
    {
        var english = Keys("Strings.resx");
        var turkish = Keys("Strings.tr.resx");
        Assert.Empty(english.Except(turkish));
        Assert.Empty(turkish.Except(english));
    }

    [Fact]
    public void Every_component_and_category_has_a_turkish_title()
    {
        var turkish = Keys("Catalog.tr.resx");
        var missing = ComponentCatalog.Default.All
            .Select(d => $"{d.Kind}.{d.Type}")
            .Concat(ComponentCatalog.Default.All.Select(d => "Category." + d.Category.Replace(' ', '_').Replace("&", "and")))
            .Distinct()
            .Where(k => !turkish.Contains(k))
            .ToList();
        Assert.True(missing.Count == 0, "Missing in Catalog.tr.resx (edit tools/strings.py): " + string.Join(", ", missing));
    }
}
