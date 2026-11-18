#!/bin/bash
set -euo pipefail

if [ "$(uname -s)" != Darwin ]; then
    printf '%s\n' "Run this launcher on macOS." >&2
    exit 2
fi
if [ "$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null || true)" = 1 ]; then
    chip=arm64
else
    chip=x64
fi
folder=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
image="$folder/Installers/CodexSidebarSorter-@VERSION@-macOS-$chip-Candidate.dmg"
mount=$(mktemp -d "${TMPDIR:-/tmp}/sidebar-sorter-dmg.XXXXXX")
mounted=false
cleanup() {
    if [ "$mounted" = true ]; then hdiutil detach "$mount" >/dev/null; fi
    rmdir "$mount"
}
trap cleanup EXIT
hdiutil attach "$image" -readonly -nobrowse -mountpoint "$mount" >/dev/null
mounted=true
printf 'macOS installer: %s\n' "$chip"
bash "$mount/Install Codex Sidebar Sorter.command" "$@"
