---
name: codex-sidebar-sorter-windows
description: Help install, configure, and operate the independent Codex Sidebar Sorter Windows companion. Use for project and chat ordering, seven sort modes, twelve interface languages, automatic name sorting, tray operation, and companion connection or upgrade problems.
license: MIT
metadata:
  version: "1.4.1"
  product: "Codex Sidebar Sorter for Windows"
  platform: "Windows x64"
---

# Codex Sidebar Sorter for Windows

Help the user operate the installed Codex Sidebar Sorter desktop companion. The companion supplies the sorting interface and persistent background worker. Agensi delivers these agent instructions through MCP.

## Required environment

Use a Windows x64 computer, an installed Codex Sidebar Sorter 1.4.1 companion, and a compatible signed-in Codex Desktop on that computer. The companion installer includes its runtime.

Determine the installed companion path from the user's shortcut or installation record. The examples use the application name, so adapt to the user's chosen installation directory.

Public installer availability is recorded in the product documentation. Resolve an accessible, verified installer download before guiding a new installation.

## Interpret the request

Identify the requested scope, sorting rule, and whether the user wants continuous automatic name sorting or a manual application. Use the user's named project or section when given. Keep each chat associated with its existing project while reordering.

Respond in the user's language. The application's language selector controls its own interface language.

## Open and connect

Open the companion using its normal shortcut. Open a compatible, signed-in Codex Desktop and select Refresh in the companion. Review the project and chat preview and the status message.

With an authorized desktop-control surface, perform the requested application actions and observe the result. With a conversational-only surface, provide the exact controls for the user to select and report the action as awaiting their check.

## Continuous alphabetical sorting

Select Keep names sorted A to Z. The default project and project-chat modes become ascending names. The worker synchronizes supported sidebar items after additions and renaming.

Close the companion window to leave the worker and native tray running. The client receives sorting requests while its supported local connection is available.

## Apply another sorting rule

1. Select the target Scope.
2. Choose the project rule and the chat rule needed for the request.
3. Use Also sort chats in each project when the requested scope includes those chats.
4. Review the preview and select Apply to sidebar.
5. Read the resulting status and the refreshed saved order.

Choosing a custom rule or moving a row pauses continuous alphabetical sorting. Re-enable Keep names sorted A to Z when the user asks to resume it.

| Requested order | Application choice |
| --- | --- |
| Alphabetical ascending | Name A to Z |
| Alphabetical descending | Name Z to A |
| Item 2 before Item 10 | Natural numeric order |
| Reverse numeric order | Reverse natural order |
| Most recently updated first | Recently updated first |
| Oldest updated first | Oldest update first |
| Exact custom arrangement | Manual order |

For Manual order, drag rows or use Move up, Move down, To top, and To bottom. Missing update timestamps sort last for both date modes. Chinese name comparison follows the companion's zh-CN ordering rule.

Reset preview restores the saved preview baseline. After an interrupted apply, use Refresh to read the saved client state before another attempt.

## Change the interface language

Use the language selector at the top of the companion. Choose the system-language option or one of the twelve supported languages.

Simplified Chinese, Traditional Chinese, English, Japanese, Korean, French, German, Spanish, Portuguese, Italian, Russian, and Arabic are available. Arabic uses right-to-left layout. The companion saves its language preference.

## Background lifecycle

Closing the companion window exits the graphical process while its worker and tray continue. Open Sidebar Sorter in the tray reopens the window. Exit in the tray stops the companion.

The installed executable supports these entry points.

- `--show` opens the companion interface.
- `--background` starts or retains the worker.
- `--exit` coordinates a complete companion exit.

Use the actual installed executable for a requested entry-point action. Check the resulting process or application state before reporting completion.

## Connection and update support

Start with the actual status message and installed companion and client versions. Refresh after the client has started. When the status names a membership mismatch, expand the named Codex project and refresh the companion before retrying the requested operation.

Use an accepted companion installer for an upgrade. The installer coordinates the preceding companion worker and preserves its preferences.

Record client interface or authorization errors as pending integration work, with the specific message and version. The companion's normal controls and local tools are the supported ordering path.

## Compatibility

The desktop architecture and fictional-data tests cover Windows x64, macOS ARM64, and Linux x64. This listing targets the Windows companion. Mac and Linux consumer packages and their authenticated real-client sorting acceptance remain in development.

Current ordering scope covers supported unpinned projects, supported Codex project chats, and chats in pinned or custom sections. ChatGPT project-chat ordering, independent alphabetical Recents ordering, and pinned project placement require additional client support.

Client version replacement has a separate acceptance step. Use the current product validation record for supported versions and observed results.

## Report the result

Give the selected scope, sorting mode, automatic-sorting state, language, and the observed apply or connection result. Mark a result as applied after reading the application's completion state. For guided-only sessions, give the exact remaining user check.

The desktop screenshots in the product materials show fictional test projects and chats. Describe them as test examples when explaining a demonstration.
