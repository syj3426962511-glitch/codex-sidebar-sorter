# Codex Sidebar Sorter unified setup

Extract the entire ZIP to a folder and open **Install.html**. The setup page shows the detected system and offers all three platform choices. The ZIP includes the program runtime and native installers.

| System | Installer |
| --- | --- |
| Windows x64 | Open **Windows Setup** and choose your installation folder. |
| macOS, Apple Silicon or Intel | Open **Install macOS.command** from the extracted folder for automatic chip selection, or choose the matching disk image on the setup page. Both disk images are installation previews. |
| Linux x64 | Run the command shown on the setup page from the extracted folder. Use the native installer's folder chooser or `--prefix` to select a destination. |

## Select a system in the app

Start a compatible local Codex Desktop client and open **Codex Sidebar Sorter**. **System** is in the header. **Auto** selects the current operating system. Choosing its name explicitly reloads the list using that system's local connection. The app clears the preview when the setting changes, and a different system selection displays the current system needed for connection.

The same archive installs on each computer. The Windows companion connects through the local Windows named pipe. The macOS and Linux companions use their respective local sockets. Sidebar writes require the corresponding signed-in client to authorize the operation. Review the platform's exact acceptance scope in the [compatibility matrix](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/blob/main/docs/04-compatibility.md).
