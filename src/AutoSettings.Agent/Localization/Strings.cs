using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using AutoSettings.Core.Catalog;

namespace AutoSettings.Agent.Localization;

/// <summary>Translated UI text (Resources/Strings*.resx). English is the fallback.</summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("AutoSettings.Agent.Resources.Strings", typeof(Strings).Assembly);
    private static readonly ResourceManager CatalogManager = new("AutoSettings.Agent.Resources.Catalog", typeof(Strings).Assembly);

    /// <summary>The UI culture in use.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Supported languages (code, native name).</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } = [("auto", "Windows"), ("en", "English"), ("tr", "Türkçe")];

    /// <summary>Chooses the UI language: "auto" follows Windows.</summary>
    public static void Use(string language)
    {
        Culture = language switch
        {
            "en" => CultureInfo.GetCultureInfo("en"),
            "tr" => CultureInfo.GetCultureInfo("tr"),
            _ => CultureInfo.CurrentUICulture,
        };
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
    }

    /// <summary>The text for <paramref name="key"/>, or the key itself when missing.</summary>
    public static string Get(string key) => Manager.GetString(key, Culture) ?? key;

    /// <summary>Formatted text, e.g. <c>Format("AutomationsOn", 3, 5)</c>.</summary>
    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>Translated title of a trigger, condition or action (English catalog title as fallback).</summary>
    public static string Title(ComponentDescriptor descriptor) =>
        CatalogManager.GetString($"{descriptor.Kind}.{descriptor.Type}", Culture)
        ?? Localized(descriptor)?.Title
        ?? descriptor.Title;

    /// <summary>A plugin component's text in the UI language, if the plugin has it.</summary>
    private static LocalizedText? Localized(ComponentDescriptor descriptor) =>
        descriptor.Localized.GetValueOrDefault(Culture.TwoLetterISOLanguageName)
        ?? descriptor.Localized.GetValueOrDefault(Culture.Name);

    /// <summary>Translated category name.</summary>
    public static string Category(string category) =>
        CatalogManager.GetString("Category." + category.Replace(' ', '_').Replace("&", "and"), Culture) ?? category;
}

/// <summary>XAML: <c>Text="{loc:T Automations}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    /// <summary>Creates the extension.</summary>
    public TExtension(string key) => Key = key;

    /// <summary>Resource key.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
