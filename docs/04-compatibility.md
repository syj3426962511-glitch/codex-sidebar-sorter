# Client compatibility

The compatibility matrix records the exact combination of companion version, operating system, and Codex client used for each check. A new client build has its own result. The companion continues to run independently while the client is closed.

| Platform | Real client evidence | Package scope |
| --- | --- | --- |
| Ubuntu 26.04.1 x64 | Codex 26.1111.21308 passed signed-in ordering in all seven modes with the published 1.4.2 installer | Linux x64 preview |
| Windows x64 | Codex 26.1111.2228.0 passed signed-out lifecycle and Store update check with the preceding companion. Signed-in ordering and a changed Store client version still require validation | Windows x64 preview |
| Intel Mac | The installed companion has fixture coverage. The client rejected the earlier external ordering connection. Signed-in sidebar ordering remains open | Intel Mac installation preview |
| Apple Silicon Mac | The installed companion has fixture coverage for the new arm64 package. Signed-in sidebar ordering remains open | Apple Silicon installation preview |

[Exact workflow evidence](02-validation.md) · [Official Linux result](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/actions/runs/44125514300)

## Supported sidebar scope

The bridge can apply ordering to eligible projects, chats in loaded Codex projects, pinned chats and supported custom sections. Independent alphabetical Recents, pinned project placement, and ChatGPT project-chat ordering require corresponding client capabilities. When the client reports project ownership or loading issues, open that project in Codex and refresh the companion.

The history reader uses a read-only SQLite connection and recognizes six tested historical schema shapes. These are isolated schema fixtures. Use the table above for authenticated client builds.

## What happens after a client update

The worker observes local history changes and newly available client connections. Each submission reads the current sidebar and membership, checks every required operation and validates the advertised input shape when available. If data or the expected operation changes, the companion presents a refresh or compatibility status. Existing preferences and the ordinary Codex launcher remain available.

We run new builds through cloud core, desktop, installed package and client acceptance. Automatic delivery of independently signed adapters and full client-contract negotiation are future work. [Future compatibility design](05-future-compatibility.md)
