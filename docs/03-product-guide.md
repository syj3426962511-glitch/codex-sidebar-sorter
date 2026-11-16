# Windows user guide

## Installation

Use the public Windows 1.4.2 preview installer, choose an installation path, and open the companion from its application shortcut. The installer includes the runtime and dependencies. Its cloud-installed acceptance has passed. Authenticated official-client ordering and actual client version replacement remain open acceptance items. [Windows preview](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview)

Open a compatible, signed-in Codex Desktop client on the same computer. Select Refresh in the companion and check the listed projects, chats, and any connection message.

## Automatic name order

Keep names sorted A to Z is enabled by default. The worker rechecks supported sidebar items after additions and renaming. Keep Codex running when applying changes to its sidebar.

## Other sorting rules

Select a scope, choose a project rule and a chat rule, and review the preview. Apply to sidebar submits the requested order. A custom rule or manual move pauses automatic name sorting. Re-enable Keep names sorted A to Z to restore continuous ascending-name sorting.

Supported choices are Name A to Z, Name Z to A, Natural numeric order, Reverse natural order, Recently updated first, Oldest update first, and Manual order.

For a project-only change, clear Also sort chats in each project. The Scope selector limits the operation to a supported project, pinned chats, or a custom section.

Reset preview restores the saved preview baseline. The application reads the saved state after a successful apply.

## Languages

Use the language selector at the top of the window. Available choices include the system language, Simplified Chinese, Traditional Chinese, English, Japanese, Korean, French, German, Spanish, Portuguese, Italian, Russian, and Arabic.

Language preferences are stored by the companion. Arabic mirrors the interface into right-to-left layout.

## Background use

Closing the companion window exits its graphical process. The separate worker and native tray continue running. Open Sidebar Sorter in the tray reopens the interface. Exit stops the companion.

Supported application entry points are `--show`, `--background`, and `--exit`. Choose the actual installed executable when using these entry points.

## Connection and membership messages

Refresh after Codex has started. If the application identifies a project-membership problem, expand the named project in Codex, then refresh the companion. Report the exact remaining message together with the application and client versions.

The current integration scope and platform acceptance are documented in the validation record.

[Validation record](02-validation.md)

## Update and recovery

Use the installer for an accepted companion update. The Windows installer coordinates the preceding companion process and preserves its preferences. Client version changes receive their own startup, shutdown, sorting, and replacement checks.

For manual sorting, review the saved preview baseline before applying an alternate order. Following an interrupted operation, refresh the saved client state before retrying.
