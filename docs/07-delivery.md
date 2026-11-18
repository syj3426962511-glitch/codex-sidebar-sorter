# Install and use Codex Sidebar Sorter

The [GitHub Releases page](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases) provides one download entry for Windows, Linux and macOS previews. Each release lists the platform packages and the observed compatibility of that version.

| Platform | Package | Setup |
| --- | --- | --- |
| Windows x64 | `CodexSidebarSorter-<version>-Windows-Setup.exe` | Open Setup, choose a destination and start the companion from the Start menu |
| Linux x64 | `CodexSidebarSorter-<version>-Linux-x64-Setup.run` | Make the file executable and choose the installation directory |
| Intel and Apple Silicon Mac | `CodexSidebarSorter-<version>-macOS-x64-Candidate.dmg` or `-macOS-arm64-Candidate.dmg` | Select the file for your Mac processor and follow the installation steps for that candidate |

The installers bundle the companion runtime. Install Codex Desktop separately and sign in through its normal entry point. For the full compatibility scope and the macOS signing status, see [Compatibility](04-compatibility.md) and [Unix installation](06-unix-installers.md).

## Linux

Use the file from your release. Replace `<version>` with its actual version. A folder chooser is available on desktops with Zenity. The terminal option works with an explicit directory.

```sh
chmod +x CodexSidebarSorter-1.4.4-Linux-x64-Setup.run
./CodexSidebarSorter-1.4.4-Linux-x64-Setup.run --prefix "$HOME/Applications/CodexSidebarSorter"
```

Open **Codex Sidebar Sorter** in the applications menu after installing.

## Sort your sidebar

1. Open the companion while a compatible Codex Desktop client is running. Select **Refresh**.
2. Select **Projects** or choose an individual project or section in **Scope**.
3. Choose a rule and inspect the change preview. For automatic ascending names, leave **Keep names sorted A to Z** enabled.
4. Choose **Apply to sidebar** to save your preview. If the client changes the items in the scope before submission, refresh and review the updated list.
5. To return the preview to the last loaded order, select **Reset preview**. To keep applying name order after additions and renames, turn automatic sorting back on.

### Example

The project **Research Plan** contains **Task 20**, **Task 2**, and **Task 10**. Select **Natural numeric order** for its chats. The preview shows **Task 2**, **Task 10**, then **Task 20**. Select **Apply to sidebar** to save this order. To switch to continuous alphabetical ordering, enable **Keep names sorted A to Z**.

## Background operation and preferences

Closing the window leaves the worker and tray active. On Windows, click the tray icon or choose **Open Sidebar Sorter** to reopen the window. The window opens while the current refresh finishes. Select **Exit** to stop the companion. The worker waits for the client to become available after it closes. The companion's own preferences survive installation updates and removal.

Select **Language** to switch from the default English interface. The installer, release notes, public screenshots, and examples use English by default.

## Updating and removal

Install the new companion in the same destination to update it. Windows removes the companion through **Installed apps**. Linux reuses its installer with `--uninstall --prefix` and the installed directory. Mac candidates can be removed by quitting the companion and using their installer with `--uninstall --prefix` or removing the companion app bundle through Finder.

If companion 1.4.3 showed `list_threads (input schema)`, update to 1.4.4 and select **Refresh**. The companion loads local active history and follows the client's pages for available remote and ChatGPT history, with no fixed total chat count.

If sorting pauses because a client version changed, open the normal Codex Desktop and select **Refresh**. Include the client version, companion version, operating system, and visible status when you report a problem. Use the [compatibility matrix](04-compatibility.md) to find the latest checked builds.
