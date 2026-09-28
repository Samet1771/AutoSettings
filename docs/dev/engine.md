# Rule engine semantics

`RuleEngine` (in `AutoSettings.Core.Engine`) is the heart of AutoSettings. One instance runs in the service
(machine automations) and one in each agent (personal automations).

## Processing an event

Events are queued (`Post`) and processed **one at a time, in order** (`RunAsync` → `HandleEventAsync`):

1. **User attribution**: events without a user get the engine's `CurrentUser` (the agent's user).
2. **Process tracking**: `ProcessTracker` updates the running-process list and computes whether an `app_started` is
   the *first* instance and an `app_closed` the *last* instance (per session and exe name).
3. **Automatic reverts**: every active profile whose revert rule matches the event is reverted (most recent first).
   This happens even while automations are paused.
4. If paused, stop here.
5. For each **enabled** automation, in file order:
   1. If none of its triggers match, skip it (`TriggerMatcher`).
   2. **Reservation**: skip if suspended by the loop guard, if it is still running, or if it is inside its cooldown.
      Otherwise record the run for the loop guard.
   3. **Conditions**: evaluate in order; the first false one stops evaluation and is reported in the activity log.
   4. **Run**: the actions are started in the background, so a long `delay` does not block other automations.

`WhenIdleAsync` waits for running automations (used by tests and shutdown).

## Actions

`ExecuteActionAsync`:

1. fills in defaults from the catalog;
2. handles built-ins: `profile.apply`, `profile.revert`, `delay`;
3. finds the handler for the type (error if not available here);
4. replaces [placeholders](../reference/placeholders.md) in text fields;
5. in dry-run mode, logs instead of executing;
6. runs the handler; exceptions become a logged failure (the message of `ActionFailedException` is shown as-is).

An automation stops at the first failed action unless that action has `continue_on_error: true`.

## Run modes

An automation cannot run twice at the same time: triggers while it runs are ignored and logged ("still running").
`cooldown` ignores triggers for a while after a run started.

## Loop guard

If an automation starts more than `MaxRunsPerMinute` (default 20) times within 60 seconds, it is **suspended** and an
error explains why. Typical cause: two automations undoing each other, or an action that triggers its own trigger
(e.g. `app.launch` of the app in an `app_started` rule with `instance: any`). Reloading the configuration, or
`ResumeAutomation`, lifts the suspension.

## Profiles

`ProfileManager` keeps:

- the list of **active profiles** (entries), each with a sequence number and an optional revert rule;
- a **baseline** per *setting key*: the value captured before the first active profile changed that setting.

A *setting key* is the action type plus the values of the descriptor's `KeyFields` (for example
`audio.volume|headphones`).

**Owner** of a key = the active entry that sets it with the highest `(priority, sequence)`.

Apply profile P:

- for each revertible action: capture the baseline if none exists for its key; execute the action only if P becomes
  the owner (otherwise log that a higher-priority profile wins);
- non-revertible actions just execute;
- applying an already active profile only updates its revert rule.

Revert profile P (remove it, then for each key it set, in reverse order):

- if another active entry with a higher order also sets the key, nothing changes (P was not visible);
- else if another active entry sets the key, re-run that entry's action (the next owner);
- else restore the baseline and forget it.

Revert rules are created by `RevertRule.Create(revert_on, triggeringEvent)`; app-based rules match the **same exe name**
(and session) as the event that applied the profile.

The unit tests in `RuleEngineTests` cover these cases (stacking in both orders, focus/unfocus pairing, cooldown,
loop guard, dry run, continue_on_error, first/last instance).

## Conditions

`and`, `or`, `not`, `user`, `time`, `app_running` and `profile_active` are evaluated by the engine itself; others by
condition handlers registered by the host. A condition that throws counts as false and is logged.

`time`: `after` is inclusive, `before` is exclusive; if `after > before` the window crosses midnight; `weekdays` is
checked against the current day.

## Thread safety

- Events: single consumer, guarded by a semaphore.
- Configuration: an immutable snapshot swapped atomically by `UpdateConfig`.
- Profiles: their own semaphore; the engine never holds the event lock while an automation's actions run.
- The activity log is thread-safe and bounded (1000 entries).
