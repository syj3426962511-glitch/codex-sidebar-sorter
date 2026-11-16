# Development and build

## Components

| Directory | Responsibility |
| --- | --- |
| desktop | Avalonia UI, background host, IPC coordination, native tray |
| src | Sorting, read-only history access, localization, own preferences |
| tests | Shared core, protocol fixtures, desktop, installer, official-client checks |
| packaging | Windows setup definition and installer translations |
| agensi | Agent skill source, marketplace copy, upload package, submission record |

The current desktop implementation uses Avalonia and .NET 10. The preceding Windows implementation is retained as an upgrade fixture.

## Build commands

```sh
dotnet build desktop/SidebarSorter.Desktop.csproj -c Release
dotnet run --project tests/CoreTests.csproj -c Release -- src artifacts/core-results.json
```

For a self-contained Windows build, publish with runtime identifier `win-x64`, build the Windows native tray, and compile `packaging/windows.iss` with Inno Setup. Platform-specific native tray commands are maintained in the desktop workflow.

[Desktop workflow](../.github/workflows/desktop.yml) · [Windows installer workflow](../.github/workflows/windows-update.yml)

## Integration boundaries

Apply sidebar ordering through the client's available local tools. Read the history index through the existing read-only SQLite path. Store companion settings in its own user-data directory.

Client access authorization, tool contracts, and history schemas require current version checks. Record a connection rejection with its actual client version and request result before choosing an adapter change.

## Language resources

The desktop supports twelve language options. Chinese source messages, the English catalog, and the additional language catalogs are embedded resources. Interface-language changes retain sorting choices. Arabic uses right-to-left layout.

The Windows installer includes its matching language resources.

## Acceptance

Functional and client lifecycle acceptance use disposable cloud hosts. The owner separately authorized the local Windows upgrade and installed-state checks.

[Validation record](02-validation.md)
