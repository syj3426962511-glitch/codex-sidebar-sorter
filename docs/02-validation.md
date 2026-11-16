# Validation record

Updated on November 16, 2026.

## Current 1.4.2 cloud candidate

The 1.4.2 candidate from `23f9d67` passed 25 core checks on each cloud platform, all three desktop jobs, and Windows installed-upgrade acceptance. Its Windows installer is 44,319,278 bytes. The six history-schema cases cover the fields consumed from historical data generations.

Windows installed acceptance used the production executable with fictional native-protocol fixtures. It covered the normal entry point, custom Unicode paths, bundled runtime and source, automatic sorting, older preferences, closing the window while the worker keeps sorting, upgrades, uninstall, and retained preferences. An approximately ten-second idle sample recorded 76,578,816 working-set bytes, 187.5 ms accumulated CPU time, and zero sorting writes.

[Core acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43883906361) · [Desktop acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43883914547) · [Windows installed-upgrade acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43883922669)

## Locally installed Windows package

The accepted installer is `CodexSidebarSorter-1.4.1-Windows-Setup.exe`, built from `036f029`, with a size of 44,062,557 bytes.

Cloud acceptance covered a custom path containing Chinese characters and spaces, the bundled runtime and source, upgrades while an earlier worker was running, retained preferences, automatic name sorting, full exit and reopening, and uninstall.

An approximately ten-second idle sample recorded 68.20 MiB working set, 218.75 ms accumulated CPU time, and zero sorting writes.

The authorized local upgrade retained the companion's preferences and the existing Codex process set. Functional and client lifecycle tests were run in disposable cloud environments.

[Windows installer acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43867345247)

## Shared core and desktop

Windows x64, macOS ARM64, and Linux x64 passed 21 shared-core checks per platform. Desktop acceptance used 30 fictional projects, 360 fictional chats, and attached-child fixtures.

Coverage included twelve languages, Arabic right-to-left layout, settings persistence, automatic sorting after additions and renaming, pinned and custom-section chats, manual ordering, preview reset, unchanged submissions, closing the window, worker single instance, reopening, and safe exit.

[Core acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43866519115) · [Desktop acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43867351898)

## Official client evidence

Windows client `26.1111.2228.0` passed ordinary window close, complete exit, reopening, companion independence, and startup after an update check. The main program and app archive hashes matched before and after the run. The cloud profile was signed out. An actual replacement with a different client version remains pending.

macOS granted Accessibility permission and returned 218 actual controls from the login screen. Frontmost application and pointer coordinates matched before and after the read, and client file hashes matched. Background sorting after authentication remains pending. Earlier local-tool requests were rejected with `untrusted-code-signing-identity`.

Linux returned its tool catalog. Signed-out list calls timed out, and signed-in real-sidebar sorting remains pending.

[Windows lifecycle](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43854854495) · [Mac Accessibility probe](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43864624646)

## Current follow-up work

- Authenticated real-client sorting acceptance.
- Background macOS ordering through an accepted interface.
- Linux real-sidebar synchronization.
- Actual client version replacement and subsequent cold startup.
- macOS and Linux consumer installers with installation and upgrade acceptance.
- Public installer delivery and a verified Agensi buyer path.

Cloud execution resumed after source publication. The current candidate results appear above. The locally installed companion remains 1.4.1, and local functional tests remain outside the owner's test scope.

## Marketplace status

The owner requested a USD 9.50 one-time Agensi listing. Submission preparation and marketplace review are recorded separately in the Agensi delivery folder.

[Agensi delivery](../agensi/README.md)
