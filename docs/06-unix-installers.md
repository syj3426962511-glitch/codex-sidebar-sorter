# Linux installer and macOS installation candidates

Updated on November 18, 2026.

The self-contained Linux x64 installer passed installation and authenticated official-client acceptance. Intel Mac has an installation candidate with passing companion installation and lifecycle checks. Mac official-client ordering remains in acceptance.

[Download the Mac installation candidate](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-unix-candidate)

[Download the accepted Linux preview installer](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/download/v1.4.2-preview/CodexSidebarSorter-1.4.2-Linux-x64-Setup.run)

## Packages

| Package | Size in bytes | Architecture |
| --- | ---: | --- |
| CodexSidebarSorter-1.4.2-macOS-x64-Candidate.dmg | 54,841,993 | Intel Mac |
| CodexSidebarSorter-1.4.2-Linux-x64-Setup.run | 93,839,351 | Linux x64 |

Both packages contain the .NET runtime, application icon, twelve interface languages, native tray component, and source from commit `291b894`. Linux also bundles its indicator dependencies, ICU, and SQLite. The companion uses the installed Codex client's existing local interfaces.

The Mac candidate has an ad hoc local signature. Developer ID signing and notarization remain part of the consumer release work. The Apple Silicon installer job ended before executing a step because GitHub could not allocate an ARM64 host. Its installation result remains pending.

## Installation

On Intel Mac, open the DMG and run **Install Codex Sidebar Sorter.command** to choose an application directory. The application can also be copied from the DMG to a selected folder.

On Linux, make the installer executable and run it. A desktop folder chooser is available when Zenity is installed. Terminal installation accepts a destination directly.

```sh
chmod +x CodexSidebarSorter-1.4.2-Linux-x64-Setup.run
./CodexSidebarSorter-1.4.2-Linux-x64-Setup.run --prefix "$HOME/Applications/CodexSidebarSorter"
```

Run the installer again with the same destination to replace an existing companion. The installer asks its running companion to finish and exit before replacement. User preferences are stored outside the application directory and retained during upgrades and removal.

```sh
./CodexSidebarSorter-1.4.2-Linux-x64-Setup.run --uninstall --prefix "$HOME/Applications/CodexSidebarSorter"
```

## Cloud installation results

Both accepted jobs exercised the production application from the installer, including a destination containing Chinese characters and spaces, bundled runtime and source, automatic ascending-name ordering, additions and renaming after window close, replacement while the companion was running, retained settings, full exit and reopening, and removal with preferences retained.

The fixture contained 30 fictional projects and 360 fictional chats. The report explicitly records `authenticatedOfficialClientSortingVerified=false`.

An approximately ten-second idle sample recorded 64,385,024 bytes and 313.19 ms accumulated CPU time on Intel Mac, and 177,025,024 bytes and 190 ms on Linux. Both samples recorded zero sorting writes.

[Accepted Intel Mac installation job](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44122917691/job/134178845211) · [Accepted Linux installation job](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44122917691/job/134178845529)

## Official-client acceptance

The Mac acceptance driver now creates its test chats through the desktop's own app-server connection and tests process-targeted Accessibility dragging while recording the foreground application and pointer position. Its production integration and successful background sorting remain open. The Intel workflow now selects the official x64 distribution. Client signature checks and Accessibility-helper preparation passed. Its latest temporary device authorization ended before login completed, so authenticated background ordering remains open.

The Linux authenticated workflow completed successfully on Ubuntu 26.04.1 x64 with official client 26.1111.21308. Two local test projects and six chats passed all seven ordering modes with saved-order read-back. The published installer passed background renaming with its window closed, retained worker after client exit, automatic recovery after client reopening, and companion removal while the official client stayed running. The original executable and archive matched. Actual client version replacement remains pending. [Authenticated Linux result](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44125514300)

The live Agensi product currently retains its preceding Windows companion skill. The Windows and Linux skill update is prepared, and marketplace submission is awaiting a working authenticated browser connection. Mac consumer delivery remains open.
