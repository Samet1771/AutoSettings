namespace AutoSettings.Sdk;

/// <summary>Where something runs.</summary>
public enum PluginScope
{
    /// <summary>In the signed-in user's AutoSettings app, as that user. The default.</summary>
    User,

    /// <summary>
    /// In the AutoSettings service, as SYSTEM, with full control of the computer. Only plugins installed for the
    /// whole computer (by an administrator) may have machine components, and only machine automations can use them.
    /// </summary>
    Machine,
}

/// <summary>Which automation files a trigger or condition may be used in.</summary>
[Flags]
public enum PluginAvailability
{
    /// <summary>Personal automations.</summary>
    User = 1,

    /// <summary>Machine automations.</summary>
    Machine = 2,

    /// <summary>Both (the default).</summary>
    Both = User | Machine,
}

/// <summary>The type of a field, which decides how it is checked and which control the editor shows.</summary>
public enum FieldKind
{
    /// <summary>One line of text.</summary>
    String,

    /// <summary>Several lines of text.</summary>
    Multiline,

    /// <summary>A file or folder path (the editor shows a Browse button).</summary>
    Path,

    /// <summary>A whole number. Use <see cref="FieldAttribute.Minimum"/> and <see cref="FieldAttribute.Maximum"/> for limits.</summary>
    Integer,

    /// <summary>A number with decimals.</summary>
    Number,

    /// <summary>Yes or no.</summary>
    Boolean,

    /// <summary>One of <see cref="FieldAttribute.Values"/>.</summary>
    Enum,

    /// <summary>A duration such as <c>30s</c> or <c>1h30m</c>.</summary>
    Duration,

    /// <summary>A time of day such as <c>22:00</c>.</summary>
    Time,

    /// <summary>A list of text. If <see cref="FieldAttribute.Values"/> is set, each item must be one of them.</summary>
    StringList,

    /// <summary>A list of apps (exe names or paths); the editor shows an app picker.</summary>
    AppList,

    /// <summary>A list of user names; the editor shows a user picker.</summary>
    UserList,
}

/// <summary>
/// Describes a component: its type as written in YAML, its title and what it does. Put it on every class passed to
/// <see cref="IPluginBuilder"/>. A trigger class can have several, one per event it raises.
/// </summary>
/// <example>
/// <code>
/// [PluginComponent("acme.usb.eject", Title = "Eject a USB drive", Description = "Safely removes a USB drive.")]
/// [Field("drive", FieldKind.String, Description = "Drive letter, for example E:.", Required = true, KeyField = true)]
/// public sealed class EjectAction : IPluginAction { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PluginComponentAttribute : Attribute
{
    /// <summary>Declares a component.</summary>
    /// <param name="type">
    /// The type used in YAML: the plugin id, a dot and a name of lower-case letters, digits and underscores, for
    /// example <c>acme.usb.eject</c> for the plugin <c>acme.usb</c>.
    /// </param>
    public PluginComponentAttribute(string type) => Type = type;

    /// <summary>The type used in YAML.</summary>
    public string Type { get; }

    /// <summary>Short title in English, for example "Eject a USB drive". Required.</summary>
    public string Title { get; set; } = "";

    /// <summary>What it does, in plain English. Required.</summary>
    public string Description { get; set; } = "";

    /// <summary>The group in the editor's Add menu. Defaults to the plugin's name.</summary>
    public string? Category { get; set; }

    /// <summary>For actions: where it runs. Machine actions need a plugin installed for the whole computer.</summary>
    public PluginScope RunsAs { get; set; } = PluginScope.User;

    /// <summary>For triggers and conditions: which automation files may use it.</summary>
    public PluginAvailability AvailableIn { get; set; } = PluginAvailability.Both;

    /// <summary>
    /// For triggers: the event that undoes this one, for example <c>acme.usb.disconnected</c> for
    /// <c>acme.usb.connected</c>. A profile applied by this trigger with <c>revert_on: auto</c> is reverted when it
    /// happens.
    /// </summary>
    public string? OppositeEvent { get; set; }

    /// <summary>A YAML example, for example <c>type: acme.usb.eject\ndrive: "E:"</c>. Defaults to just the type.</summary>
    public string? Example { get; set; }

    /// <summary>Extra notes for the documentation, such as requirements or limitations.</summary>
    public string? Notes { get; set; }
}

/// <summary>Declares a parameter of the component classes it is put on.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    /// <summary>Declares a field.</summary>
    /// <param name="name">
    /// Name used in YAML: lower-case letters, digits and underscores, starting with a letter, for example
    /// <c>drive</c>. <c>type</c>, <c>user</c> and <c>continue_on_error</c> are reserved.
    /// </param>
    /// <param name="kind">The type of the value.</param>
    public FieldAttribute(string name, FieldKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>Name used in YAML.</summary>
    public string Name { get; }

    /// <summary>The type of the value.</summary>
    public FieldKind Kind { get; }

    /// <summary>What the field means, in plain English. Required.</summary>
    public string Description { get; set; } = "";

    /// <summary>Whether the automation must set it.</summary>
    public bool Required { get; set; }

    /// <summary>The value used when the automation does not set it, written as in YAML (for example <c>"30s"</c> or <c>"true"</c>).</summary>
    /// <remarks>On a trigger, a field with a default always filters events; leave filters without a default.</remarks>
    public string? Default { get; set; }

    /// <summary>For <see cref="FieldKind.Enum"/> (required) and <see cref="FieldKind.StringList"/>: the allowed values.</summary>
    public string[]? Values { get; set; }

    /// <summary>For numbers: the smallest allowed value. <see cref="double.NaN"/> (the default) means no limit.</summary>
    public double Minimum { get; set; } = double.NaN;

    /// <summary>For numbers: the largest allowed value. <see cref="double.NaN"/> (the default) means no limit.</summary>
    public double Maximum { get; set; } = double.NaN;

    /// <summary>An example value shown in the editor and the documentation.</summary>
    public string? Example { get; set; }

    /// <summary>
    /// For revertible actions: this field tells settings apart (for example the drive letter), so two actions with
    /// different values change different settings.
    /// </summary>
    public bool KeyField { get; set; }

    /// <summary>
    /// For text fields: whether <c>{{ placeholders }}</c> are replaced before the action gets the value. True by
    /// default; set it to false for fields that are interpreted as code or commands.
    /// </summary>
    public bool Placeholders { get; set; } = true;
}

/// <summary>A translated title and description for a component, for example in Turkish.</summary>
/// <example><c>[Localized("tr", "USB sürücüyü çıkar", Description = "USB sürücüyü güvenle kaldırır.")]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class LocalizedAttribute : Attribute
{
    /// <summary>Adds a translation.</summary>
    /// <param name="language">Two-letter language code, for example <c>tr</c>.</param>
    /// <param name="title">The title in that language.</param>
    public LocalizedAttribute(string language, string title)
    {
        Language = language;
        Title = title;
    }

    /// <summary>Two-letter language code.</summary>
    public string Language { get; }

    /// <summary>The title in that language.</summary>
    public string Title { get; }

    /// <summary>The description in that language (optional).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// For classes with several <see cref="PluginComponentAttribute"/>s: the type this translation is for. Leave it
    /// empty when the class declares one component.
    /// </summary>
    public string? Type { get; set; }
}
