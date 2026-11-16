# Codex Sidebar Sorter

A standalone Windows sidebar sorting application for Codex Desktop, with Chinese and English UI. The application uses its own process, bundled runtime and installer. Sorting requests use the running desktop client's native local tools. Local history is read using a read-only SQLite connection.

This repository is a private 1.3.0 candidate while cloud acceptance runs. Formal public release follows the required acceptance results.

## Candidate scope

- Chinese and English UI with an in-app language selector.
- Project lists and project chats default to ascending name order.
- Seven sorting choices, two-level previews, pinned-slot handling and manual arrangement.
- A self-contained Windows x64 installer with a selectable installation path.
- Windows, macOS and Ubuntu cloud tests for production ordering, translations, settings, read-only history and native message framing.
- Windows application, installer and client-lifecycle acceptance run in disposable cloud machines.

## Runtime requirements

Codex Desktop provides the native local sidebar tools. The application discovers that local interface on each connection. Its installer bundles the .NET runtime and the native tray component. Language and tool preferences are stored under the current user's Local AppData/CodexSidebarSorter directory.

## Source layout

- src contains the application, history reader and native tray host.
- packaging contains the bilingual installer definition.
- tests contains cross-platform checks of the production core.
- .github/workflows contains the cloud build and verification workflow.

## License

MIT.