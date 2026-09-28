# The editor

AutoSettings has two ways to edit an automation or a profile, like Home Assistant: a **visual** editor and a
**YAML** editor. Both edit the same thing, and you can switch between them at any time.

Open the editor from the **Automations** or **Profiles** page: **New**, **Edit**, or double-click a row.

## Visual view

The editor shows three sections for an automation:

- **When**: the triggers. Any one of them starts the automation.
- **If**: the conditions. All of them must be true. Leave it empty to always run.
- **Then**: the actions, run from top to bottom.

A profile only has **Then** (its settings) and a **priority**.

Each trigger, condition and action is a **card**:

- The drop-down at the top picks its type. Changing the type keeps the fields both types have in common.
- The description below it says what it does.
- The fields come from the [reference](../reference/index.md). Hover over a field name for help. `*` marks
  required fields. Empty fields use their default, which is shown greyed out.
- Problems are listed in red at the bottom of the card, for example *"'level' must be at most 100"*.
- **↑ ↓** reorder the cards, **✕** removes one.

Special fields:

| Field | Help |
|---|---|
| Apps (`app`) | **Pick app…** lists running apps and apps from the Start menu, with icons. Choose *Use the full path* to match one specific exe. You can also type names separated by commas, and use wildcards (`*steam*`). |
| Users (`user`, `users`) | **Pick user…** lists the accounts on this PC and who is signed in. |
| Files and folders | **Browse…** opens a file or folder picker. |
| Choices | Drop-downs list the allowed values; the first entry is the default. |
| `and` / `or` / `not` | Nested conditions appear inside the card, with their own **+ Add condition** button. |

**+ Add trigger / condition / action** opens a menu grouped by category (Display, Audio, Power, ...).

## YAML view

The **YAML** tab shows the same automation as YAML text:

- **Autocomplete** appears as you type, or with **Ctrl+Space**. It suggests:
  - trigger, condition and action **types** after `type:`, only those allowed in this file;
  - the **fields** of the entry you are in, with their descriptions, leaving out the ones already there;
  - **values** for choices, `true`/`false`, and profile ids after `profile:`.
- **Hover** over a type or field name for its description.
- **Squiggles** underline problems: red for errors, orange for warnings. Hover over the line to read the message,
  or double-click it in the list below the editor to jump there.

You can only go back to the visual view when the YAML has no errors, like in Home Assistant.

## Saving

**Save** checks the automation together with the rest of the file (for example, whether the profile it applies
exists) and writes `automations.yaml`. If something is wrong, nothing is saved and the problems are listed.

**Test** runs the actions of the automation as it is in the editor, even before you save. The result is in the
**Activity** page.

!!! note "Comments are not kept"
    The editor writes the whole file in a standard format, so **comments and custom formatting are lost** the first
    time you save from the editor (AutoSettings asks once). If you like to keep comments, use **Edit whole file**
    instead. That editor saves your text exactly as typed, with the same autocomplete and checks.

## Edit whole file

**Automations → Edit whole file** (or **Settings → Edit whole file**) opens `automations.yaml` as text, with the
same autocomplete, hover help and squiggles. A file with errors is never saved. Use this editor for larger changes,
to keep comments, or when the file has errors that stop the visual editor from opening.

## Templates

The **Templates** page has ready-made automations: gaming mode, presentation mode, day and night, meetings, battery
saver and focus time. **Add** copies a template into your file. If a template uses an id you already have, the copy
gets a new id such as `gaming-2`. Then adjust it: app names, devices, times.

## Import and export

- **Export** saves the selected automations, or all of them, to a `.yaml` file, together with the profiles they use.
- **Import** adds the automations and profiles from such a file. Clashing ids are renamed.

Use this to share automations with others or move them to another PC.

## Other list actions

- The switch in the **On** column enables or disables an automation.
- **Duplicate** makes a copy right below. **↑ ↓** change the order, which is the order automations run in when the
  same event triggers several of them.
- **Delete** removes the selected automations after asking.
- **Run now** / **Run, ignoring conditions** test the saved automation.
