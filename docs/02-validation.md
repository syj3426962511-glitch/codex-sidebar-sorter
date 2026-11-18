# Validation record

## Codex Sidebar Sorter 1.4.3

| Check | Verified result |
| --- | --- |
| Shared core | 29 checks each on Windows x64, Ubuntu 26.04.1 x64, and macOS arm64 |
| Desktop | All three jobs passed with 30 fictional projects and 360 chats. English first, twelve selectable languages, stale preview protection, saved recent order, and automatic sorting recovery were exercised |
| Windows x64 installer | Published 1.4.2 replaced while running, 1.4.3 reopened, preferences retained, and ordinary removal passed |
| Linux x64 installer | Installed and reopened at a Unicode path with spaces, retained preferences, and removed normally with fictional protocol data |
| Intel Mac and Apple Silicon Mac | Both self-contained DMGs were installed, replaced, reopened, and removed with fictional protocol data |
| Official Windows Codex 26.1111.9265.0 | The installed companion coexisted with signed-out Codex startup, ordinary close, full exit and reopening, Store update check, and removal. The app executable and archive matched. The Store reported no newer version |
| Official Ubuntu Codex 26.1111.21308 | The published 1.4.2 Linux installer has signed-in ordering and lifecycle evidence from the prior release |

[Core](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44215291003) · [Desktop](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44216250841) · [Windows installer](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44215290927) · [Unix installers](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44215291453) · [Official Windows](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44215787854) · [Official Linux 1.4.2](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44125514300)

The 1.4.3 installers contain binaries built from `121391d51d1b41e7c91c8355a059106c4d8175ea`. The final desktop test uses the same runtime sources with a corrected fixture check for equally sorted names. With the same case-insensitive sort key, ties retain their order in the sidebar.

During the ten-second installed idle samples, every platform recorded zero ordering writes. Working sets were approximately 54 MiB on Windows, 160 MiB on Linux, 62 MiB on Intel Mac, and 106 MiB on Apple Silicon.

Signed-in 1.4.3 client runs, an actual transition between distinct Codex client versions, and public Developer ID distribution for Mac remain open. The exact Mac installation status appears in the [compatibility matrix](04-compatibility.md).

## Prior release evidence

The Linux 1.4.2 test used Ubuntu 26.04.1 x64 and official Codex 26.1111.21308. Two projects and six chats passed all seven modes with saved-order read-back. After the companion window closed it sorted renamed items, survived a complete Codex exit, recovered after reopening, and uninstalled while Codex remained open. The official executable and application archive matched before and after the run. [Prior Linux test](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44125514300)

The earlier Windows 26.1111.2228.0 lifecycle run used a signed-out profile. The Windows installed-upgrade fixtures and historical schema tests are recorded in [the 1.4.2 release](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview).
