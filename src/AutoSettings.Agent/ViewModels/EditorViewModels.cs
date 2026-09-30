using System.Collections.ObjectModel;
using System.ComponentModel;
using AutoSettings.Agent.Localization;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Editing;
using AutoSettings.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoSettings.Agent.ViewModels;

/// <summary>A value in a drop-down.</summary>
/// <param name="Value">Stored value ("" = not set).</param>
/// <param name="Label">Shown text.</param>
public sealed record OptionItem(string Value, string Label);

/// <summary>A catalog entry offered in a type drop-down.</summary>
/// <param name="Descriptor">The descriptor.</param>
public sealed record TypeOption(ComponentDescriptor Descriptor)
{
    /// <summary>Shown text.</summary>
    public string Label => $"{Strings.Title(Descriptor)} ({Descriptor.Type})";
}

/// <summary>Editor for one field of a trigger, condition or action.</summary>
public sealed partial class FieldViewModel : ObservableObject
{
    private string _text = "";
    private string _selectedValue = "";

    public FieldViewModel(FieldDescriptor field, object? value, ComponentKind kind, ExecutionScope scope, Action changed)
    {
        Field = field;
        Changed = changed;
        if (field.Type == FieldType.ConditionList)
        {
            Nested = new ComponentListViewModel(ComponentKind.Condition, value as IEnumerable<ComponentConfig> ?? [], scope, changed);
        }
        else if (IsChoice)
        {
            Options.Add(new OptionItem("", DefaultLabel));
            IReadOnlyList<string> values = field.Type == FieldType.Boolean ? ["true", "false"] : field.AllowedValues ?? [];
            foreach (var option in values)
                Options.Add(new OptionItem(option, option));
            _selectedValue = FieldText.Format(value);
        }
        else
        {
            _text = FieldText.Format(value);
        }
        Scope = scope;
    }

    public FieldDescriptor Field { get; }

    public ExecutionScope Scope { get; }

    private Action Changed { get; }

    public string Name => Field.Name;

    public string Label => Field.Required ? Field.Name + " *" : Field.Name;

    public string Description => Field.Description;

    /// <summary>Hint shown in an empty text box.</summary>
    public string Placeholder => Field.Default is not null
        ? Strings.Format("DefaultValue", FieldText.Format(Field.Default))
        : Field.Example ?? "";

    public bool IsChoice => Field.Type is FieldType.Enum or FieldType.Boolean;

    public bool IsMultiline => Field.Type == FieldType.Multiline;

    public bool IsPath => Field.Type == FieldType.Path;

    public bool IsAppList => Field.Type == FieldType.AppList;

    public bool IsUserList => Field.Type == FieldType.UserList;

    public bool IsConditionList => Field.Type == FieldType.ConditionList;

    public bool IsText => !IsChoice && !IsConditionList;

    public ObservableCollection<OptionItem> Options { get; } = [];

    /// <summary>For and/or/not: the nested conditions.</summary>
    public ComponentListViewModel? Nested { get; }

    private string DefaultLabel => Field.Default is not null
        ? Strings.Format("DefaultValue", FieldText.Format(Field.Default))
        : Strings.Get("NotSet");

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                Changed();
        }
    }

    public string SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (SetProperty(ref _selectedValue, value ?? ""))
                Changed();
        }
    }

    /// <summary>Appends a value to a list field (used by the pickers).</summary>
    public void AppendItem(string item)
    {
        var items = FieldText.FormatList(FieldText.Parse(Field, Text));
        if (!items.Contains(item, StringComparer.OrdinalIgnoreCase))
            items.Add(item);
        Text = string.Join(", ", items);
    }

    /// <summary>The raw value to store (null when not set).</summary>
    public object? RawValue()
    {
        if (Nested is not null)
            return Nested.ToComponents();
        if (IsChoice)
            return string.IsNullOrEmpty(SelectedValue) ? null : SelectedValue;
        return FieldText.Parse(Field, Text);
    }
}

/// <summary>A card in the visual editor: one trigger, condition or action.</summary>
public sealed partial class ComponentViewModel : ObservableObject
{
    private readonly ExecutionScope _scope;
    private readonly Action _changed;
    private TypeOption? _selectedType;
    private string _errors = "";
    private bool _suspend;

    public ComponentViewModel(ComponentKind kind, ComponentConfig component, ExecutionScope scope, Action changed)
    {
        Kind = kind;
        _scope = scope;
        _changed = changed;
        TypeOptions = AgentCatalog.Current.OfKind(kind).Where(d => EditorCatalog.IsUsable(d, scope)).Select(d => new TypeOption(d)).ToList();
        var descriptor = AgentCatalog.Current.Find(kind, component.Type);
        _selectedType = TypeOptions.FirstOrDefault(t => t.Descriptor == descriptor);
        UnknownType = descriptor is null ? component.Type : null;
        BuildFields(component);
        Validate();
    }

    public ComponentKind Kind { get; }

    /// <summary>Set when the YAML used a type this version does not know.</summary>
    public string? UnknownType { get; }

    public IReadOnlyList<TypeOption> TypeOptions { get; }

    public ObservableCollection<FieldViewModel> Fields { get; } = [];

    /// <summary>Asked by the parent list to remove / move this card.</summary>
    public event Action<ComponentViewModel, int>? MoveRequested;

    public IRelayCommand RemoveCommand => new RelayCommand(() => MoveRequested?.Invoke(this, 0));

    public IRelayCommand MoveUpCommand => new RelayCommand(() => MoveRequested?.Invoke(this, -1));

    public IRelayCommand MoveDownCommand => new RelayCommand(() => MoveRequested?.Invoke(this, 1));

    public ComponentDescriptor? Descriptor => _selectedType?.Descriptor;

    public string Title => Descriptor is { } d ? Strings.Title(d) : UnknownType ?? "?";

    public string Description => Descriptor?.Description ?? "";

    public TypeOption? SelectedType
    {
        get => _selectedType;
        set
        {
            if (value is null || value == _selectedType)
                return;
            var current = ToComponent();
            _selectedType = value;
            BuildFields(new ComponentConfig(value.Descriptor.Type, current.Parameters));
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
            FieldChanged();
        }
    }

    /// <summary>Problems with this card, one per line (empty when valid).</summary>
    public string Errors
    {
        get => _errors;
        private set
        {
            if (SetProperty(ref _errors, value))
                OnPropertyChanged(nameof(HasErrors));
        }
    }

    public bool HasErrors => Errors.Length > 0;

    /// <summary>The component as configured in the card.</summary>
    public ComponentConfig ToComponent()
    {
        var type = Descriptor?.Type ?? UnknownType ?? "";
        var parameters = Fields
            .Select(f => new KeyValuePair<string, object?>(f.Name, f.RawValue()))
            .Where(p => p.Value is not null);
        return new ComponentConfig(type, parameters);
    }

    private void BuildFields(ComponentConfig component)
    {
        _suspend = true;
        Fields.Clear();
        if (Descriptor is { } descriptor)
        {
            foreach (var field in descriptor.Fields)
                Fields.Add(new FieldViewModel(field, component.Parameters.GetValueOrDefault(field.Name), Kind, _scope, FieldChanged));
        }
        _suspend = false;
    }

    private void FieldChanged()
    {
        if (_suspend)
            return;
        Validate();
        _changed();
    }

    private void Validate()
    {
        if (Descriptor is null)
        {
            Errors = Strings.Format("UnknownType", UnknownType);
            return;
        }
        Errors = string.Join(Environment.NewLine, FieldText.Validate(Kind, ToComponent(), _scope, AgentCatalog.Current));
    }
}

/// <summary>A list of cards (triggers, conditions or actions) with add/remove/move.</summary>
public sealed class ComponentListViewModel
{
    private readonly ExecutionScope _scope;
    private readonly Action _changed;

    public ComponentListViewModel(ComponentKind kind, IEnumerable<ComponentConfig> components, ExecutionScope scope, Action changed)
    {
        Kind = kind;
        _scope = scope;
        _changed = changed;
        foreach (var component in components)
            Add(new ComponentViewModel(kind, component, scope, changed));
        Items.CollectionChanged += (_, _) => changed();
    }

    public ComponentKind Kind { get; }

    public ObservableCollection<ComponentViewModel> Items { get; } = [];

    /// <summary>Catalog entries grouped by category, for the "Add" menu.</summary>
    public IEnumerable<IGrouping<string, ComponentDescriptor>> AddMenu =>
        AgentCatalog.Current.OfKind(Kind).Where(d => EditorCatalog.IsUsable(d, _scope)).GroupBy(d => d.Category);

    /// <summary>Adds a new, empty card of the given type.</summary>
    public ComponentViewModel AddNew(ComponentDescriptor descriptor)
    {
        var item = new ComponentViewModel(Kind, new ComponentConfig(descriptor.Type), _scope, _changed);
        Add(item);
        return item;
    }

    public List<ComponentConfig> ToComponents() => Items.Select(i => i.ToComponent()).ToList();

    private void Add(ComponentViewModel item)
    {
        item.MoveRequested += OnMoveRequested;
        Items.Add(item);
    }

    private void OnMoveRequested(ComponentViewModel item, int offset)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
            return;
        if (offset == 0)
        {
            item.MoveRequested -= OnMoveRequested;
            Items.RemoveAt(index);
            return;
        }
        var target = index + offset;
        if (target >= 0 && target < Items.Count)
            Items.Move(index, target);
    }
}

/// <summary>The visual editor for one automation or profile.</summary>
public sealed partial class ItemEditorViewModel : ObservableObject
{
    private string _id = "";
    private string _name = "";
    private string _description = "";
    private bool _enabled = true;
    private string _cooldown = "";
    private string _priority = "0";

    public ItemEditorViewModel(bool isProfile, ExecutionScope scope)
    {
        IsProfile = isProfile;
        Scope = scope;
        Triggers = new ComponentListViewModel(ComponentKind.Trigger, [], scope, OnChanged);
        Conditions = new ComponentListViewModel(ComponentKind.Condition, [], scope, OnChanged);
        Actions = new ComponentListViewModel(ComponentKind.Action, [], scope, OnChanged);
    }

    public bool IsProfile { get; }

    public bool IsAutomation => !IsProfile;

    public ExecutionScope Scope { get; }

    public ComponentListViewModel Triggers { get; private set; }

    public ComponentListViewModel Conditions { get; private set; }

    public ComponentListViewModel Actions { get; private set; }

    /// <summary>Raised when anything changes.</summary>
    public event EventHandler? Modified;

    public string Id { get => _id; set { if (SetProperty(ref _id, value)) OnChanged(); } }

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) OnChanged(); } }

    public string Description { get => _description; set { if (SetProperty(ref _description, value)) OnChanged(); } }

    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) OnChanged(); } }

    public string Cooldown { get => _cooldown; set { if (SetProperty(ref _cooldown, value)) OnChanged(); } }

    public string Priority { get => _priority; set { if (SetProperty(ref _priority, value)) OnChanged(); } }

    public void Load(Automation automation)
    {
        _id = automation.Id;
        _name = automation.Name ?? "";
        _description = automation.Description ?? "";
        _enabled = automation.Enabled;
        _cooldown = automation.Cooldown is { } c ? ValueConverter.FormatDuration(c) : "";
        Triggers = new ComponentListViewModel(ComponentKind.Trigger, automation.Triggers, Scope, OnChanged);
        Conditions = new ComponentListViewModel(ComponentKind.Condition, automation.Conditions, Scope, OnChanged);
        Actions = new ComponentListViewModel(ComponentKind.Action, automation.Actions, Scope, OnChanged);
        OnPropertyChanged(string.Empty);
    }

    public void Load(Profile profile)
    {
        _id = profile.Id;
        _name = profile.Name ?? "";
        _description = profile.Description ?? "";
        _priority = profile.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Actions = new ComponentListViewModel(ComponentKind.Action, profile.Actions, Scope, OnChanged);
        OnPropertyChanged(string.Empty);
    }

    public Automation ToAutomation() => new()
    {
        Id = Id.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Enabled = Enabled,
        Cooldown = ValueConverter.TryParseDuration(Cooldown, out var cooldown) ? cooldown : null,
        Triggers = Triggers.ToComponents(),
        Conditions = Conditions.ToComponents(),
        Actions = Actions.ToComponents(),
    };

    public Profile ToProfile() => new()
    {
        Id = Id.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Priority = int.TryParse(Priority, out var priority) ? priority : 0,
        Actions = Actions.ToComponents(),
    };

    private void OnChanged() => Modified?.Invoke(this, EventArgs.Empty);
}

/// <summary>Catalog helpers for the editor.</summary>
public static class EditorCatalog
{
    /// <summary>Whether a descriptor can be used in a file of the given scope.</summary>
    public static bool IsUsable(ComponentDescriptor descriptor, ExecutionScope scope)
    {
        if (descriptor.Kind == ComponentKind.Action)
            return scope == ExecutionScope.Machine || descriptor.RunsAs == ExecutionScope.User || descriptor.RunsAsResolver is not null;
        return descriptor.AvailableIn.HasFlag(scope == ExecutionScope.User ? ScopeSupport.User : ScopeSupport.Machine);
    }
}
