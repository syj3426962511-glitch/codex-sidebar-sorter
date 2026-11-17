#!/bin/bash
set -euo pipefail

base=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
prefix=""
launch=true
uninstall=false
while [ "$#" -gt 0 ]; do
    case "$1" in
        --prefix) prefix=${2:?An installation directory is required}; shift 2 ;;
        --no-launch) launch=false; shift ;;
        --uninstall) uninstall=true; shift ;;
        *) printf 'Unknown argument %s\n' "$1" >&2; exit 2 ;;
    esac
done

if [ "$(uname -s)" = Darwin ]; then
    source="$base/Codex Sidebar Sorter.app"
    marker="Contents/Resources/.sidebar-sorter-install"
    executable="Contents/MacOS/SidebarSorter"
    if [ -z "$prefix" ]; then
        parent=$(osascript -e 'POSIX path of (choose folder with prompt "Choose an installation folder for Codex Sidebar Sorter")')
        prefix="${parent%/}/Codex Sidebar Sorter.app"
    fi
else
    source="$base/payload"
    marker=".sidebar-sorter-install"
    executable="Start Sidebar Sorter"
    if [ -z "$prefix" ]; then
        prefix="$HOME/.local/opt/codex-sidebar-sorter"
        if command -v zenity >/dev/null 2>&1 && [ -n "${DISPLAY:-}" ]; then
            parent=$(zenity --file-selection --directory --title="Choose an installation folder")
            prefix="${parent%/}/codex-sidebar-sorter"
        elif [ -t 0 ]; then
            read -r -p "Installation directory [$prefix] " selected
            prefix=${selected:-$prefix}
        fi
    fi
fi

case "$prefix" in /*) ;; *) printf 'Choose an absolute installation directory.\n' >&2; exit 2 ;; esac
prefix=${prefix%/}
if [ -z "$prefix" ] || [ -L "$prefix" ]; then
    printf 'Choose a regular application directory.\n' >&2
    exit 2
fi
if [ -e "$prefix" ]; then
    if [ ! -f "$prefix/$marker" ] || [ "$(head -n 1 "$prefix/$marker")" != CodexSidebarSorter ]; then
        printf 'The selected directory belongs to another application.\n' >&2
        exit 2
    fi
    "$prefix/$executable" --exit
fi

if [ "$uninstall" = true ]; then
    [ -f "$prefix/$marker" ] || { printf 'No installed companion was found.\n' >&2; exit 2; }
    rm -rf -- "$prefix"
    desktop="${XDG_DATA_HOME:-$HOME/.local/share}/applications/codex-sidebar-sorter.desktop"
    if [ -f "$desktop" ] && grep -Fqx "X-SidebarSorter-Directory=$prefix" "$desktop"; then
        rm -- "$desktop"
    fi
    printf 'Codex Sidebar Sorter removed. User preferences retained.\n'
    exit 0
fi

[ -d "$source" ] || { printf 'The installer payload is missing.\n' >&2; exit 2; }
parent=$(dirname -- "$prefix")
mkdir -p -- "$parent"
staging=$(mktemp -d "$parent/.sidebar-sorter-install.XXXXXX")
trap 'rm -rf -- "$staging"' EXIT HUP INT TERM
if [ "$(uname -s)" = Darwin ]; then
    ditto "$source" "$staging/new"
else
    cp -a -- "$source" "$staging/new"
fi
if [ -e "$prefix" ]; then mv -- "$prefix" "$staging/previous"; fi
if ! mv -- "$staging/new" "$prefix"; then
    if [ -d "$staging/previous" ] && ! mv -- "$staging/previous" "$prefix"; then
        trap - EXIT HUP INT TERM
        printf 'The preceding installation is retained in %s\n' "$staging/previous" >&2
    fi
    exit 1
fi
if [ "$(uname -s)" != Darwin ]; then
    desktop_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    mkdir -p -- "$desktop_dir"
    escaped=$(printf '%s' "$prefix" | sed 's/\\/\\\\/g; s/"/\\"/g; s/`/\\`/g; s/\$/\\$/g; s/%/%%/g')
    cat > "$desktop_dir/codex-sidebar-sorter.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Codex Sidebar Sorter
Exec="$escaped/Start Sidebar Sorter"
Icon=$prefix/tray.png
Terminal=false
Categories=Utility;
X-SidebarSorter-Directory=$prefix
EOF
    chmod a+rw "$desktop_dir/codex-sidebar-sorter.desktop"
fi
printf 'Installed Codex Sidebar Sorter in %s\n' "$prefix"
if [ "$launch" = true ]; then
    nohup "$prefix/$executable" >/dev/null 2>&1 </dev/null &
fi
