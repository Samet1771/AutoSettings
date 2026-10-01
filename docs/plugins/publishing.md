# Packaging and publishing

## The package

Users install a plugin from a **`.aspkg` file**. It is a zip file with `plugin.yaml` at its root and the plugin's files
next to it. Make one with the `autosettings-plugin` tool:

```powershell
autosettings-plugin pack <folder> --out dist
```

- For a **script plugin**, `<folder>` is the plugin folder (with `plugin.yaml` and the scripts).
- For a **.NET plugin**, `<folder>` is the build output (`bin\Release\net10.0`). The tool writes the `components` of
  `plugin.yaml` from your attributes.

The tool checks everything AutoSettings checks when installing, then writes `dist\<id>-<version>.aspkg` and adds
its line to `dist\SHA256SUMS.txt`. Debug symbols (`.pdb`) and `AutoSettings.Sdk.dll` are left out.

AutoSettings refuses a package when:

- a file path would leave the plugin folder (`..`, a drive letter or an absolute path);
- it has more than 5000 files or is larger than 200 MB unpacked;
- `plugin.yaml` is missing or invalid, or a script or the `entry` assembly is missing;
- it contains `AutoSettings.Sdk.dll`.

## Publishing on GitHub

Publish each version as a **GitHub release** of your repository:

1. Tag the release with the version, for example `v1.2.0` (or `1.2.0`). Pre-releases (`v1.3.0-beta.1`) are only
   offered to users who turned on beta versions.
2. Attach the package (`acme.usb-1.2.0.aspkg`) **and `SHA256SUMS.txt`**. AutoSettings refuses a download whose
   checksum is not published, either in `SHA256SUMS.txt` or as the checksum GitHub shows for the file.
3. Put the repository in `plugin.yaml`, so installed copies find their updates:

   ```yaml
   update:
     github: acme/autosettings-usb
     asset: "*.aspkg"        # optional; which release file is the package
   ```

Users can then install the plugin from the repository instead of a file: on the Plugins page, or with
`AutoSettings.Agent.exe --plugin install acme/autosettings-usb`. AutoSettings takes the newest release that has a
matching package, checks the checksum and installs it.

## Updates

AutoSettings looks for newer releases of every installed plugin that has `update.github`. A newer version is shown on
the Plugins page and installed with **Update**, or with `--plugin update <id>` or `--plugin update --all`. The version
before the update is kept: **Roll back** (or `--plugin rollback <id>`) returns to it.

The plugin id must stay the same in every version. A release whose package has another id is refused.

## Versions

Use [semantic versioning](https://semver.org) for your plugin:

- **patch** (1.2.**1**) for fixes;
- **minor** (1.**3**.0) for new components or fields;
- **major** (**2**.0.0) when an automation that worked may stop working, for example after a component or a
  required field was removed or renamed.

Renaming a component type breaks every automation that uses it. Those automations are then marked
**needs plugin**. Keep old types working, or tell users what to change.

Set `min_app_version` when you use a feature of a newer AutoSettings. Set `sdk` to the SDK major version you built
against.
