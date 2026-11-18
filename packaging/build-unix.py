"""Build self-contained Unix installers on the corresponding disposable cloud host."""

import io
import os
import pathlib
import plistlib
import re
import shutil
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET


if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform not in ("darwin", "linux"):
    raise RuntimeError("Build Unix candidates on a disposable cloud host.")

repository = pathlib.Path(__file__).resolve().parent.parent
os.chdir(repository)
version = ET.parse("desktop/SidebarSorter.Desktop.csproj").findtext(".//Version")
rid = sys.argv[1]
expected = "osx-" if sys.platform == "darwin" else "linux-"
if not rid.startswith(expected):
    raise ValueError("The package architecture must match its build platform.")
output = repository / "artifacts/unix-package"
output.mkdir(parents=True, exist_ok=True)
work = pathlib.Path(os.environ["RUNNER_TEMP"]) / ("sorter-package-" + rid)
work.mkdir(exist_ok=True)
stage = work / "image"
stage.mkdir(exist_ok=True)
is_mac = sys.platform == "darwin"
application = stage / "Codex Sidebar Sorter.app" if is_mac else stage / "payload"
program = application / "Contents/MacOS" if is_mac else application
resources = application / "Contents/Resources" if is_mac else application
program.mkdir(parents=True, exist_ok=True)
resources.mkdir(parents=True, exist_ok=True)


def run(*arguments):
    subprocess.run(arguments, check=True)


run("dotnet", "publish", "desktop/SidebarSorter.Desktop.csproj", "-c", "Release", "-r", rid,
    "--self-contained", "true", "-o", str(program))
source = resources / "Source"
source.mkdir()
tracked = subprocess.check_output(["git", "archive", "--format=tar", "HEAD"])
with tarfile.open(fileobj=io.BytesIO(tracked)) as archive:
    archive.extractall(source, filter="data")
(resources / ".sidebar-sorter-install").write_text("CodexSidebarSorter\n" + version + "\n")
installer = stage / ("Install Codex Sidebar Sorter.command" if is_mac else "install.sh")
shutil.copy2("packaging/install-unix.sh", installer)
installer.chmod(0o755)

if is_mac:
    architecture = {"osx-arm64": "arm64", "osx-x64": "x86_64"}[rid]
    run("clang", "-arch", architecture, "-O2", "-fobjc-arc", "-dynamiclib", "-framework", "Cocoa",
        "desktop/NativeTray/macos.m", "-o", str(program / "libsorter_tray.dylib"))
    icons = work / "Sorter.iconset"
    icons.mkdir(exist_ok=True)
    for size in (16, 32, 128, 256, 512):
        run("sips", "-z", str(size), str(size), "src/Assets/sidebar-sorter-logo-256.png",
            "--out", str(icons / ("icon_" + str(size) + "x" + str(size) + ".png")))
    run("iconutil", "-c", "icns", str(icons), "-o", str(resources / "Sorter.icns"))
    info = {
        "CFBundleIdentifier": "io.github.syj3426962511.codex-sidebar-sorter",
        "CFBundleName": "Codex Sidebar Sorter", "CFBundleDisplayName": "Codex Sidebar Sorter",
        "CFBundleExecutable": "SidebarSorter", "CFBundleIconFile": "Sorter.icns",
        "CFBundlePackageType": "APPL", "CFBundleShortVersionString": version,
        "CFBundleVersion": version, "LSMinimumSystemVersion": "13.0",
        "NSHighResolutionCapable": True,
    }
    with (application / "Contents/Info.plist").open("wb") as stream:
        plistlib.dump(info, stream)
    run("chmod", "-R", "a+rX,go-w", str(application))
    # Candidates use a local signature. Developer ID distribution is a separate release step.
    run("codesign", "--force", "--deep", "--sign", "-", str(application))
    run("codesign", "--verify", "--deep", "--strict", str(application))
    (stage / "Applications").symlink_to("/Applications")
    package = output / ("CodexSidebarSorter-" + version + "-macOS-" + rid.removeprefix("osx-") + "-Candidate.dmg")
    run("hdiutil", "create", "-ov", "-volname", "Codex Sidebar Sorter", "-srcfolder", str(stage),
        "-format", "UDZO", str(package))
else:
    flags = subprocess.check_output(["pkg-config", "--cflags", "--libs", "ayatana-appindicator3-0.1"], text=True).split()
    run("cc", "-O2", "-shared", "-fPIC", "desktop/NativeTray/linux.c", "-o",
        str(program / "libsorter_tray.so"), *flags)
    libraries = program / "lib"
    libraries.mkdir()
    for native in program.glob("*.so"):
        linked = subprocess.run(["ldd", str(native)], capture_output=True, text=True, check=False)
        for library in re.findall(r"=> (/\S+)", linked.stdout):
            name = pathlib.Path(library).name
            if re.match(r"(?:libc|libm|libpthread|libdl|librt|libresolv)\.so", name):
                continue
            shutil.copy2(library, libraries / name)
    for pattern in ("libicu*.so.*", "libsqlite3.so.*"):
        for library in pathlib.Path("/usr/lib/x86_64-linux-gnu").glob(pattern):
            shutil.copy2(library, libraries / library.name)
    launcher = program / "Start Sidebar Sorter"
    launcher.write_text('#!/bin/sh\nset -eu\nroot=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)\n'
                        'export LD_LIBRARY_PATH="$root/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"\n'
                        'exec "$root/SidebarSorter" "$@"\n')
    launcher.chmod(0o755)
    run("chmod", "-R", "a+rX,go-w", str(application))
    payload = work / "payload.tar.gz"
    with tarfile.open(payload, "w:gz") as archive:
        archive.add(application, arcname="payload")
        archive.add(installer, arcname="install.sh")
    package = output / ("CodexSidebarSorter-" + version + "-Linux-x64-Setup.run")
    header = '''#!/bin/sh
set -eu
temporary=$(mktemp -d "${TMPDIR:-/tmp}/sidebar-sorter.XXXXXX")
trap 'rm -rf -- "$temporary"' EXIT HUP INT TERM
payload_line=$(awk '/^__SORTER_PAYLOAD__$/ {print NR + 1; exit}' "$0")
tail -n +"$payload_line" "$0" | tar -xz -C "$temporary"
bash "$temporary/install.sh" "$@"
exit $?
__SORTER_PAYLOAD__
'''
    with package.open("wb") as stream:
        stream.write(header.encode())
        with payload.open("rb") as data:
            shutil.copyfileobj(data, stream)
    package.chmod(0o755)
print("Installer created at " + str(package))
