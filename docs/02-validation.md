# Validation record

Updated on November 18, 2026.

## November 18 Unix installation candidates

Self-contained Intel Mac and Linux x64 packages passed cloud installation, custom Unicode paths, bundled runtime and source, automatic sorting with fictional protocol fixtures, window close with continued background sorting, running replacement, preference retention, full exit and reopening, and removal. The packages were built from `291b894`. Their reports record `authenticatedOfficialClientSortingVerified=false`. The Apple Silicon installer job ended before any step ran because a hosted ARM64 runner could not be acquired. [Unix candidate packages and exact results](06-unix-installers.md)

## Current 1.4.2 cloud candidate

The 1.4.2 preview installer from `43ee877` includes the production changes from `fa6ec55`, which passed 26 core checks on each cloud platform and all three desktop jobs. Windows installed-upgrade acceptance passed for `43ee877`. The installer is 44,327,746 bytes. The six history-schema cases cover historical data generations, and the new regression checks missing-operation preflight before grouped mutations.

Windows installed acceptance used the production executable with fictional native-protocol fixtures. It covered the normal entry point, custom Unicode paths, bundled runtime and source, automatic sorting, older preferences, closing the window while the worker keeps sorting, upgrades, uninstall, and retained preferences. An approximately ten-second idle sample recorded 71,782,400 working-set bytes, 312.5 ms accumulated CPU time, and zero sorting writes.

[Core acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43888466005) · [Desktop acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43888466496) · [Windows installed-upgrade acceptance](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43888952292)

The public Windows preview download returned HTTP 200 without authentication and the expected 44,327,746-byte content length. [Windows preview release](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview)

The authorized local upgrade to `1.4.2+43ee877` completed with installer exit code 0 and retained companion settings. The existing Codex main window process remained alive. Local checks covered installed files and process state. Functional tests run in cloud hosts.

## Earlier Windows 1.4.1 package

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

The later authenticated Mac session reached the project pages and created two Accessibility test projects. The latest downloaded report has `desktopSignInRequired=false` and `fullAcceptancePassed=false`. Its project views still showed no chats. Full background sorting and its production integration remain open. The temporary session has ended. [Authenticated Mac session](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43897768093)

Linux returned its tool catalog. Signed-out list calls timed out, and signed-in real-sidebar sorting remains pending.

[Windows lifecycle](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43854854495) · [Mac Accessibility probe](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/43864624646)

## Current follow-up work

- Authenticated real-client sorting acceptance.
- Background macOS ordering through an accepted interface.
- Linux real-sidebar synchronization.
- Actual client version replacement and subsequent cold startup.
- macOS and Linux consumer installers with installation and upgrade acceptance.
- A verified paid-buyer transaction and licensed Agensi MCP delivery.
- Full parameter and response negotiation, signed adapter updates, and automated new-build validation.

Cloud execution resumed after source publication. The current candidate results appear above. The locally installed companion is now 1.4.2, and functional tests continue on disposable cloud hosts.

## Marketplace status

The Windows preview installer is public, and the Agensi companion skill is live as v1.4.2 at USD 9.50 one-time. The listing explains the purchase contents, supported scope, remaining real-client acceptance, and planned adaptation mechanisms. The November 17 compatibility review records the exact evidence and remaining per-build work. [Compatibility review](04-compatibility.md#compatibility-review-on-november-17-2026)

[Agensi delivery](../agensi/README.md)
