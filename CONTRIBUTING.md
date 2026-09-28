# Contributing

Thanks for helping improve AutoSettings!

- **Bugs and ideas**: open an issue. For bugs, include the relevant lines of the Activity tab and the logs
  (`%LocalAppData%\AutoSettings\logs`, `%ProgramData%\AutoSettings\logs`) and your Windows version.
- **Security issues**: please use a private security advisory, not a public issue.

## Development

See [docs/dev/building.md](docs/dev/building.md) for building and testing, and
[docs/dev/adding-an-action.md](docs/dev/adding-an-action.md) for adding actions, conditions and triggers.

Before opening a pull request:

1. `dotnet build` and `dotnet test` pass.
2. If you changed the catalog, regenerate the reference docs: `dotnet run --project src/AutoSettings.DocGen`.
3. New or changed UI texts and component titles are in `tools/strings.py` in English and Turkish; run
   `python3 tools/strings.py` to regenerate the resources.
4. User-visible changes are documented (guide pages, `CHANGELOG.md`).

## Translations

All UI texts are in `tools/strings.py`. To improve the Turkish texts or add a language, edit that file and run it;
see [docs/dev/building.md](docs/dev/building.md#translations).

## Style

- Follow `.editorconfig`; match the surrounding code.
- Messages shown to users (descriptions, errors, activity entries) are plain language and say what to do.
- Keep Core free of Windows APIs so it stays testable everywhere.
