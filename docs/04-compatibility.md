# Client-version compatibility

## Current evidence

The owner requires client-version compatibility and cloud-only functional acceptance. Each supported client build receives a recorded ordering and lifecycle result. Future client releases enter the compatibility matrix after their release and actual execution.

| Item | Current evidence |
| --- | --- |
| Current public preview installer | Windows 1.4.2 from `43ee877`, cloud installed-upgrade acceptance passed |
| Locally installed companion | Windows 1.4.1 from `036f029` |
| Official Windows client build | `26.1111.2228.0` lifecycle run |
| Authenticated Windows sidebar ordering | Cloud acceptance pending |
| Original client version replacement | Cloud acceptance pending |
| macOS and Linux real-sidebar ordering | Cloud acceptance pending |
| Candidate source | 1.4.2, 26 shared-core checks passed on each cloud platform and Windows installed-upgrade acceptance passed |

## Existing integration mechanisms

The companion rediscovers local endpoints for each connection and reads the client's advertised tool catalog. Requests use the operations exposed by that catalog. The independent worker and tray have their own process lifecycle and settings.

History access uses SQLite read-only connections and explicit selected columns. The candidate inspects the published optional columns before constructing its read query. Additional unrelated columns leave that query usable.

## Historical schema changes

The official migration files establish the following field generations. The candidate regression set models each generation's consumed fields.

| Official migration stage | Reading behavior in the candidate |
| --- | --- |
| Initial thread table | Use title and the original update timestamp |
| Agent-path field added | Exclude nested-agent records through the available path |
| Millisecond timestamps added | Prefer millisecond precision with the original timestamp fallback |
| Saved-name field added | Prefer a non-empty saved name |
| Project identifier added | Resolve the database project identifier through the legacy mapping |
| Attached-parent field added | Exclude attached child records |

Before database project identifiers are available, project membership is read from the existing local assignment index. The timestamp expression follows the upstream migration's handling of both early second and millisecond values.

[Official initial thread schema](https://github.com/openai/codex/blob/662a634e88fcc809d77da393d75572c3d814cb27/codex-rs/state/migrations/0001_threads.sql) · [Agent path](https://github.com/openai/codex/blob/662a634e88fcc809d77da393d75572c3d814cb27/codex-rs/state/migrations/0022_threads_agent_path.sql) · [Timestamp migration](https://github.com/openai/codex/blob/662a634e88fcc809d77da393d75572c3d814cb27/codex-rs/state/migrations/0025_thread_timestamps_millis.sql) · [Saved names](https://github.com/openai/codex/blob/662a634e88fcc809d77da393d75572c3d814cb27/codex-rs/state/migrations/0041_threads_name.sql) · [Project identifiers](https://github.com/openai/codex/blob/662a634e88fcc809d77da393d75572c3d814cb27/codex-rs/state/migrations/0049_projects.sql)

## Candidate regression coverage

The expanded core suite contains 26 checks, including six history-schema generations and operation-name preflight. These cases check title fallback, name precedence, timestamp precision, early millisecond values, legacy project mapping, archived and agent filtering, attached-child filtering, and unchanged history bytes. The desktop regression checks a missing chat operation before a grouped project's first mutation.

The earlier accepted suite contained 21 checks. Its older-schema fixture covered the attached-parent difference. The expanded 26-check suite passed on Windows, macOS, and Linux in cloud run `43888466005`. The three desktop jobs passed in `43888466496`.

## Per-build release acceptance

For every build entered as supported, record the exact desktop build and operating system, authentication state, visible project and chat counts, seven ordering modes, additions and renaming, background operation, complete companion exit, client exit and reopening, and replacement with another client build.

A client authorization or contract change requires an adapter update and the affected regression cases. The product page publishes the matrix's actual supported builds and open results.

## Publication status

On November 16, 2026, the owner authorized direct GitHub and marketplace publication while retaining cloud-only testing. The source repository and Windows 1.4.2 preview installer are public. The English Agensi Windows companion skill is live at USD 9.50 one-time, with the preview scope and pending acceptance stated on its page. Authenticated real-sidebar acceptance, actual client version replacement, and production acceptance remain open. [Windows preview release](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview) · [Agensi product](https://www.agensi.io/skills/codex-sidebar-sorter-for-windows)

## Future-version design

The design specifies an adapter boundary at the existing native bridge, consumed-contract capability negotiation, per-scope batch preflight, signed data-only adapter updates, interrupted-batch recovery, and new-build cloud validation. Each mechanism's implementation status is recorded in the design. [Future compatibility design](05-future-compatibility.md) · [中文方案](05-未来版本适配方案.md)
