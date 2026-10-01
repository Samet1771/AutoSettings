# Versions and compatibility

Three version numbers matter for plugins.

| Version | Where | Meaning |
|---|---|---|
| **Plugin version** | `version` in `plugin.yaml` | Your plugin's own version. See [Publishing](publishing.md#versions). |
| **SDK version** | `sdk` in `plugin.yaml`, `SdkInfo.Version` | The plugin API: the `AutoSettings.Sdk` types, the manifest format and the script contract. |
| **App version** | `min_app_version` in `plugin.yaml` | The oldest AutoSettings that has what your plugin needs. |

## The SDK version

The SDK uses [semantic versioning](https://semver.org) and changes independently of AutoSettings:

- **Minor and patch** versions (1.1, 1.2, …) only **add**: new types, members, field types or manifest keys.
  Plugins built for 1.0 keep working with every 1.x.
- A **major** version (2.0) may remove or change things. AutoSettings lists the major versions it can load
  (`PluginManifestValidator.SupportedSdkMajorVersions`, currently `1`). When 2.0 arrives, AutoSettings keeps loading 1.x
  plugins for at least one more release, and the release notes say when 1.x support ends.

Write `sdk: "1.0"` (the major and minor version you built against) in `plugin.yaml`. AutoSettings refuses a plugin
whose major version it does not support, with a message that says so.

### What is guarded

- **The .NET API.** A test compares the public API of `AutoSettings.Sdk` with a checked-in snapshot
  (`tests/AutoSettings.Core.Tests/AutoSettings.Sdk.api.txt`). An accidental change fails the build. Additions update
  the snapshot and raise the minor version; anything else needs a major version.
- **The process protocol.** .NET plugins run in `AutoSettings.PluginHost.exe`, which talks to AutoSettings over a
  versioned protocol (`SdkInfo.ProtocolVersion`). The host ships with AutoSettings, so it always matches the app. Your
  plugin only depends on the SDK types.
- **The script contract.** The JSON request on standard input, the `AUTOSETTINGS_*` variables and the output rules in
  [Writing a script plugin](script-plugins.md) follow the same rule: new fields and variables can appear in a minor
  version, and nothing is removed or renamed before a major version.

## The app version

Set `min_app_version` to the first AutoSettings version that has everything your plugin uses, for example a newer
field type. Beta versions of that release count too (0.3.0-beta.2 satisfies `min_app_version: 0.3.0`). Older apps
refuse to install the plugin and say which version is needed.

## Packages on NuGet

| Package | What |
|---|---|
| `AutoSettings.Sdk` | The SDK for .NET plugins. Reference it with `ExcludeAssets="runtime"`. |
| `AutoSettings.PluginTool` | The `autosettings-plugin` command: `pack`, `validate` and `describe`. |
| `AutoSettings.Templates` | `dotnet new autosettings-plugin`. |

All three are released together with the SDK version.
