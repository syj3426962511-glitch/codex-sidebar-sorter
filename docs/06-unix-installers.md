# macOS and Linux installation candidates

Updated on November 18, 2026.

Self-contained macOS Intel and Linux x64 installation candidates are available for review. Their installation and companion lifecycle checks passed on disposable cloud hosts using fictional native-protocol fixtures. Authenticated ordering in the official Codex sidebar remains in acceptance.

[Download the installation candidates](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-unix-candidate)

## Packages

| Package | Size in bytes | Architecture |
| --- | ---: | --- |
| CodexSidebarSorter-1.4.2-macOS-x64-Candidate.dmg | 54,841,993 | Intel Mac |
| CodexSidebarSorter-1.4.2-Linux-x64-Candidate.run | 93,839,351 | Linux x64 |

Both packages contain the .NET runtime, application icon, twelve interface languages, native tray component, and source from commit `291b894`. Linux also bundles its indicator dependencies, ICU, and SQLite. The companion uses the installed Codex client's existing local interfaces.

The Mac candidate has an ad hoc local signature. Developer ID signing and notarization remain part of the consumer release work. The Apple Silicon installer job ended before executing a step because GitHub could not allocate an ARM64 host. Its installation result remains pending.

## Installation

On Intel Mac, open the DMG and run **Install Codex Sidebar Sorter.command** to choose an application directory. The application can also be copied from the DMG to a selected folder.

On Linux, make the installer executable and run it. A desktop folder chooser is available when Zenity is installed. Terminal installation accepts a destination directly.

```sh
chmod +x CodexSidebarSorter-1.4.2-Linux-x64-Candidate.run
./CodexSidebarSorter-1.4.2-Linux-x64-Candidate.run --prefix "$HOME/Applications/CodexSidebarSorter"
```

Run the installer again with the same destination to replace an existing companion. The installer asks its running companion to finish and exit before replacement. User preferences are stored outside the application directory and retained during upgrades and removal.

```sh
./CodexSidebarSorter-1.4.2-Linux-x64-Candidate.run --uninstall --prefix "$HOME/Applications/CodexSidebarSorter"
```

## Cloud installation results

Both accepted jobs exercised the production application from the installer, including a destination containing Chinese characters and spaces, bundled runtime and source, automatic ascending-name ordering, additions and renaming after window close, replacement while the companion was running, retained settings, full exit and reopening, and removal with preferences retained.

The fixture contained 30 fictional projects and 360 fictional chats. The report explicitly records `authenticatedOfficialClientSortingVerified=false`.

An approximately ten-second idle sample recorded 64,385,024 bytes and 313.19 ms accumulated CPU time on Intel Mac, and 177,025,024 bytes and 190 ms on Linux. Both samples recorded zero sorting writes.

[Accepted Intel Mac installation job](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44122917691/job/134178845211) · [Accepted Linux installation job](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44122917691/job/134178845529)

## Official-client acceptance

The Mac acceptance driver now creates its test chats through the desktop's own app-server connection and tests process-targeted Accessibility dragging while recording the foreground application and pointer position. Its production integration and successful background sorting remain open. The original Mac download used in earlier ARM64 runs provides an ARM64 executable, so the Intel official-client run needs the matching Intel distribution.

The Linux acceptance workflow uses a fresh official device authorization on a temporary host. It creates two local test projects and six chats and checks all seven ordering modes through the production sorting service, with saved-order read-back. The workflow is awaiting authorization at this update. Its remaining acceptance work includes real-client lifecycle and independent background operation.

The Agensi purchase currently covers the published Windows companion skill. Mac and Linux consumer delivery will be added after their official-client acceptance and platform distribution work are complete.
