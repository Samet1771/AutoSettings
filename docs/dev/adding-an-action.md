# Adding an action, condition or trigger

Everything the user can write in YAML is described once in the **catalog** (Core) and implemented by a **handler**
(Platform). Validation, defaults, the JSON Schema, the UI summaries and the reference docs all come from the catalog.

## Adding an action, step by step

Example: an action `display.night_light` that turns Night light on or off.

### 1. Describe it (Core)

Add a descriptor to `src/AutoSettings.Core/Catalog/BuiltInActions.cs`:

```csharp
new()
{
    Type = "display.night_light",
    Kind = ComponentKind.Action,
    Category = Categories.Display,
    Title = "Night light",
    Description = "Turns Night light (warmer colors) on or off.",
    Revertible = true,                         // can be undone by profiles
    Fields =
    [
        Fields.Choice("state", "on, off, or toggle.", ["on", "off", "toggle"], required: true),
    ],
    Example = """
        type: display.night_light
        state: on
        """,
    Notes = "Anything users should know: requirements, limitations.",
},
```

Guidelines:

- `Type`: `area.verb_or_setting`, lower case, as users will type it.
- Descriptions are for end users: plain language, one or two sentences.
- `RunsAs = ExecutionScope.Machine` if it needs administrator rights; use `RunsAsResolver` if that depends on the
  fields (see `registry.set`, `command.run`).
- `KeyFields`: fields that select *which* setting is changed (e.g. the audio `device`), so profiles can stack
  correctly.
- `Validate`: cross-field rules (see `display.resolution`).
- Script-like fields that are executed as code must set `AllowPlaceholders = false` (see
  [security](security.md#placeholders-and-scripts)).
- Every action automatically gets `continue_on_error`.

### 2. Implement it (Platform)

Add a handler in `src/AutoSettings.Platform/Actions/`. For simple synchronous actions derive from
`SyncActionHandler` or `SyncRevertibleActionHandler`:

```csharp
public sealed class NightLightAction : SyncRevertibleActionHandler
{
    public override string Type => "display.night_light";

    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var state = action.GetString("state");   // typed, defaults filled in
        // ... change the setting; throw ActionFailedException("plain message") on failure
    }

    protected override string? Capture(ComponentConfig action, ActionContext context) =>
        /* current value as text */ "on";

    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        // put the captured value back
    }
}
```

Rules:

- Parameters arrive typed (`GetInteger`, `GetBoolean`, `GetDuration`, `GetStringList`...), with defaults applied and
  placeholders replaced.
- Throw `ActionFailedException` with a message a user understands; it is shown in the activity log.
- `Capture` returns text (it may travel over IPC). Include what you need to restore precisely (e.g. a device id).
- Handlers run on thread-pool threads; they may be called concurrently for different automations.

### 3. Register it

Add it to `PlatformHandlers.UserActions(...)` (runs as the user in the agent) and/or `MachineActions()` (runs as
SYSTEM in the service). The service automatically routes user actions of machine automations to the right agent.

### 4. Document and test

```powershell
dotnet run --project src/AutoSettings.DocGen   # generates docs/reference/actions/display.night_light.md
dotnet test
```

`CatalogTests` automatically checks that the new descriptor has a title, descriptions for every field and a valid
example.

### 5. Translate the title

The app shows component titles in the user's language. Add the Turkish title to `CATALOG_TR` in
`tools/strings.py` and regenerate the resources:

```bash
python3 tools/strings.py
```

`LocalizationTests` fails if a component or category has no Turkish title. Add an example to `docs/examples/` if it enables a new scenario; those files are validated by the tests too.

## Adding a condition

Same as an action, in `BuiltInConditions.cs` with `Kind = ComponentKind.Condition`, and a class implementing
`IConditionHandler` (or deriving from `SyncConditionHandler`) registered in `PlatformHandlers.CommonConditions()`
(works in the service and the agent) or `UserConditions()` (needs the user's desktop; also set
`AvailableIn = ScopeSupport.User` on the descriptor).

## Adding a trigger

Triggers need an event source:

1. Add a `SystemEventKind` value (Core) and its name in `EventNames.TriggerType`/`Describe`.
2. Add the descriptor in `BuiltInTriggers.cs` with `EventKind` set.
3. Add matching logic for its fields in `TriggerMatcher`.
4. Raise the event: in the service (`MachineHost`, forwarded to agents through `AgentHub.SendEvent`) or in the agent
   (`AgentHost`), usually from a class implementing `ISystemEventSource` in Platform.
5. If a profile applied by this trigger should revert automatically, extend `RevertRule.Create`.
