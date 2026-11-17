# Codex Sidebar Sorter 1.4.2

Organize Codex projects and chats with seven sorting modes, twelve interface languages and an independent background worker.

## Choose an installer

| Platform | Included file | Validation |
| --- | --- | --- |
| Windows x64 | Windows/CodexSidebarSorter-1.4.2-Windows-Setup.exe | Installation, running replacement, settings retention, background operation, reopening and removal passed. Official-client lifecycle used a signed-out profile |
| Ubuntu 26.04.1 x64 | Linux/CodexSidebarSorter-1.4.2-Linux-x64-Setup.run | Official client 26.1111.21308 passed authenticated seven-mode ordering, automatic background changes, client exit and reopening, and removal while the client stayed running |

The installers contain their runtimes, icons, languages and source. Install on the same computer as a compatible, signed-in Codex Desktop.

Intel Mac has a separately downloadable installation candidate. Its installation and companion lifecycle passed. Authenticated Mac sidebar ordering, production Accessibility integration, Apple Silicon installation acceptance and distribution signing remain open.

[Mac installation candidate](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/download/v1.4.2-preview/CodexSidebarSorter-1.4.2-macOS-x64-Candidate.dmg)

## Install

On Windows, run the EXE and choose an installation directory. Open the companion from its shortcut.

On Linux, make the installer executable and select a destination.

```sh
chmod +x CodexSidebarSorter-1.4.2-Linux-x64-Setup.run
./CodexSidebarSorter-1.4.2-Linux-x64-Setup.run --prefix "$HOME/Applications/CodexSidebarSorter"
```

A folder chooser is available when Zenity is present. The installer creates a desktop application entry. Reusing the same destination replaces the companion and retains its preferences.

## Connect and sort

1. Open a compatible, signed-in Codex Desktop.
2. Open Codex Sidebar Sorter and select Refresh.
3. Review the listed projects and chats.
4. Keep names sorted A to Z is enabled by default. New or renamed items trigger another synchronization.
5. To apply another order, select a scope and sorting rule, review the preview and select Apply to sidebar.

Available modes are ascending names, descending names, natural numeric order, reverse natural order, recently updated first, oldest updated first and manual order. Custom rules and manual moves pause automatic alphabetical sorting. Re-enable Keep names sorted A to Z to resume it.

## Language and background use

Select Simplified Chinese, Traditional Chinese, English, Japanese, Korean, French, German, Spanish, Portuguese, Italian, Russian or Arabic. Arabic uses right-to-left layout.

Close the companion window to retain its worker and tray. Open Sidebar Sorter in the tray reopens the interface. Exit stops the companion. The worker waits for an available client connection while Codex is closed.

## Agent skill

AgentSkill/codex-sidebar-sorter contains the optional agent instructions, compatibility reference and MIT license. Agensi supplies the same skill through its connection. The standalone companion provides its own sorting interface and worker.

## Support scope

Eligible unpinned projects, supported Codex project chats, and chats in pinned or custom sections have ordering support. Independent alphabetical Recents ordering, pinned project placement and ChatGPT project-chat ordering need additional client support. Actual client version replacement and additional client builds have separate acceptance work.

The Linux real-client acceptance used two isolated test projects and six chats. Shared desktop regression used 30 fictional projects and 360 fictional chats.

[Releases](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview) · [Validation](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/blob/main/docs/02-validation.md) · [Compatibility](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/blob/main/docs/04-compatibility.md)
