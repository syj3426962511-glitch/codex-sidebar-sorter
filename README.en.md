# Codex Sidebar Sorter

A standalone chat sorter with Chinese and English UI and automatic ascending-name order. The current 1.4.0 candidate uses a shared desktop interface, a separate sorting worker, and platform-native tray menus.

This project is undergoing private cloud acceptance. Windows, macOS and Linux packages and public sharing links follow successful real-client integration and lifecycle acceptance.

[中文说明](README.md)

## Current acceptance

| Coverage | Current result |
| --- | --- |
| Windows x64, macOS ARM64 and Linux x64 UI and shared logic | Passed in the cloud with 30 fictional projects and 360 fictional chats |
| Automatic sorting after additions and renaming | Passed against the protocol fixture |
| Pinned and custom-section chats | Automatic sorting passed in the cloud |
| Closing the UI, worker single instance, reopening and safe exit | Passed on all three platforms |
| Official Windows client | Ordinary window closing and reopening verified, complete exit and post-update startup under acceptance |
| Official macOS client | Independent processes rejected with `untrusted-process-ancestry` |
| Official Linux client | Tool catalog readable, signed-out list requests timed out, real sorting pending |
| 1.4.0 installers and public release | Pending complete acceptance |

Runtime and installation tests use disposable GitHub-hosted systems. Local work in this development round is limited to source editing, historical records and cloud receipts.

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

`desktop` contains the cross-platform UI, background controller and native trays. `src` provides shared ordering, read-only history, settings and translation, along with the existing Windows implementation. `tests` and `.github/workflows` contain cloud acceptance. `packaging` retains the existing Windows installer definition.

Source builds use .NET 10. Native trays use Windows C++, macOS Cocoa and Linux Ayatana AppIndicator. Current build and runtime commands are in `.github/workflows/desktop.yml`. Release packages are planned to include the runtime and resources.

## License

MIT, provided in LICENSE.
