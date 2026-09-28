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
3. User-visible changes are documented (guide pages, `CHANGELOG.md`).

## Style

- Follow `.editorconfig`; match the surrounding code.
- Messages shown to users (descriptions, errors, activity entries) are plain language and say what to do.
- Keep Core free of Windows APIs so it stays testable everywhere.
