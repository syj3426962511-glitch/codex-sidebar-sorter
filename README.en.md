# Codex Sidebar Sorter

A standalone Windows application with Chinese and English UI that applies sorting to the original Codex sidebar. Projects and chats within projects both default to ascending name order. The installer bundles the runtime, icons and native tray host, with a selectable installation directory.

Version 1.3.0 is a private acceptance candidate. The downloadable release follows cloud acceptance.

[中文说明](README.md)

## Install and use

1. Install and sign in to Codex Desktop on Windows x64, with at least one local chat.
2. Download this project's installer from Releases and select an installation directory.
3. Open the tool and choose English, 中文 or the system language.
4. Review the project and chat preview, select the scope and sorting choices, then click Apply to sidebar.

The installer contains the required .NET runtime and application resources. Reading and applying sidebar order use the running Codex client.

## Sorting choices

| Choice | Behavior |
| --- | --- |
| Name A to Z | Ascending names using zh-CN comparison rules for Chinese text |
| Name Z to A | Descending names |
| Natural numbers ascending | Numeric parts follow natural order, such as 2, 10, 20 |
| Natural numbers descending | Descending natural order |
| Recently updated first | Newest timestamps first, missing timestamps last |
| Oldest updates first | Oldest timestamps first, missing timestamps last |
| Manual order | Drag rows or use Move up, Move down, To top and To bottom |

Project order and chats within projects have separate choices. Existing pinned positions follow the native interface's supported behavior. Attached chats retain their parent relationship and are excluded from the independently sortable sidebar list.

Name and natural-number sorting apply on demand. After adding or renaming a chat or project, refresh and apply again. Codex shares the chat sort mode between project chats and Recents.

## Background and client lifecycle

Closing the tool window releases its managed UI and runtime. The native tray host continues waiting for system events. Open the application or double-click its tray icon to show the window, and use the tray menu to exit. The tray host can remain running while Codex is closed. Reading and applying order resume when Codex is open.

Application files and the runtime live in the tool's installation directory. Language preferences use the current user's `LocalAppData/CodexSidebarSorter/settings.json`. Sorting uses the client's existing local tools, and history uses a read-only SQLite connection. The tool discovers the local pipe on each connection. Changes to the client's native interface or history schema require a corresponding adapter update.

## Cloud acceptance

Production ordering, translations, settings, read-only history and message framing run on GitHub-hosted Windows, macOS and Ubuntu systems. Windows additionally exercises the bilingual UI, installation, running upgrade, uninstall and tray lifecycle. A separate Windows cloud workflow exercises official client startup, shutdown and the Store update check.

macOS and Ubuntu results cover shared core logic. The installable GUI targets Windows x64. Actions reports record the actual coverage and outcomes.

## Source and build

`src` contains the UI, sorting service, read-only history reader and native tray host. `packaging` contains the bilingual installer definition. `tests` and `.github/workflows` contain cloud tests and builds.

Source builds require the .NET 10 SDK, Visual Studio C++ build tools and Inno Setup. Build commands are in `.github/workflows/cloud-validation.yml`. Daily use of an installed release uses its bundled runtime.

## License

MIT, provided in LICENSE.
