# Examples

Ready-to-use automation files. Copy the parts you like into your `automations.yaml`
(personal examples) or `%ProgramData%\AutoSettings\automations.yaml` (files named `*.machine.yaml`).

Every example is checked by the automated tests, so they always match the current format.

| File | What it shows |
|---|---|
| [gaming-mode.yaml](gaming-mode.yaml) | A profile applied while a game launcher runs and reverted when it closes; closing apps; notifications with placeholders |
| [presentation.yaml](presentation.yaml) | A profile tied to a focused app, reverted when you switch away |
| [day-and-night.yaml](day-and-night.yaml) | Time-of-day conditions (crossing midnight), battery conditions, cooldowns, radios |
| [meetings.yaml](meetings.yaml) | Default audio devices for calls, window-title filters, monitor-count conditions |
| [company.machine.yaml](company.machine.yaml) | Machine automations: boot scripts as SYSTEM, per-user sign-in rules, stopping a service while an app runs |
