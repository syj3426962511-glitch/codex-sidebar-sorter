# Codex Sidebar Sorter

A standalone chat sorter with automatic ascending-name order. The current 1.4.1 candidate offers 12 language options, a shared desktop interface, a separate sorting worker, and platform-native tray menus.

Languages include Simplified Chinese, Traditional Chinese, English, Japanese, Korean, French, German, Spanish, Portuguese, Italian, Russian, and Arabic. Interface text, menus, and application messages follow the selected language. The tool stores its own language preference. Arabic uses a right-to-left layout.

This project is undergoing private cloud acceptance. The Windows 1.4.1 installer candidate passed installation, running legacy upgrades, preference retention and uninstall acceptance. macOS and Linux packages and public sharing links await complete real-client acceptance.

[中文说明](README.md)

## Current acceptance

| Coverage | Current result |
| --- | --- |
| Windows x64, macOS ARM64 and Linux x64 UI and shared logic | Passed in the cloud with 30 fictional projects and 360 fictional chats |
| Automatic sorting after additions and renaming | Passed against the protocol fixture |
| Pinned and custom-section chats | Automatic sorting passed in the cloud |
| Closing the UI, worker single instance, reopening and safe exit | Passed on all three platforms |
| Official Windows client | Window closing, complete exit, reopening, worker independence and startup after a Store update check passed, cross-version replacement pending |
| Official macOS client | MCP service initialization passed, native sidebar requests rejected with `untrusted-code-signing-identity` |
| Official Linux client | Tool catalog readable, signed-out list requests timed out, real sorting pending |
| Windows 1.4.1 installer candidate | Bundled runtime, 12 language options, Unicode paths, running legacy upgrades, preferences, full exit/reopen and uninstall passed |
| macOS and Linux packages and public release | Pending complete acceptance |

Functional and installation acceptance use disposable GitHub-hosted systems.

## Behavior

Projects and chats within Codex projects default to automatic ascending-name order. Adding or renaming items triggers event-driven synchronization. Selecting another rule, moving rows or resetting a preview pauses automatic sorting. Selecting Keep names sorted A to Z resumes it.

The interface provides project, chat and combined views, search, scope selection, two-level ordering, change previews, movement buttons and drag sorting. Apply to sidebar submits manual choices.

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

Project order and chats within projects have separate choices. Native project reordering covers unpinned projects in the default Projects section. Section reordering covers chats inside pinned and custom sections. Attached chats retain their parent relationship.

ChatGPT project-chat ordering and independent name ordering in Recents remain limited by the native interface. Codex shares the saved chat sort mode between project chats and Recents.

## Background and client lifecycle

Closing the window exits the graphical process while the separate worker and native tray remain running. The tray menu or application entry opens the UI again, and the tray menu provides Exit. The background uses file events and idle waits, reclaiming completed sorting allocations when the UI closes.

The UI and worker coordinate operations through the tool's own IPC channel. Language and sorting preferences use the tool's user-data directory. Sorting uses the client's existing local tools, history uses a read-only SQLite connection, and endpoints are rediscovered for each connection.

The client interface, access authorization and history schema require version adaptation. Startup, shutdown, actual package replacement and subsequent cold startup are recorded from real execution receipts.

## Cloud acceptance

Ordering, translations, settings, read-only history and message framing passed cross-platform tests. Desktop acceptance uses the production UI, automatic worker and native tray to check additions, renaming, manual choices, unchanged submissions, closing, single instance, reopening and exit.

Separate workflows exercise official clients. Protocol-fixture results, official-client results and installer results are recorded in Actions receipts and project acceptance records.

## Source and build

`desktop` contains the cross-platform UI, background controller and native trays. `src` provides shared ordering, read-only history, settings and translation, along with the preceding Windows implementation used for upgrade acceptance. `tests` and `.github/workflows` contain cloud acceptance. `packaging/windows.iss` defines the current Windows installer.

Source builds use .NET 10. Native trays use Windows C++, macOS Cocoa and Linux Ayatana AppIndicator. Desktop commands are in `.github/workflows/desktop.yml`, and Windows packaging and running-upgrade acceptance are in `.github/workflows/windows-update.yml`. The Windows installer includes the runtime, icons, resources and complete source.

## License

MIT, provided in LICENSE.
