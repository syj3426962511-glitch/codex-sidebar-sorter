"""Assemble one offline installer from the validated native packages."""

import argparse
import hashlib
import json
from pathlib import Path
import stat
import xml.etree.ElementTree as ET
import zipfile


def add_generated(archive, name, content, executable=False):
    entry = zipfile.ZipInfo(name)
    entry.create_system = 3
    entry.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
    archive.writestr(entry, content, compress_type=zipfile.ZIP_DEFLATED)


def assemble(native, output):
    root = Path(__file__).resolve().parents[1]
    version = ET.parse(root / "desktop/SidebarSorter.Desktop.csproj").findtext(".//Version")
    installers = {
        "win-x64": f"CodexSidebarSorter-{version}-Windows-Setup.exe",
        "osx-arm64": f"CodexSidebarSorter-{version}-macOS-arm64-Candidate.dmg",
        "osx-x64": f"CodexSidebarSorter-{version}-macOS-x64-Candidate.dmg",
        "linux-x64": f"CodexSidebarSorter-{version}-Linux-x64-Setup.run",
    }
    payloads = {}
    manifest = {"version": version, "entry": "Install.html", "installers": {}}
    for architecture, name in installers.items():
        matches = list(native.rglob(name))
        if len(matches) != 1 or not matches[0].is_file() or matches[0].stat().st_size == 0:
            raise ValueError(f"Expected one nonempty installer for {architecture}")
        payload = matches[0]
        location = f"Installers/{name}"
        payloads[location] = payload
        with payload.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        manifest["installers"][architecture] = {"path": location, "size": payload.stat().st_size, "sha256": digest}

    output.mkdir(parents=True, exist_ok=True)
    target = output / f"CodexSidebarSorter-{version}-Universal-Setup.zip"
    with zipfile.ZipFile(target, "w") as archive:
        for source, destination in (("Install.html", "Install.html"), ("install-macos.sh", "Install macOS.command")):
            text = (root / "packaging/integrated" / source).read_text(encoding="utf-8").replace("@VERSION@", version)
            add_generated(archive, destination, text, executable=destination.endswith(".command"))
        add_generated(archive, "manifest.json", json.dumps(manifest, indent=2) + "\n")
        archive.write(root / "src/Assets/sidebar-sorter-logo-256.png", "logo.png")
        archive.write(root / "docs/08-integrated-installation.md", "Installation-Guide.en.md", zipfile.ZIP_DEFLATED)
        archive.write(root / "LICENSE", "LICENSE", zipfile.ZIP_DEFLATED)
        for location, payload in payloads.items():
            archive.write(payload, location, zipfile.ZIP_STORED)
    print(target)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("native", type=Path, help="Directory containing the four native installer artifacts")
    parser.add_argument("output", type=Path, help="Destination directory for the offline ZIP")
    args = parser.parse_args()
    assemble(args.native, args.output)
