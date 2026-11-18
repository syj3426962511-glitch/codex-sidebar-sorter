# Codex Sidebar Sorter

Codex Sidebar Sorter is an independent desktop companion for organizing projects and chats in the Codex sidebar. Preview the order, select from seven methods, or keep project and chat names in ascending order automatically.

![Codex Sidebar Sorter showing English sample data](docs/assets/sidebar-sorter-english.png)

[Download 1.4.4](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.4-preview) · [Installation and user guide](docs/07-delivery.md) · [Compatibility and validation](docs/04-compatibility.md) · [Agensi](https://www.agensi.io/skills/codex-sidebar-sorter)

## Start here

1. Choose your platform on the release page. Check the exact client and platform evidence in the release notes before installing.
2. Start and sign in to a compatible Codex Desktop client on the same computer.
3. Open Codex Sidebar Sorter and select **Refresh**. The application opens in English on a new installation.
4. Leave **Keep names sorted A to Z** on for continuous sorting. Choose another rule and select **Apply to sidebar** to save a specific order.

The companion includes its runtime and keeps its own preferences. Closing its window leaves the background worker and tray active. Use **Open Sidebar Sorter** in the tray to reopen it, or **Exit** to stop the companion.

## Ordering and languages

| Mode | How it orders items |
| --- | --- |
| Name A to Z and Z to A | Compare names, ignoring case |
| Natural numeric order and reverse natural order | Put Task 2 before Task 10 in ascending order |
| Recently updated and oldest update first | Use the latest known update time for each item. Missing times sort last |
| Manual order | Drag rows within the same group or use the movement buttons |

The scope picker includes supported projects, project chats, pinned chats and custom sections. The companion reads all available pages of active history and uses the client's page size. Search and the project and chat tabs help you review large lists. Archived records and attached child sessions are outside the active chat count. Some chats can be displayed with limited context when the installed client exposes partial data.

The optional interface languages are English, Simplified Chinese, Traditional Chinese, Japanese, Korean, French, German, Spanish, Portuguese, Italian, Russian, and Arabic. Arabic uses a right-to-left layout. **System language** follows your operating system after you select it.

## Compatibility

The companion runs independently of the client. Synchronizing the original sidebar requires an authorized local interface on that client build. The companion checks operation names, schema information when available, and the current scope membership before submitting a change. Preview and connection status describe what your current installation can expose.

Ubuntu 26.04.1 x64 and official client 26.1111.21308 have authenticated seven-mode evidence with the published 1.4.2 release. Windows has signed-out client lifecycle evidence and installed sorting results with isolated test data. Mac packages are installation previews. Consult the matrix for the current release, including builds and outcomes for each test. [Compatibility matrix](docs/04-compatibility.md)

People who purchase the optional Agensi skill receive an English agent guide through Agensi. Desktop downloads are available through GitHub. [Agensi delivery](agensi/README.md)

## Build and contribute

Use .NET 10. Platform packaging and cloud acceptance commands are recorded in [Development and build](docs/01-development.md).

MIT. See [LICENSE](LICENSE).
