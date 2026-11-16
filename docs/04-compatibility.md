# Client-version compatibility

## Current evidence

The owner requires client-version compatibility and cloud-only functional acceptance. Each supported client build receives a recorded ordering and lifecycle result. Future client releases enter the compatibility matrix after their release and actual execution.

| Item | Current evidence |
| --- | --- |
| Accepted companion installer | Windows 1.4.1 from `036f029` |
| Official Windows client build | `26.1111.2228.0` lifecycle run |
| Authenticated Windows sidebar ordering | Cloud acceptance pending |
| Original client version replacement | Cloud acceptance pending |
| macOS and Linux real-sidebar ordering | Cloud acceptance pending |
| Candidate source | 1.4.2, history-schema compatibility changes awaiting cloud execution |

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

The expanded core suite contains 25 checks, including six history-schema generations. These cases check title fallback, name precedence, timestamp precision, early millisecond values, legacy project mapping, archived and agent filtering, attached-child filtering, and unchanged history bytes.

The earlier accepted suite contained 21 checks. Its older-schema fixture covered the attached-parent difference. The expanded candidate suite awaits cloud execution.

## Per-build release acceptance

For every build entered as supported, record the exact desktop build and operating system, authentication state, visible project and chat counts, seven ordering modes, additions and renaming, background operation, complete companion exit, client exit and reopening, and replacement with another client build.

A client authorization or contract change requires an adapter update and the affected regression cases. The product page publishes the matrix's actual supported builds and open results.

## Publication status

On November 16, 2026, the owner chose to retain cloud-only testing and wait for cloud recovery before paid publication. The English Agensi draft is saved at USD 9.50 one-time. Public installer delivery and submission follow the requested cloud acceptance.
