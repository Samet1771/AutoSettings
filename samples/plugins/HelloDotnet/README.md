# Hello .NET (sample .NET plugin)

A .NET plugin that uses every part of the SDK. It has:

- `example.dotnet.set_variable`: a revertible action that sets a user environment variable;
- `example.dotnet.variable_equals`: a condition;
- `example.dotnet.file_created` and `example.dotnet.file_deleted`: triggers from a `FileSystemWatcher`, so they
  react instantly instead of polling.

## Build and try

```powershell
dotnet build -c Release
autosettings-plugin pack bin\Release\net10.0 --out dist     # writes dist\example.dotnet-1.0.0.aspkg
```

Install `dist\example.dotnet-1.0.0.aspkg` from the Plugins page in AutoSettings. While developing, you can instead
copy the packed folder to `%LocalAppData%\AutoSettings\plugins\example.dotnet\1.0.0`.

See [Writing a .NET plugin](../../../docs/plugins/dotnet-plugins.md).
