# Development and build

Use .NET 10 for the Avalonia desktop application and shared ordering code. The native tray uses Windows C++, macOS Cocoa, or Linux Ayatana AppIndicator.

| Directory | Responsibility |
| --- | --- |
| desktop | UI, worker host, IPC and native trays |
| src | Ordering, read-only history, localization and preferences |
| tests | Sorting, interface, installer and client acceptance |
| packaging | Self-contained platform packages and installer translation files |
| agensi | Marketplace copy and optional agent skill |

```sh
dotnet build desktop/SidebarSorter.Desktop.csproj -c Release
dotnet run --project tests/CoreTests.csproj -c Release -- src artifacts/core-results.json
```

Publish Windows with runtime identifier `win-x64`, build its native tray, and compile `packaging/windows.iss` using the version from the desktop project. Unix packaging runs on the target architecture. The legacy Windows app are preserved in historical Git revisions.

Functional and client acceptance run on disposable cloud hosts. Authenticated desktop acceptance requires an isolated test account and a private test repository. Standard public runs use fictional data, or the official client with a signed-out profile.

English strings are the source caption keys. Embedded catalogs hold the optional localized strings. English is the fresh default. Files in `packaging/Languages` hold the localized Windows setup resources.

[Desktop acceptance](../.github/workflows/desktop.yml) · [Windows build and upgrade](../.github/workflows/windows-update.yml) · [Unix build and upgrade](../.github/workflows/unix-packages.yml)
